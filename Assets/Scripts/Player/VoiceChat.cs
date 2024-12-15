using UnityEngine;
using Steamworks;
using System.IO;
using System;
using Unity.Netcode;
using System.Linq;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class VoiceChat : NetworkBehaviour
{
    private MemoryStream output;
    private MemoryStream stream;
    private MemoryStream input;

    private int optimalRate;
    private int clipBufferSize;
    private float[] clipBuffer;

    private int playbackBuffer;
    private int dataPosition;
    private int dataReceived;
    public AudioSource audioSource;

    public bool pushToTalk = true, toggleToTalk, openMic;
    private bool toggleActive;

    [SerializeField] private GameObject micUI;
    [SerializeField] private GameObject spit;

    #region Input Things
    private InputActionAsset inputActions;
    #endregion

    private void Start()
    {
        inputActions = GetComponent<InputSystem>().inputActions;
        
        // Initialize streams
        optimalRate = (int)SteamUser.OptimalSampleRate;

        clipBufferSize = optimalRate * 5;
        clipBuffer = new float[clipBufferSize];

        stream = new MemoryStream();
        output = new MemoryStream();
        input = new MemoryStream();

        audioSource.clip = AudioClip.Create("VoiceData", clipBufferSize, 1, optimalRate, true, OnAudioRead, null);
        audioSource.volume = 2.0f;
        audioSource.loop = true;
        audioSource.Play();
    }

    private void Update()
    {
        if (IsOwner) // Push-to-Talk, and ensure only the owner sends data
        {
            if (inputActions.FindAction("Talk").triggered)
            {
                toggleActive = !toggleActive; // Toggle the state on key press
            }
            SteamUser.VoiceRecord = (pushToTalk && inputActions.FindAction("Talk").ReadValue<float>() > 0) || (toggleToTalk && toggleActive) || openMic;
            ActivateTalkUIServerRpc(OwnerClientId, SteamUser.VoiceRecord);

            if (SteamUser.HasVoiceData)
            {
                int compressedWritten = SteamUser.ReadVoiceData(stream);
                stream.Position = 0;
                SendVoiceDataToClientsServerRpc(stream.GetBuffer(), compressedWritten);
            }
        }
    }

    private void OnAudioFilterRead(float[] data, int channels) 
    {
        
    }

    // This will be called on the server and forward the voice data to all clients except the sender
    [ServerRpc]
    private void SendVoiceDataToClientsServerRpc(byte[] voiceData, int compressedWritten, ServerRpcParams serverRpcParams = default)
    {
        // Get the sender's client ID
        ulong senderClientId = serverRpcParams.Receive.SenderClientId;

        // Broadcast the voice data to all clients except the sender
        PlayVoiceOnClientsClientRpc(voiceData, compressedWritten, new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = NetworkManager.Singleton.ConnectedClientsList
                    .Where(client => client.ClientId != senderClientId)
                    .Select(client => client.ClientId).ToArray()
            }
        });
    }

    // This will be executed on all clients to play the received voice data
    [ClientRpc]
    private void PlayVoiceOnClientsClientRpc(byte[] voiceData, int compressedWritten, ClientRpcParams clientRpcParams = default)
    {
        Decompresser(voiceData, compressedWritten);
    }

    private void Decompresser(byte[] voiceData, int compressedWritten)
    {
        input.Write(voiceData, 0, compressedWritten);
        input.Position = 0;

        int uncompressedWritten = SteamUser.DecompressVoice(input, compressedWritten, output);
        input.Position = 0;

        byte[] outputBuffer = output.GetBuffer();
        WriteToClip(outputBuffer, uncompressedWritten);
        output.Position = 0;
    }

    private void OnAudioRead(float[] data)
    {
        for (int i = 0; i < data.Length; ++i)
        {
            // start with silence
            data[i] = 0;

            // do I  have anything to play?
            if (playbackBuffer > 0)
            {
                // current data position playing
                dataPosition = (dataPosition + 1) % clipBufferSize;

                data[i] = clipBuffer[dataPosition];

                playbackBuffer --;
            }
        }

    }

    private void WriteToClip(byte[] uncompressed, int iSize)
    {
        float gain = 4.0f;
        for (int i = 0; i < iSize; i += 2)
        {
            // insert converted float to buffer
            float converted = (short)(uncompressed[i] | uncompressed[i + 1] << 8) / 32767.0f;
            converted *= gain;

            clipBuffer[dataReceived] = converted;

            // buffer loop
            dataReceived = (dataReceived +1) % clipBufferSize;

            playbackBuffer++;
        }
    }

    [ServerRpc]
    private void ActivateTalkUIServerRpc(ulong clientId, bool isTalking)
    {
        ActivateTalkUIClientRpc(clientId, isTalking);
    }

    [ClientRpc]
    private void ActivateTalkUIClientRpc(ulong clientId, bool isTalking)
    {
        if (OwnerClientId == clientId)
        {
            micUI.SetActive(isTalking);
            spit.SetActive(isTalking);
        }
    }
}
