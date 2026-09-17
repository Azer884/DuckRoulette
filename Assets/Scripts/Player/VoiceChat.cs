using UnityEngine;
using Unity.Netcode;
using System.Linq;
using UnityEngine.InputSystem;

public class VoiceChat : NetworkBehaviour
{
    // Wire format: 16 kHz mono, 8-bit G.711 mu-law (~16 KB/s while a player is actually talking).
    // Capture runs at whatever rate the chosen device supports and is resampled to this, so every
    // peer can decode into one fixed-rate playback clip no matter what hardware the sender has.
    private const int VoiceSampleRate = 16000;
    private const int ChunksPerSecond = 50;
    private const int SendSamples = VoiceSampleRate / ChunksPerSecond;
    private const int PlaybackBufferSeconds = 5;
    private const int MaxChunksPerFrame = 10;

    // A mu-law frame is one byte per sample; anything past a second of audio is not real voice.
    private const int MaxVoicePayloadBytes = 8192;

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

    #region Capture
    private AudioClip micClip;
    private string micDevice;
    private int micReadPosition;
    private int captureRate;
    private int captureChunkSamples;
    private float[] captureBuffer;
    private byte[] sendBuffer;
    #endregion

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

        if (IsOwner)
        {
            MicDeviceSettings.OnDeviceChanged += HandleDeviceChanged;
            StartCapture(MicDeviceSettings.SelectedDevice);
        }

