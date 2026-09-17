using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class SFXManager : MonoBehaviour
{
    public static SFXManager Instance { get; private set; }

    // Buttons already given a click sound, so re-scanning a scene never double-hooks one.
    private readonly HashSet<Button> hookedButtons = new();

    [Header("Mixer")]
    public AudioMixerGroup sfxMixerGroup;
    public AudioMixerGroup dapMixerGroup;

    [Header("UI")]
    public AudioClip buttonClick;
    public AudioClip buttonSelect;
    public AudioClip buttonHover;
    public AudioClip toggleOn;
    public AudioClip toggleOff;

    [Header("Slap")]
    public AudioClip slapClip;
    public AudioClip[] slapPainClips;

    [Header("Shooting")]
    public AudioClip reloadClip;
    public AudioClip triggerClip;
    public AudioClip shootClip;
    public AudioClip emptyShotClip;

    [Header("Team Up")]
    public AudioClip dapSound;
    public AudioClip perfectDapSound;

    [Header("Footsteps")]
    public AudioClip[] footstepClips;

    [Header("Death")]
    public AudioClip deathClip;
    public AudioClip[] bodyImpactClips;

    [Header("Slap Stun")]
    public AudioClip stunClip;

    [Header("Bullet Impact")]
    public AudioClip bulletImpactClip;

    [Header("Gun Handling")]
    public AudioClip gunSpinClip;
    public AudioClip gunSpinAfterShotClip;
    // Played to the new holder only, so a player never has to look down to notice the gun
    // arrived - the previous hand-off had no cue of any kind.
    public AudioClip turnStartClip;

    [Header("Hiding Spot")]
    public AudioClip hideEnterClip;
    public AudioClip hideExitClip;

    [Header("Round Flow")]
    public AudioClip roundStartClip;
    public AudioClip shotClockTickClip;
    public AudioClip shotClockUrgentTickClip;

    [Header("Match End")]
    public AudioClip victoryClip;
    public AudioClip defeatClip;

    [Header("Rewards")]
    public AudioClip coinRewardClip;
    public AudioClip taskCompleteClip;

    [SerializeField] private AudioSource uiSource;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        if (uiSource == null)
        {
            uiSource = gameObject.AddComponent<AudioSource>();
        }

        uiSource.playOnAwake = false;
        uiSource.spatialBlend = 0f;
        if (sfxMixerGroup != null)
        {
            uiSource.outputAudioMixerGroup = sfxMixerGroup;
        }

        SceneManager.sceneLoaded += OnSceneLoaded;
        HookAllButtons();
        WarnAboutMissingClips();
    }

    // H1: unassigned clips used to fail silently (PlayAt/PlayUI just return early on
    // clip == null), so a missing death/turn-start/UI sound never showed up anywhere.
    // This logs every gap once at startup instead.
    private void WarnAboutMissingClips()
    {
        var missing = new List<string>();
        foreach (var field in GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            if (field.FieldType == typeof(AudioClip))
            {
                if ((AudioClip)field.GetValue(this) == null) missing.Add(field.Name);
            }
            else if (field.FieldType == typeof(AudioClip[]))
            {
                var arr = (AudioClip[])field.GetValue(this);
                if (arr == null || arr.Length == 0) missing.Add(field.Name);
            }
        }

        if (missing.Count > 0)
        {
            Debug.LogWarning($"SFXManager: {missing.Count} clip field(s) unassigned, those sounds are silent: {string.Join(", ", missing)}");
        }
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        HookAllButtons();
    }

    // Wires every Button already in the loaded scene(s) to play the click sound.
    // Buttons created later at runtime (e.g. an instantiated lobby list row) need
    // RegisterButton(button) called on them once, right after Instantiate.
    public void HookAllButtons()
    {
        foreach (Button button in FindObjectsByType<Button>(FindObjectsSortMode.None))
        {
            RegisterButton(button);
        }
    }

    public void RegisterButton(Button button)
    {
        if (button == null || hookedButtons.Contains(button)) return;

        hookedButtons.Add(button);
        button.onClick.AddListener(PlayButtonClick);

        if (button.GetComponent<SFXButtonHooks>() == null)
        {
            button.gameObject.AddComponent<SFXButtonHooks>();
        }
    }

    public void PlayUI(AudioClip clip)
    {
        if (clip == null) return;
        uiSource.PlayOneShot(clip);
    }

    public void PlayButtonClick() => PlayUI(buttonClick);
    public void PlayButtonSelect() => PlayUI(buttonSelect);
    public void PlayButtonHover() => PlayUI(buttonHover);
    public void PlayToggleOn() => PlayUI(toggleOn);
    public void PlayToggleOff() => PlayUI(toggleOff);

    // Spawns a temporary positional AudioSource for a 3D one-shot, matching the old
    // per-script PlayLocalOneShot implementations this manager replaces.
    public void PlayAt(AudioClip clip, Vector3 position, float pitch = 1f, AudioMixerGroup group = null,
        float minDistance = 1f, float maxDistance = 40f, int priority = 128)
    {
        if (clip == null) return;

        GameObject audioObject = new GameObject($"{clip.name}_OneShot");
        audioObject.transform.position = position;

        AudioSource source = audioObject.AddComponent<AudioSource>();
        source.clip = clip;
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Logarithmic;
        source.minDistance = minDistance;
        source.maxDistance = maxDistance;
        source.priority = priority;
        source.playOnAwake = false;
        source.pitch = pitch;
        source.outputAudioMixerGroup = group != null ? group : sfxMixerGroup;
        source.Play();

        Destroy(audioObject, clip.length / Mathf.Max(Mathf.Abs(pitch), 0.01f));
    }

    public AudioClip RandomSlapPain()
    {
        return slapPainClips != null && slapPainClips.Length > 0
            ? slapPainClips[Random.Range(0, slapPainClips.Length)]
            : null;
    }

    public AudioClip RandomFootstep()
    {
        return footstepClips != null && footstepClips.Length > 0
            ? footstepClips[Random.Range(0, footstepClips.Length)]
            : null;
    }

    public AudioClip RandomBodyImpact()
    {
        return bodyImpactClips != null && bodyImpactClips.Length > 0
            ? bodyImpactClips[Random.Range(0, bodyImpactClips.Length)]
            : null;
    }
}
