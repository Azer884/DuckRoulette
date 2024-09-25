using System.Collections;
using UnityEngine;
using Steamworks;

public class PushToTalk : MonoBehaviour
{
    public KeyCode pushToTalkKey = KeyCode.V;       // Key for Push-to-Talk
    public AudioSource targetAudioSource;           // Target object AudioSource to play voice
    public float voiceUpdateInterval = 0.05f;       // Interval to check for voice data (reduced for smoother capture)

    private bool isTalking = false;
    private byte[] voiceBuffer = new byte[1024];    // Buffer for storing compressed voice data
    private byte[] decompressedBuffer = new byte[22050]; // Buffer for decompressed data (should be large enough)
    private int sampleRate = 16000;                 // Steam voice uses 16kHz sample rate
    private int channels = 1;                       // Mono channel for voice data
    private AudioClip microphoneClip;               // AudioClip to play voice data
    private bool audioPlaying = false;              // Flag to check if audio is playing

    void Start()
    {
        // Make sure the targetAudioSource is set
        if (targetAudioSource == null)
        {
            Debug.LogError("No AudioSource assigned to play voice data.");
            return;
        }

        // Initialize Steam API
        if (!SteamAPI.Init())
        {
            Debug.LogError("Steam API initialization failed.");
            return;
        }
    }

    void Update()
    {
        // Handle Push-to-Talk key input
        if (Input.GetKeyDown(pushToTalkKey))
        {
            StartTalking();
        }
        if (Input.GetKeyUp(pushToTalkKey))
        {
            StopTalking();
        }
    }

    private void StartTalking()
    {
        if (!isTalking)
        {
            SteamUser.StartVoiceRecording();
            isTalking = true;
            Debug.Log("Started voice recording.");
            StartCoroutine(CaptureVoiceCoroutine());
        }
    }

    private void StopTalking()
    {
        if (isTalking)
        {
            SteamUser.StopVoiceRecording();
            isTalking = false;
            Debug.Log("Stopped voice recording.");

            // Stop playback and clear any remaining audio to prevent looping the last sound
            if (targetAudioSource.isPlaying)
            {
                targetAudioSource.Stop();
                targetAudioSource.clip = null; // Clear the clip to avoid residual sounds
            }
        }
    }

    // Coroutine to capture and play voice data
    IEnumerator CaptureVoiceCoroutine()
    {
        while (isTalking)
        {
            // Check for available voice data every interval
            uint voiceDataAvailable = 0;
            EVoiceResult result = SteamUser.GetAvailableVoice(out voiceDataAvailable);

            if (result == EVoiceResult.k_EVoiceResultOK && voiceDataAvailable > 0)
            {
                uint bytesWritten;
                result = SteamUser.GetVoice(true, voiceBuffer, (uint)voiceBuffer.Length, out bytesWritten);

                if (result == EVoiceResult.k_EVoiceResultOK && bytesWritten > 0)
                {
                    // Decompress the captured voice data
                    uint bytesDecompressed;
                    EVoiceResult decompressResult = SteamUser.DecompressVoice(
                        voiceBuffer, bytesWritten, 
                        decompressedBuffer, (uint)decompressedBuffer.Length, 
                        out bytesDecompressed, (uint)sampleRate
                    );

                    if (decompressResult == EVoiceResult.k_EVoiceResultOK && bytesDecompressed > 0)
                    {
                        // Convert the decompressed voice data to an AudioClip
                        ProcessDecompressedVoiceData(decompressedBuffer, bytesDecompressed);
                    }
                    else
                    {
                        Debug.LogError("Failed to decompress voice data: " + decompressResult.ToString());
                    }
                }
            }

            // Wait for a short interval before checking again
            yield return new WaitForSeconds(voiceUpdateInterval);
        }
    }

    // Converts the decompressed voice data to audio and plays it
    private void ProcessDecompressedVoiceData(byte[] decompressedData, uint bytesDecompressed)
    {
        // Convert byte array (PCM16 format) to float array for AudioClip
        int samplesCount = (int)(bytesDecompressed / 2); // Each sample is 2 bytes (16-bit)
        float[] audioData = new float[samplesCount];
        for (int i = 0; i < samplesCount; i++)
        {
            short sample = (short)(decompressedData[i * 2] | (decompressedData[i * 2 + 1] << 8));
            audioData[i] = sample / 32768.0f; // Convert to range [-1, 1]
        }

        // Create an AudioClip with the decompressed data
        if (microphoneClip == null || microphoneClip.samples != audioData.Length)
        {
            microphoneClip = AudioClip.Create("MicrophoneClip", audioData.Length, channels, sampleRate, false);
        }

        // Set the audio data
        microphoneClip.SetData(audioData, 0);

        // Play the new audio clip if it's not already playing or replace the old one
        if (!audioPlaying || targetAudioSource.clip == null)
        {
            targetAudioSource.clip = microphoneClip;
            targetAudioSource.Play();
            audioPlaying = true;
        }
    }

    void OnDestroy()
    {
        // Shutdown Steam API on exit
        SteamAPI.Shutdown();
    }
}
