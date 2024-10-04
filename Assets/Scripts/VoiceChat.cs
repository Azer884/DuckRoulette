using UnityEngine;
using Steamworks;
using System.IO;
using System;

public class VoiceChat : MonoBehaviour
{
    private MemoryStream voiceStream;
    private MemoryStream decompressedStream;
    public float volumeMultiplier = 2.0f;
    public AudioSource audioSource;

    private void Start()
    {
        // Initialize streams
        voiceStream = new MemoryStream();
        decompressedStream = new MemoryStream();
    }

    private void Update()
    {
        if (Input.GetKey(KeyCode.V)) // Push-to-Talk example
        {
            SteamUser.VoiceRecord = true; // Start recording

            if (SteamUser.HasVoiceData)
            {
                // Clear the stream for new voice data
                voiceStream.SetLength(0);
                decompressedStream.SetLength(0);

                // Read voice data into the stream
                SteamUser.ReadVoiceData(voiceStream);
                byte[] voiceData = voiceStream.ToArray();

                Decompresser(voiceData);
            }
        }
        else
        {
            SteamUser.VoiceRecord = false; // Stop recording
        }
    }

    private void Decompresser(byte[] voiceData)
    {
        SteamUser.DecompressVoice(voiceData, decompressedStream);

        // Convert decompressed data into a byte array
        byte[] decompressedData = decompressedStream.ToArray();

        // Create an AudioClip from the decompressed data
        AudioClip audioClip = AudioClip.Create("VoiceClip", decompressedData.Length / 2, 1, (int)SteamUser.SampleRate, false);
        audioClip.SetData(ConvertBytesToFloats(decompressedData), 0);
        
        // Assign and play the audio on the AudioSource
        audioSource.clip = audioClip;
        audioSource.Play();
    }

    // Convert byte array (PCM data) to float array
    private float[] ConvertBytesToFloats(byte[] decompressedData)
    {
        int length = decompressedData.Length / 2; // 16-bit PCM audio has 2 bytes per sample
        float[] floatData = new float[length];

        for (int i = 0; i < length; i++)
        {
            short value = BitConverter.ToInt16(decompressedData, i * 2);
            floatData[i] = value / 32768f * volumeMultiplier; // Normalize 16-bit PCM to a float range of -1 to 1
        }

        return floatData;
    }
}
