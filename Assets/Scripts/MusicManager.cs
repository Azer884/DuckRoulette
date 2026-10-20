using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;
using Unity.Netcode;

public class MusicManager : MonoBehaviour
{
    public static MusicManager Instance { get; private set; }
    [SerializeField] private AudioMixer audioMixer;
    [SerializeField] private float fadeDuration = 2f; // Duration for the crossfade effect
    private AudioSource musicSource;
    [SerializeField, Tooltip("Element 0 is the lobby song. Every other element is an in-game track: " +
        "the boombox plays them in this order and the Change Music key cycles through them.")]
    private AudioClip[] musicClips;

    private AudioClip[] gameTracks;

    /// <summary>The in-game tracks (every music clip except the lobby one). The boombox plays these.</summary>
    public AudioClip[] GameTracks
    {
        get
        {
            if (gameTracks == null)
            {
                gameTracks = musicClips != null && musicClips.Length > 1 ? musicClips[1..] : Array.Empty<AudioClip>();
            }
            return gameTracks;
        }
    }

    [Header("Game scene: music comes from the boombox")]
    [SerializeField, Tooltip("Seconds for the music to go from flat 2D to positional 3D, still at " +
        "the player, before it starts moving.")]
    private float spatialFadeSeconds = 2.5f;
    [SerializeField, Tooltip("Seconds the music takes to travel from the player to the boombox.")]
    private float travelSeconds = 10f;
    [SerializeField, Tooltip("Seconds to blend between 3D (at the box) and 2D (everywhere) when a " +
        "map-wide moment starts or ends.")]
    private float broadcastBlendSeconds = 2f;
    [SerializeField, Tooltip("Full volume within this many meters of the boombox.")]
    private float minDistance = 4f;
    [SerializeField, Tooltip("Silent past this many meters from the boombox.")]
    private float maxDistance = 30f;
    [SerializeField, Tooltip("How fast the music fades between min and max distance. 1 = linear, " +
        "higher drops off sooner, so it is only loud close to the box.")]
    private float falloffPower = 2f;
    [SerializeField] private float trackCrossfadeSeconds = 1f;

    [Header("Behind walls (same effect as proximity voice chat)")]
    [SerializeField, Tooltip("Layers that can block the boombox. What each object does to the sound " +
        "comes from its SoundOccluder, or is guessed from its collider when it has none.")]
    private LayerMask wallLayerMask = 1; // Default
    [SerializeField] private float muffledCutoff = 448f;
    [SerializeField, Tooltip("Roughly how many seconds the muffle takes to fade in or out.")]
    private float muffleBlendSeconds = 0.3f;
    [SerializeField, Tooltip("Seconds between occlusion checks. The filter still eases every frame.")]
    private float muffleCheckInterval = 0.1f;

    private OcclusionFilter occlusion;
    private SoundOcclusion.Amount occlusionTarget;
    private float nextOcclusionCheck;
    private bool nextTrackRequested;

    // Volume the fades drive; LateUpdate combines it with the behind-a-wall muffle.
    private float fadeLevel = 1f;

    private Coroutine followRoutine;
    private Coroutine fadeRoutine;
    private BumBox followedBox;
    private float baseVolume = 1f;
    
