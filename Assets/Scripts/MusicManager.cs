using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

public class MusicManager : MonoBehaviour
{
    public static MusicManager Instance { get; private set; }
    [SerializeField] private AudioMixer audioMixer;
    [SerializeField] private float fadeDuration = 2f; // Duration for the crossfade effect
    private AudioSource musicSource;
    [SerializeField] private AudioClip[] musicClips; // Array to hold different music clips
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject); // Make sure this object persists across scenes
            musicSource = GetComponent<AudioSource>();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void OnEnable()
    {
        PauseMenu.OnPause += () => PauseMusic(.5f);
        PauseMenu.OnUnPause += () => UnPauseMusic(.5f);
        SceneManager.sceneLoaded += PlayMusic;
    }

    private void PlayMusic(Scene scene, LoadSceneMode loadMode)
    {
        StartCoroutine(CrossfadeMusic(scene, fadeDuration));
    }

    private IEnumerator CrossfadeMusic(Scene scene, float fadeDuration = 2f)
    {
        float initialVolume = musicSource.volume;

        // Fade out the current music
        for (float t = 0; t < fadeDuration; t += Time.deltaTime)
        {
            musicSource.volume = Mathf.Lerp(initialVolume, 0, t / fadeDuration);
            yield return null;
        }
        musicSource.volume = 0;

        // Change the music clip
        if (scene.name == "Lobby")
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
            musicSource.volume = Mathf.Lerp(0, initialVolume, t / fadeDuration);
            yield return null;
        }
        musicSource.volume = initialVolume;
    }

    private void PauseMusic(float delay = 0.1f)
    {
        audioMixer.FindSnapshot("Paused").TransitionTo(delay);
    }

    private void UnPauseMusic(float delay = 0.1f)
    {
        audioMixer.FindSnapshot("Unpaused").TransitionTo(delay);
    }

    void OnDisable()
    {
        PauseMenu.OnPause -= () => PauseMusic();
        PauseMenu.OnUnPause -= () => UnPauseMusic();
        SceneManager.sceneLoaded -= PlayMusic;
    }
}
