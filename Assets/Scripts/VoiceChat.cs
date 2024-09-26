using System.Collections;
using UnityEngine;
using Steamworks;
using Unity.Netcode;

public class VoiceChat : NetworkBehaviour
{
    public KeyCode pushToTalkKey = KeyCode.V;
    public float voiceUpdateInterval = 0.1f;
    public float volumeBoost = 2.0f;
    public float noiseThreshold = 0.02f;

    private bool isTalking = false;
    private byte[] voiceBuffer = new byte[1024];
    private byte[] decompressedBuffer = new byte[22050];
    private int sampleRate = 16000;
    private int channels = 1;
    private AudioClip microphoneClip;

    public override void OnNetworkSpawn()
    {
        if (!IsOwner) enabled = false;
        base.OnNetworkSpawn();
    }

    void Start()
    {
        if (!SteamAPI.Init())
        {
            Debug.LogError("Steam API initialization failed.");
            return;
        }
    }

    void Update()
    {
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
        }
    }

    IEnumerator CaptureVoiceCoroutine()
    {
        while (isTalking)
        {
            EVoiceResult result = SteamUser.GetAvailableVoice(out uint voiceDataAvailable);

            if (result == EVoiceResult.k_EVoiceResultOK && voiceDataAvailable > 0)
            {
                result = SteamUser.GetVoice(true, voiceBuffer, (uint)voiceBuffer.Length, out uint bytesWritten);

                if (result == EVoiceResult.k_EVoiceResultOK && bytesWritten > 0)
                {
                    SendVoiceServerRpc(voiceBuffer, bytesWritten, OwnerClientId);
                }
            }

            yield return new WaitForSeconds(voiceUpdateInterval);
        }
    }

    // This sends the voice data to the server which will relay it to other clients
    [ServerRpc]
    private void SendVoiceServerRpc(byte[] voice, uint bytes, ulong senderId)
    {
        GameManager.Instance.SendVoiceClientRpc(voice, bytes, senderId);
    }

    public void DecompressAndPlayVoice(byte[] voiceBuffer, uint bytesWritten)
    {
        EVoiceResult decompressResult = SteamUser.DecompressVoice(
            voiceBuffer, bytesWritten,
            decompressedBuffer, (uint)decompressedBuffer.Length,
            out uint bytesDecompressed, (uint)sampleRate
        );

        if (decompressResult == EVoiceResult.k_EVoiceResultOK && bytesDecompressed > 0)
        {
            ProcessDecompressedVoiceData(decompressedBuffer, bytesDecompressed);
        }
    }

    private void ProcessDecompressedVoiceData(byte[] decompressedData, uint bytesDecompressed)
    {
        int samplesCount = (int)(bytesDecompressed / 2); 
        float[] audioData = new float[samplesCount];
        for (int i = 0; i < samplesCount; i++)
        {
            short sample = (short)(decompressedData[i * 2] | (decompressedData[i * 2 + 1] << 8));
            float floatSample = sample / 32768.0f; 

            if (Mathf.Abs(floatSample) < noiseThreshold)
            {
                floatSample = 0f;
            }

            floatSample *= volumeBoost;
            audioData[i] = Mathf.Clamp(floatSample, -1f, 1f);
        }

        if (microphoneClip == null || microphoneClip.samples != audioData.Length)
        {
            microphoneClip = AudioClip.Create("VoiceClip", audioData.Length, channels, sampleRate, false);
        }

        microphoneClip.SetData(audioData, 0);
        AudioSource audioSource = GetComponent<AudioSource>();
        audioSource.clip = microphoneClip;
        audioSource.Play();
    }

    void OnDisable()
    {
        SteamAPI.Shutdown();
    }
}
