using UnityEngine;
using TMPro;
using System.Collections;

public enum MessagePriority
{
    Low = 0,
    Medium = 1,
    High = 2
}

public class MessageBox : MonoBehaviour
{
    public static MessageBox Instance { get; private set; }

    private TextMeshProUGUI textMeshPro;
    private Coroutine hideCoroutine;

    private float messageDuration = 3f;
    private string lastMessage = "";
    private float lastShownTime = -10f;
    private float suppressInterval = 0.5f;
    private MessagePriority currentPriority = MessagePriority.Low;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            textMeshPro = GetComponentInChildren<TextMeshProUGUI>(true);
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public static void Informate(string message, Color? color = null, MessagePriority priority = MessagePriority.Low, float duration = 3f)
    {
        if (Instance == null || Instance.textMeshPro == null || string.IsNullOrEmpty(message))
            return;

        float time = Time.time;

        // Don't show if same message was just shown
        if (message == Instance.lastMessage && time - Instance.lastShownTime < Instance.suppressInterval)
            return;

        // Don't override higher-priority message
        if (priority < Instance.currentPriority && time - Instance.lastShownTime < Instance.messageDuration)
            return;

        Instance.lastMessage = message;
        Instance.lastShownTime = time;
        Instance.messageDuration = duration;
        Instance.currentPriority = priority;

        Instance.textMeshPro.text = message;
        Instance.textMeshPro.color = color ?? Color.white;
        Instance.textMeshPro.gameObject.SetActive(true);

        if (Instance.hideCoroutine != null)
        {
            Instance.StopCoroutine(Instance.hideCoroutine);
        }
        Instance.hideCoroutine = Instance.StartCoroutine(Instance.HideMessageAfterDelay());
    }

    private IEnumerator HideMessageAfterDelay()
    {
        yield return new WaitForSeconds(messageDuration);
        if (textMeshPro != null)
        {
            textMeshPro.gameObject.SetActive(false);
        }
        currentPriority = MessagePriority.Low;
    }
}