    // Store delegate references to allow proper unsubscription
    private Action pauseHandler;
    private Action unpauseHandler;
    
    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject); // Make sure this object persists across scenes
            musicSource = GetComponent<AudioSource>();
            baseVolume = musicSource.volume;
            fadeLevel = baseVolume;

            occlusion = new OcclusionFilter(gameObject, muffledCutoff);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void OnEnable()
    {
        // Create and store delegate references instead of using lambdas
        pauseHandler = () => PauseMusic(.5f);
        unpauseHandler = () => UnPauseMusic(.5f);
        
        PauseMenu.OnPause += pauseHandler;
        PauseMenu.OnUnPause += unpauseHandler;
        SceneManager.sceneLoaded += PlayMusic;
        BumBox.TrackChanged += OnBoxTrackChanged;
    }

    private void PlayMusic(Scene scene, LoadSceneMode loadMode)
    {
        // Only a real scene change counts, and only for the surviving singleton.
        if (loadMode != LoadSceneMode.Single || this != Instance)
        {
            return;
        }

        StopFollowing();
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(CrossfadeMusic(scene, fadeDuration));

        if (scene.name == "GameScene")
        {
            followRoutine = StartCoroutine(FollowBoombox());
        }
    }

    // In the match the music doesn't play "in your head": it turns positional where you stand,
    // then drifts over to the boombox and plays from there - loud near it, fading with distance.
    // Now and then the box blasts it across the whole map (2D for everyone) before it goes back.
    private IEnumerator FollowBoombox()
    {
        BumBox box = null;
        while (box == null)
        {
            box = BumBox.Primary;
            yield return null;
        }

        followedBox = box;
        box.SetDrivenBy(musicSource);

        ApplyRolloff();
        musicSource.dopplerLevel = 0f;

        if (box.CurrentClip != null && box.CurrentClip != musicSource.clip)
        {
            CrossfadeTo(box.CurrentClip);
        }

        // 1. Turn 3D right where the player is, so nothing audibly changes yet.
        for (float t = 0f; t < spatialFadeSeconds && box != null; t += Time.deltaTime)
        {
            transform.position = ListenerPosition();
            musicSource.spatialBlend = t / spatialFadeSeconds;
            yield return null;
        }

        // 2. Drift over to the box.
        Vector3 from = ListenerPosition();
        for (float t = 0f; t < travelSeconds && box != null; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / travelSeconds);
            transform.position = Vector3.Lerp(from, box.transform.position, k);
            BlendTowardBoxMode(box);
            yield return null;
        }

        // 3. Stay on it - it can be picked up and carried around.
        while (box != null && box.IsSpawned)
        {
            transform.position = box.transform.position;
            BlendTowardBoxMode(box);
            UpdateMuffle(box);
            AdvanceWhenFinished(box);
            yield return null;
        }

        // The box went away (map regenerated): go flat again and pick up the next one.
        musicSource.spatialBlend = 0f;
        followedBox = null;
        followRoutine = StartCoroutine(FollowBoombox());
    }

    // Full volume up to minDistance, then a curve that drops quickly and reaches silence at
    // maxDistance. A plain linear fade left the music clearly audible most of the way across the map.
    private void ApplyRolloff()
    {
        float max = Mathf.Max(minDistance + 0.1f, maxDistance);
        float min = Mathf.Clamp(minDistance, 0f, max - 0.1f);

        const int Samples = 12;
        Keyframe[] keys = new Keyframe[Samples + 1];
        for (int i = 0; i <= Samples; i++)
        {
            float distance = Mathf.Lerp(min, max, i / (float)Samples);
            float remaining = 1f - Mathf.InverseLerp(min, max, distance);
            keys[i] = new Keyframe(distance / max, Mathf.Pow(remaining, Mathf.Max(0.01f, falloffPower)));
        }

        AnimationCurve curve = new(keys);
        for (int i = 0; i < keys.Length; i++)
        {
            curve.SmoothTangents(i, 0f);
        }

        musicSource.rolloffMode = AudioRolloffMode.Custom;
        musicSource.minDistance = min;
        musicSource.maxDistance = max;
        musicSource.SetCustomCurve(AudioSourceCurveType.CustomRolloff, curve);
    }

    // Same as proximity voice: what stands between you and the box muffles and quiets the music,
    // by what it is made of. Map-wide (2D) moments aren't muffled - the music is everywhere then.
    private void UpdateMuffle(BumBox box)
    {
        if (musicSource.spatialBlend <= 0.5f)
        {
            occlusionTarget = SoundOcclusion.Amount.Clear;
        }
        else if (Time.time >= nextOcclusionCheck)
        {
            nextOcclusionCheck = Time.time + muffleCheckInterval;
            AudioListener listener = FindListener();
            Transform listenerRoot = listener != null ? listener.transform.root : null;
            Vector3 ears = listener != null ? listener.transform.position : transform.position;
            occlusionTarget = SoundOcclusion.Evaluate(ears, box.transform.position + Vector3.up * 0.3f,
                wallLayerMask, listenerRoot, box.transform);
        }

        occlusion.Step(occlusionTarget, muffleBlendSeconds, Time.deltaTime);
    }

    // The host moves the box on to its next song when the current one ends; every player follows
    // through the synced track index.
    private void AdvanceWhenFinished(BumBox box)
    {
        bool finished = musicSource.clip != null && !musicSource.loop && fadeRoutine == null &&
                        !musicSource.isPlaying && musicSource.time <= 0.01f;
        if (!finished)
        {
            nextTrackRequested = false;
            return;
        }

        if (!nextTrackRequested && NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            nextTrackRequested = true;
            box.ServerNextTrack();
        }
    }

    // 3D at the box normally, 2D for everyone while the box is blasting map-wide.
    private void BlendTowardBoxMode(BumBox box)
    {
        musicSource.spatialBlend = Mathf.MoveTowards(musicSource.spatialBlend, box.IsBroadcasting ? 0f : 1f,
            Time.deltaTime / Mathf.Max(0.01f, broadcastBlendSeconds));
    }

    private void StopFollowing()
    {
        if (followRoutine != null)
        {
            StopCoroutine(followRoutine);
            followRoutine = null;
        }

        if (followedBox != null)
        {
            followedBox.SetDrivenBy(null);
            followedBox = null;
        }

        musicSource.spatialBlend = 0f;
        transform.localPosition = Vector3.zero;
        occlusionTarget = SoundOcclusion.Amount.Clear;
        occlusion.Set(SoundOcclusion.Amount.Clear);
        fadeLevel = baseVolume;
    }

    private AudioListener cachedListener;

    // The listener moves with the local player and changes on respawn/scene load, so it is
    // cached until it goes away or is disabled instead of searched for every frame.
    private AudioListener FindListener()
    {
        if (cachedListener == null || !cachedListener.isActiveAndEnabled)
        {
            cachedListener = FindFirstObjectByType<AudioListener>();
        }
        return cachedListener;
    }

    private Vector3 ListenerPosition()
    {
        AudioListener listener = FindListener();
        return listener != null ? listener.transform.position : transform.position;
    }

    // Changing the boombox's track changes the game music itself.
    private void OnBoxTrackChanged(BumBox box, AudioClip clip)
    {
        if (box == followedBox && clip != null && clip != musicSource.clip)
        {
            CrossfadeTo(clip);
        }
    }

    private void CrossfadeTo(AudioClip clip)
    {
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(CrossfadeClip(clip, trackCrossfadeSeconds));
    }

    private IEnumerator CrossfadeClip(AudioClip clip, float seconds)
    {
        float start = fadeLevel;
        for (float t = 0; t < seconds; t += Time.deltaTime)
        {
            fadeLevel = Mathf.Lerp(start, 0f, t / seconds);
            yield return null;
        }

        musicSource.clip = clip;
        musicSource.Play();

        for (float t = 0; t < seconds; t += Time.deltaTime)
        {
            fadeLevel = Mathf.Lerp(0f, baseVolume, t / seconds);
            yield return null;
        }
        fadeLevel = baseVolume;
        fadeRoutine = null;
    }

    private IEnumerator CrossfadeMusic(Scene scene, float fadeDuration = 2f)
    {
        float initialVolume = baseVolume;

        // Fade out the current music
        for (float t = 0; t < fadeDuration; t += Time.deltaTime)
        {
            fadeLevel = Mathf.Lerp(initialVolume, 0, t / fadeDuration);
            yield return null;
        }
        fadeLevel = 0;

        // Change the music clip (in the match, a track picked on the boombox wins)
        if (scene.name == "GameScene" && followedBox != null && followedBox.CurrentClip != null)
        {
            musicSource.clip = followedBox.CurrentClip;
        }
        else if (scene.name == "Lobby")
        {
            musicSource.clip = musicClips[0]; // Set the clip for the lobby
        }
        else if (scene.name == "GameScene")
        {
            musicSource.clip = musicClips[1]; // Set the clip for the game
        }

        musicSource.Play();

        // Fade in the new music
        for (float t = 0; t < fadeDuration; t += Time.deltaTime)
        {
            fadeLevel = Mathf.Lerp(0, initialVolume, t / fadeDuration);
            yield return null;
        }
        fadeLevel = initialVolume;
        fadeRoutine = null;
    }

    private void LateUpdate()
    {
        if (musicSource != null && this == Instance)
        {
            musicSource.volume = fadeLevel * occlusion.VolumeMultiplier;
        }
    }

    private void PauseMusic(float delay = 0.1f)
    {
        if (audioMixer != null)
            audioMixer.FindSnapshot("Paused")?.TransitionTo(delay);
    }

    private void UnPauseMusic(float delay = 0.1f)
    {
        if (audioMixer != null)
            audioMixer.FindSnapshot("Unpaused")?.TransitionTo(delay);
    }

    void OnDisable()
    {
        // Now properly unsubscribe using stored references
        if (pauseHandler != null)
            PauseMenu.OnPause -= pauseHandler;
        if (unpauseHandler != null)
            PauseMenu.OnUnPause -= unpauseHandler;
            
        SceneManager.sceneLoaded -= PlayMusic;
        BumBox.TrackChanged -= OnBoxTrackChanged;
    }
}
