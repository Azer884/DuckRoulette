using UnityEngine;
using UnityEngine.UI;

public class SoundToScale : MonoBehaviour
{
    public Transform targetObject;  // The object to scale
    public Image filler;
    public float scaleMultiplier = 1.0f;  // Scaling factor
    public float smoothTime = 0.1f;  // Smoothing factor for scaling

    [HideInInspector] public AudioSource audioSource;
    private float[] samples = new float[256];  // Audio sample array
    private float currentLevel = 0f;  // Smoothed audio level
    private float velocity;  // SmoothDamp velocity
    public float originalScale = 1;

    // Set when this sits on a player: the owner's own voice source never plays (their voice is
    // only sent to the others), so the owner's meter reads the live mic level instead.
    private VoiceChat voiceChat;

    void Start()
    {
        // Get the AudioSource component
        audioSource = GetComponent<AudioSource>();
        voiceChat = GetComponent<VoiceChat>();
    }

    void Update()
    {
        if (filler != null && voiceChat != null && voiceChat.IsOwner)
        {
            float micLevel = voiceChat.IsTransmitting ? voiceChat.LocalMicLevel : 0f;
            currentLevel = Mathf.SmoothDamp(currentLevel, micLevel, ref velocity, smoothTime);
            filler.fillAmount = currentLevel;
            return;
        }

        // Get the current audio data
        if (audioSource != null)
        {
            audioSource.GetOutputData(samples, 0);
    
            // Calculate the RMS value of the samples
            float rmsValue = Mathf.Sqrt(GetRMS(samples));

            if (filler != null)
            {
                // Speech RMS is a few percent of full scale, so a linear fill barely moved; the
                // meter reads on a dB scale instead.
                currentLevel = Mathf.SmoothDamp(currentLevel, VoiceChat.LevelToFill(rmsValue), ref velocity, smoothTime);
                filler.fillAmount = currentLevel;
                return;
            }

            // Smoothly scale the object based on the RMS value
            currentLevel = Mathf.SmoothDamp(currentLevel, rmsValue, ref velocity, smoothTime);
            float scale = (1f + currentLevel * scaleMultiplier) * originalScale;
            targetObject.localScale = new Vector3(scale, scale, scale);
        }
    }

    private float GetRMS(float[] data)
    {
        float sum = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            sum += data[i] * data[i];
        }
        return sum / data.Length;
    }
}
