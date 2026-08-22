using UnityEngine;
using Steamworks;
using System.IO;
using System;
using Unity.Netcode;
using System.Linq;
using UnityEngine.InputSystem;

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
    private InputAction talkAction;
    #endregion

    // NetworkVariable instead of an edge-triggered ServerRpc/ClientRpc: Netcode syncs a
    // NetworkVariable's current value to newly-connected observers automatically, so a client
    // who joins mid-talk still sees the correct mic UI state immediately. Setting .Value every
    // frame is still cheap - Netcode only sends a delta when the value actually changes.
    public NetworkVariable<bool> isTalking = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    public override void OnNetworkSpawn()
    {
        isTalking.OnValueChanged += HandleTalkingChanged;
        HandleTalkingChanged(false, isTalking.Value);
        base.OnNetworkSpawn();
    }

    public override void OnNetworkDespawn()
    {
        isTalking.OnValueChanged -= HandleTalkingChanged;
        base.OnNetworkDespawn();
    }

    private void HandleTalkingChanged(bool oldValue, bool newValue)
    {
        micUI.SetActive(newValue);
        spit.SetActive(newValue);
    }

    private void Start()
    {
        inputActions = GetComponent<InputSystem>().inputActions;
        talkAction = inputActions.FindAction("Talk");

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
            if (talkAction.triggered)
            {
                toggleActive = !toggleActive; // Toggle the state on key press
            }
            SteamUser.VoiceRecord = (pushToTalk && talkAction.ReadValue<float>() > 0) || (toggleToTalk && toggleActive) || openMic;

            isTalking.Value = SteamUser.VoiceRecord;

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

}
