using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

// Player-facing feedback for RoundManager's per-round shot clock (previously a
// NetworkVariable<float> with no UI reading it at all - the gun holder got force-shot with
// zero warning that a timer was even running).
//
// All visuals live on Assets/PreFabs/Ui/ShotClockUI.prefab - drop that prefab into the game
// scene once and edit colors/layout/fonts there like any other UI. This script only drives it.
public class ShotClockUI : MonoBehaviour
{
    [SerializeField] private GameObject root;
    [SerializeField] private Image fillImage;
    [SerializeField] private TextMeshProUGUI timerText;
    [SerializeField] private TextMeshProUGUI turnLabel;

    [Header("Colors")]
    [SerializeField] private Color normalColor = new(0.95f, 0.95f, 0.95f, 0.95f);
    [SerializeField] private Color yourTurnColor = new(1f, 0.75f, 0.15f, 1f);
    [SerializeField] private Color urgentColor = new(1f, 0.25f, 0.2f, 1f);
    [SerializeField] private float urgentThreshold = 5f;

    private void Awake()
    {
        if (root != null)
        {
            root.SetActive(false);
        }
    }

    private void Update()
    {
        if (root == null || fillImage == null || timerText == null)
        {
            return;
        }

        if (RoundManager.Instance == null || GameManager.Instance == null ||
            NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            if (root.activeSelf)
            {
                root.SetActive(false);
            }
            return;
        }

        bool active = RoundManager.Instance.IsRoundActive;
        if (root.activeSelf != active)
        {
            root.SetActive(active);
        }
        if (!active)
        {
            return;
        }

        float remaining = RoundManager.Instance.RemainingTime;
        float duration = Mathf.Max(0.01f, RoundManager.Instance.RoundDuration);
        float ratio = Mathf.Clamp01(remaining / duration);

        fillImage.fillAmount = ratio;
        timerText.text = Mathf.CeilToInt(remaining).ToString();

        ulong gunHolder = GameManager.Instance.playerWithGun.Value;
        bool isMyTurn = gunHolder == NetworkManager.Singleton.LocalClientId;
        bool urgent = remaining <= urgentThreshold;

        Color color = urgent ? urgentColor : (isMyTurn ? yourTurnColor : normalColor);
        fillImage.color = color;
        timerText.color = color;

        root.transform.localScale = urgent
            ? Vector3.one * (1f + Mathf.PingPong(Time.time * 4f, 0.12f))
            : Vector3.one;

        if (turnLabel != null)
        {
            turnLabel.text = isMyTurn ? "YOUR TURN" : GameManager.Instance.GetPlayerNickname(gunHolder).ToUpperInvariant();
            turnLabel.color = isMyTurn ? yourTurnColor : new Color(1f, 1f, 1f, 0.85f);
        }
    }
}
