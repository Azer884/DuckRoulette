using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

// Owner only: muffles and quiets every other player's voice by whatever stands between them and
// this player's ears. What the world is made of matters (a fence barely changes a voice, a rock
// nearly silences it) - see SoundOcclusion / SoundOccluder.
public class VoiceChatRaycast : NetworkBehaviour
{
    [SerializeField, Tooltip("Layers that can block voice. What each object does to the sound comes " +
        "from its SoundOccluder, or is guessed from its collider when it has none.")]
    private LayerMask wallLayerMask = 1;
    [SerializeField] private float maxDistance = 25f;
    [SerializeField, Tooltip("Height of a speaker's mouth above their player root.")]
    private float headHeight = 0.61f;

    [Header("Filter")]
    [SerializeField, Tooltip("Low-pass cutoff (Hz) when fully muffled.")]
    private float muffledCutoff = 450f;
    [SerializeField, Tooltip("Roughly how many seconds the muffle takes to fade in or out.")]
    private float blendSeconds = 0.3f;
    [SerializeField, Tooltip("Seconds between occlusion checks per player. The filter still eases every frame.")]
    private float checkInterval = 0.1f;
    [SerializeField, Tooltip("How far beside and above a speaker the extra rays aim, so corners give a partial muffle.")]
    private float raySpread = 0.6f;

    private const float PlayerRefreshSeconds = 1f;

    private class Speaker
    {
        public VoiceChat Voice;
        public AudioSource Audio;
        public OcclusionFilter Filter;
        public SoundOcclusion.Amount Target;
        public float NextCheck;
    }

    private readonly List<Speaker> speakers = new();
    private AudioListener listener;
    private float nextRefresh;

    public override void OnNetworkSpawn()
    {
        if (!IsOwner) enabled = false;
        base.OnNetworkSpawn();
    }

    private void Update()
    {
        if (Time.time >= nextRefresh)
        {
            nextRefresh = Time.time + PlayerRefreshSeconds;
            RefreshSpeakers();
        }

        Vector3 ears = ListenerPosition();

        for (int i = speakers.Count - 1; i >= 0; i--)
        {
            Speaker speaker = speakers[i];
            if (speaker.Voice == null || speaker.Audio == null)
            {
                speakers.RemoveAt(i);
                continue;
            }

            Vector3 mouth = speaker.Audio.transform.position + Vector3.up * headHeight;
            float distance = Vector3.Distance(ears, mouth);

            if (distance > maxDistance)
            {
                speaker.Audio.volume = 0f;
                speaker.Filter.Set(SoundOcclusion.Amount.Clear);
                continue;
            }

            if (Time.time >= speaker.NextCheck)
            {
                speaker.NextCheck = Time.time + checkInterval;
                speaker.Target = SoundOcclusion.Evaluate(ears, mouth, wallLayerMask, transform, speaker.Voice.transform, raySpread);
            }

            // Distance falloff is the AudioSource's own 3D rolloff; this only layers occlusion on top.
            speaker.Filter.Step(speaker.Target, blendSeconds, Time.deltaTime);
            speaker.Audio.volume = speaker.Filter.VolumeMultiplier;
        }
    }

    // Every spawned voice except our own. Scanning for VoiceChat (not the "Player" tag, which
    // the boombox also carries) picks up players that spawn after us without connect callbacks.
    private void RefreshSpeakers()
    {
        foreach (VoiceChat voice in FindObjectsByType<VoiceChat>(FindObjectsSortMode.None))
        {
            if (voice.IsOwner || voice.audioSource == null || speakers.Exists(s => s.Voice == voice))
            {
                continue;
            }

            speakers.Add(new Speaker
            {
                Voice = voice,
                Audio = voice.audioSource,
                Filter = new OcclusionFilter(voice.audioSource.gameObject, muffledCutoff),
                Target = SoundOcclusion.Amount.Clear,
                // Spread the checks out so all players aren't raycast on the same frame.
                NextCheck = Time.time + Random.value * checkInterval
            });
        }
    }

    private Vector3 ListenerPosition()
    {
        if (listener == null)
        {
            listener = GetComponentInChildren<AudioListener>();
        }
        return listener != null ? listener.transform.position : transform.position + Vector3.up * headHeight;
    }
}