        base.OnNetworkSpawn();
    }

    public override void OnNetworkDespawn()
    {
        isTalking.OnValueChanged -= HandleTalkingChanged;

        if (IsOwner)
        {
            MicDeviceSettings.OnDeviceChanged -= HandleDeviceChanged;
            StopCapture();
        }

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

        clipBufferSize = VoiceSampleRate * PlaybackBufferSeconds;
        clipBuffer = new float[clipBufferSize];

        audioSource.clip = AudioClip.Create("VoiceData", clipBufferSize, 1, VoiceSampleRate, true, OnAudioRead, null);
        audioSource.loop = true;
        audioSource.Play();
    }

    private void HandleDeviceChanged(string device)
    {
        StopCapture();
        StartCapture(device);
    }

    // device == null means "system default", which is what Microphone.Start already does with a
    // null device name, so an unset or unplugged device needs no special case here.
    private void StartCapture(string device)
    {
        if (Microphone.devices.Length == 0)
        {
            Debug.LogWarning("VoiceChat: no input devices found - voice capture is disabled.");
            return;
        }

        micDevice = device;

        // A device that reports a caps range cannot be opened outside it, so clamp into the range
        // and let the resampler handle the difference instead of failing to record at all.
        Microphone.GetDeviceCaps(micDevice, out int minFrequency, out int maxFrequency);
        captureRate = VoiceSampleRate;
        if (minFrequency != 0 || maxFrequency != 0)
        {
            captureRate = Mathf.Clamp(VoiceSampleRate, minFrequency, maxFrequency);
        }

        // One chunk is 20 ms. The clip is exactly ChunksPerSecond chunks long, so a chunk never
        // straddles the loop point and each read is a single GetData call.
        captureChunkSamples = captureRate / ChunksPerSecond;
        micClip = Microphone.Start(micDevice, true, 1, captureRate);

        if (micClip == null)
        {
            Debug.LogWarning($"VoiceChat: could not open input device '{micDevice ?? "Default"}'.");
            return;
        }

        captureBuffer = new float[captureChunkSamples];
        sendBuffer = new byte[SendSamples];
        micReadPosition = 0;
    }

    private void StopCapture()
    {
        if (micClip == null) return;

        Microphone.End(micDevice);
        micClip = null;
    }

    private void Update()
    {
        if (!IsOwner) return;

        if (talkAction.triggered)
        {
            toggleActive = !toggleActive; // Toggle the state on key press
        }

        bool wantsToTalk = (pushToTalk && talkAction.ReadValue<float>() > 0) || (toggleToTalk && toggleActive) || openMic;
        isTalking.Value = wantsToTalk;

        CaptureVoice(wantsToTalk);
    }

    private void CaptureVoice(bool wantsToTalk)
    {
        if (micClip == null) return;

        int writePosition = Microphone.GetPosition(micDevice);
        if (writePosition < 0) return;

        int available = writePosition - micReadPosition;
        if (available < 0) available += micClip.samples;

        // After a hitch the mic keeps filling its ring, and sending that whole backlog would be
        // both a bandwidth spike and permanently late audio. Drop everything but the newest chunks.
        int maxBacklog = captureChunkSamples * MaxChunksPerFrame;
        if (available > maxBacklog)
        {
            micReadPosition = writePosition - maxBacklog;
            if (micReadPosition < 0) micReadPosition += micClip.samples;
            available = maxBacklog;
        }

        while (available >= captureChunkSamples)
        {
            // Rates that are not a multiple of ChunksPerSecond leave a sub-chunk tail at the loop
            // point; skip it rather than reading past the end of the clip.
            int remaining = micClip.samples - micReadPosition;
            if (remaining < captureChunkSamples)
            {
                available -= remaining;
                micReadPosition = 0;
                continue;
            }

            micClip.GetData(captureBuffer, micReadPosition);
            micReadPosition = (micReadPosition + captureChunkSamples) % micClip.samples;
            available -= captureChunkSamples;

            // Samples are read and discarded while silent too, so releasing push-to-talk never
            // leaves a backlog of stale audio to send on the next press.
            if (!wantsToTalk) continue;

            EncodeChunk();
            SendVoiceDataToClientsServerRpc(sendBuffer, sendBuffer.Length);
        }
    }

    // Linear resample of one capture chunk down to the wire rate, then mu-law encode in place.
    private void EncodeChunk()
    {
        float step = (float)captureChunkSamples / SendSamples;

        for (int i = 0; i < SendSamples; i++)
        {
            float sourceIndex = i * step;
            int index = (int)sourceIndex;
            float fraction = sourceIndex - index;

            float sample = captureBuffer[index];
            if (index + 1 < captureChunkSamples)
            {
                sample = Mathf.Lerp(sample, captureBuffer[index + 1], fraction);
            }

            sendBuffer[i] = LinearToMuLaw(sample);
        }
    }

    // This will be called on the server and forward the voice data to all clients except the sender
    [ServerRpc]
    private void SendVoiceDataToClientsServerRpc(byte[] voiceData, int voiceDataLength, ServerRpcParams serverRpcParams = default)
    {
        // Relayed verbatim to every other client, so bound it here: a bogus length made every
        // receiver throw while decoding, and an oversized array was amplified to the whole lobby.
        if (voiceData == null || !RpcValidation.IsValidVoicePayload(voiceData.Length, voiceDataLength, MaxVoicePayloadBytes))
        {
            return;
        }

        if (voiceData.Length != voiceDataLength)
        {
            byte[] trimmed = new byte[voiceDataLength];
            System.Buffer.BlockCopy(voiceData, 0, trimmed, 0, voiceDataLength);
            voiceData = trimmed;
        }

        // Get the sender's client ID
        ulong senderClientId = serverRpcParams.Receive.SenderClientId;

        // Broadcast the voice data to all clients except the sender
        PlayVoiceOnClientsClientRpc(voiceData, voiceDataLength, new ClientRpcParams
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
    private void PlayVoiceOnClientsClientRpc(byte[] voiceData, int voiceDataLength, ClientRpcParams clientRpcParams = default)
    {
        WriteToClip(voiceData, voiceDataLength);
    }

    private void OnAudioRead(float[] data)
    {
        for (int i = 0; i < data.Length; ++i)
        {
            // start with silence
            data[i] = 0;

            // do I have anything to play?
            if (playbackBuffer > 0)
            {
                data[i] = clipBuffer[dataPosition];

                // Advance AFTER reading: pre-incrementing skipped the sample the writer had just
                // put at this position and read one sample ahead of the write cursor instead.
                dataPosition = (dataPosition + 1) % clipBufferSize;

                playbackBuffer--;
            }
        }
    }

    private void WriteToClip(byte[] voiceData, int voiceDataLength)
    {
        if (voiceData == null || voiceDataLength <= 0 || voiceDataLength > voiceData.Length) return;

        for (int i = 0; i < voiceDataLength; i++)
        {
            clipBuffer[dataReceived] = MuLawToLinear(voiceData[i]);

            // buffer loop
            dataReceived = (dataReceived + 1) % clipBufferSize;

            playbackBuffer++;
        }
    }

    #region G.711 mu-law
    private const int MuLawBias = 0x84;
    private const int MuLawClip = 32635;

    private static byte LinearToMuLaw(float sample)
    {
        int pcm = Mathf.Clamp(Mathf.RoundToInt(sample * short.MaxValue), -MuLawClip, MuLawClip);

        int sign = 0;
        if (pcm < 0)
        {
            sign = 0x80;
            pcm = -pcm;
        }

        int magnitude = pcm + MuLawBias;

        int exponent = 7;
        for (int mask = 0x4000; exponent > 0 && (magnitude & mask) == 0; exponent--, mask >>= 1)
        {
        }

        int mantissa = (magnitude >> (exponent + 3)) & 0x0F;
        return (byte)~(sign | (exponent << 4) | mantissa);
    }

    private static float MuLawToLinear(byte encoded)
    {
        int value = ~encoded;
        int sign = value & 0x80;
        int exponent = (value >> 4) & 0x07;
        int mantissa = value & 0x0F;

        int magnitude = (((mantissa << 3) + MuLawBias) << exponent) - MuLawBias;
        int pcm = sign != 0 ? -magnitude : magnitude;

        return Mathf.Clamp(pcm / (float)short.MaxValue, -1f, 1f);
    }
    #endregion
}
