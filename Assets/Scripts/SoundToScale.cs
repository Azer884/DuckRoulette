using UnityEngine;
using UnityEngine.UI;

public class SoundToScale : MonoBehaviour
{
    public Transform targetObject;  // The object to scale
    public Image filler;
    public float scaleMultiplier = 1.0f;  // Scaling factor
    public float smoothTime = 0.1f;  // Smoothing factor for scaling

    private AudioSource audioSource;
    private float[] samples = new float[256];  // Audio sample array
    private float currentLevel = 0f;  // Smoothed audio level
    private float velocity;  // SmoothDamp velocity
    public float originalScale = 1;

    void Start()
    {
        // Get the AudioSource component
        audioSource = GetComponent<AudioSource>();
    }

    void Update()
    {
        // Get the current audio data
        audioSource.clip.GetData(samples, 0);

        // Calculate the RMS value of the samples
        float rmsValue = Mathf.Sqrt(GetRMS(samples));

        // Smoothly scale the object based on the RMS value
        currentLevel = Mathf.SmoothDamp(currentLevel, rmsValue, ref velocity, smoothTime);
        if (filler != null)
        {
            filler.fillAmount = currentLevel;
            return;
        }
        float scale = (1f + currentLevel * scaleMultiplier) * originalScale;
        targetObject.localScale = new Vector3(scale, scale, scale);
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
