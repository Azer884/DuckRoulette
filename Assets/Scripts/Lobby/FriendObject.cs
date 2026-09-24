using System.Collections;
using UnityEngine;
using Steamworks;
using TMPro;
using UnityEngine.UI;

public class FriendObject : MonoBehaviour
{
    public TextMeshProUGUI playerName;
    public Image onlineStats;
    public SteamId steamid;

    [Header("Invite feedback")]
    [SerializeField, Tooltip("Seconds the button stays disabled after an invite, so it can't be spammed.")]
    private float inviteCooldown = 3f;
    [SerializeField] private string sentLabel = "Invite sent!";
    [SerializeField] private string failedLabel = "Invite failed";
    [SerializeField] private string sendingLabel = "Inviting...";
    [SerializeField] private Color sentColor = new(0.45f, 0.9f, 0.4f, 1f);
    [SerializeField] private Color failedColor = new(0.95f, 0.35f, 0.3f, 1f);

    private Button button;
    // What SteamFriendManager wants (online or not); the invite cooldown only ever overrides it
    // temporarily and hands control back when it ends.
    private bool available = true;
    private bool coolingDown;
    private string originalName;
    private Color originalColor;

    private void Awake()
    {
        button = GetComponent<Button>();
    }

    /// <summary>Called by SteamFriendManager when the friend goes online/offline. Respected once any
    /// running invite cooldown ends.</summary>
    public void SetAvailable(bool isAvailable)
    {
        available = isAvailable;
        if (!coolingDown && button != null)
        {
            button.interactable = available;
        }
    }

    public void Invite()
    {
        if (coolingDown)
        {
            return;
        }

        GameObject settingsBar = GameObject.FindGameObjectWithTag("SettingsBar");
        if (settingsBar != null && settingsBar.TryGetComponent(out Animator settingsAnim))
        {
            settingsAnim.Play("Settings");
        }

        // Check for an existing lobby, create if none exists
        if (LobbySaver.instance.currentLobby == null)
        {
            Debug.Log("No lobby found. Creating a new one...");
            GameNetworkManager.Instance.StartHost(6);

            // Wait for the lobby to initialize
            StartCoroutine(WaitForLobbyCreationAndInvite());
        }
        else
        {
            SendInvite();
        }
    }

    private IEnumerator WaitForLobbyCreationAndInvite()
    {
        // Lock the button straight away - creating the lobby can take a couple of seconds and a
        // second click would otherwise queue a second lobby creation.
        BeginFeedback(sendingLabel, OriginalColorOrCurrent());

        float timeout = Time.time + 15f;
        while (LobbySaver.instance.currentLobby == null)
        {
            if (Time.time >= timeout)
            {
                Debug.LogWarning("Timed out waiting for lobby creation; invite not sent.");
                yield return ShowResult(false);
                yield break;
            }
            yield return null; // Wait until the lobby is initialized
        }

        SendInvite();
    }

    private void SendInvite()
    {
        bool sent = LobbySaver.instance.currentLobby.Value.InviteFriend(steamid);
        Debug.Log(sent ? "Invited " + steamid : "Steam refused the invite to " + steamid);
        StopAllCoroutines();
        StartCoroutine(ShowResult(sent));
    }

    // The friend's row itself is the feedback: its name swaps to "Invite sent!" and the button is
    // disabled for the cooldown, then both go back.
    private IEnumerator ShowResult(bool sent)
    {
        BeginFeedback(sent ? sentLabel : failedLabel, sent ? sentColor : failedColor);

        if (sent && SFXManager.Instance != null)
        {
            SFXManager.Instance.PlayUI(SFXManager.Instance.taskCompleteClip);
        }

        yield return new WaitForSecondsRealtime(inviteCooldown);
        EndFeedback();
    }

    private Color OriginalColorOrCurrent() => coolingDown ? originalColor : playerName != null ? playerName.color : Color.white;

    private void BeginFeedback(string label, Color color)
    {
        if (!coolingDown && playerName != null)
        {
            originalName = playerName.text;
            originalColor = playerName.color;
        }

        coolingDown = true;
        if (button != null)
        {
            button.interactable = false;
        }

        if (playerName != null)
        {
            playerName.text = label;
            playerName.color = color;
        }
    }

    private void EndFeedback()
    {
        coolingDown = false;
        if (playerName != null && originalName != null)
        {
            playerName.text = originalName;
            playerName.color = originalColor;
        }

        if (button != null)
        {
            button.interactable = available;
        }
    }

    // A row disabled mid-cooldown (friends panel closed) must not come back stuck on "Invite sent!".
    private void OnDisable()
    {
        if (coolingDown)
        {
            StopAllCoroutines();
            EndFeedback();
        }
    }
}
