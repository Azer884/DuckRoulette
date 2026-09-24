using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Tells the player WHY they were sent back to the Lobby (kicked, host left, connection lost...).
// Before this nothing in code ever showed an error, so a dropped player just landed in the menu
// with no explanation. Sits on the "DisconnectError" instance of Assets/PreFabs/Ui/Error.prefab in
// Lobby.unity (hidden by default) - not on the prefab asset itself, whose other instances are
// reused as the tutorial prompt and the end-game panel.
//
// Showing is scene-independent: ErrorPopup.Show() remembers the message and displays it on the
// ErrorPopup in the active scene, right away or as soon as the next scene finishes loading - the
// usual case, since a kick loads the Lobby right after.
public class ErrorPopup : MonoBehaviour
{
    [SerializeField, Tooltip("Object toggled on/off. Defaults to this GameObject.")]
    private GameObject root;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI messageText;
    [SerializeField, Tooltip("Closes the popup.")]
    private Button closeButton;

    private static string pendingTitle;
    private static string pendingMessage;

    private GameObject Root => root != null ? root : gameObject;

    private void Awake()
    {
        if (closeButton != null)
        {
            closeButton.onClick.AddListener(Close);
        }
    }

    public static void Show(string title, string message)
    {
        pendingTitle = title;
        pendingMessage = message;
        TryShowPending();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        pendingTitle = null;
        pendingMessage = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => TryShowPending();

    private static void TryShowPending()
    {
        if (pendingMessage == null)
        {
            return;
        }

        ErrorPopup popup = FindTarget();
        if (popup == null)
        {
            return;
        }

        popup.Display(pendingTitle, pendingMessage);
        pendingTitle = null;
        pendingMessage = null;
    }

    private static ErrorPopup FindTarget()
    {
        Scene active = SceneManager.GetActiveScene();
        foreach (ErrorPopup popup in FindObjectsByType<ErrorPopup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (popup != null && popup.gameObject.scene == active)
            {
                return popup;
            }
        }

        return null;
    }

    private void Display(string title, string message)
    {
        if (titleText != null)
        {
            titleText.text = string.IsNullOrEmpty(title) ? "Error" : title;
        }

        if (messageText != null)
        {
            messageText.text = message;
        }

        // The popup may sit under a parent that is hidden by default; make sure the whole chain up
        // to the canvas is visible, otherwise SetActive(true) on the root alone shows nothing.
        Transform t = Root.transform;
        while (t != null)
        {
            if (!t.gameObject.activeSelf)
            {
                t.gameObject.SetActive(true);
            }

            if (t.GetComponent<Canvas>() != null)
            {
                break;
            }
            t = t.parent;
        }

        Root.transform.SetAsLastSibling();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void Close() => Root.SetActive(false);
}

// Watches the local client's connection from outside any scene, because the objects that used to
// listen for it (GameNetworkManager in the Lobby, GameManager on the player prefab) are destroyed
// or despawned by the very disconnect they would need to report. Anything that ends a client's
// session without the player asking for it gets an ErrorPopup explaining what happened.
public static class DisconnectNotice
{
    public const string HostLeftMessage = "The host left the game.";
    public const string KickedMessage = "You were kicked from the lobby by the host.";

    private static bool leavingOnPurpose;
    private static string overrideMessage;
    private static NetworkManager hooked;

    /// <summary>Call right before the LOCAL player leaves on purpose (Exit button, Leave lobby),
    /// so the resulting disconnect isn't reported as an error.</summary>
    public static void MarkLeavingOnPurpose() => leavingOnPurpose = true;

    /// <summary>Use this text instead of the transport's disconnect reason for the next drop.</summary>
    public static void SetReason(string message) => overrideMessage = message;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        leavingOnPurpose = false;
        overrideMessage = null;
        hooked = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    // NetworkManager lives in the bootstrap Loading scene; hook it as soon as it exists.
    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => TryHook();

    private static void TryHook()
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null || manager == hooked)
        {
            return;
        }

        if (hooked != null)
        {
            hooked.OnClientStarted -= OnClientStarted;
            hooked.OnClientStopped -= OnClientStopped;
        }

        hooked = manager;
        manager.OnClientStarted += OnClientStarted;
        manager.OnClientStopped += OnClientStopped;
    }

    private static void OnClientStarted()
    {
        leavingOnPurpose = false;
        overrideMessage = null;
    }

    private static void OnClientStopped(bool wasHost)
    {
        bool onPurpose = leavingOnPurpose;
        string message = overrideMessage;
        leavingOnPurpose = false;
        overrideMessage = null;

        // The host ends its own session; it's never "kicked".
        if (wasHost || onPurpose)
        {
            return;
        }

        if (string.IsNullOrEmpty(message) && hooked != null)
        {
            message = hooked.DisconnectReason;
        }

        if (string.IsNullOrEmpty(message))
        {
            message = "Lost connection to the host. The host may have left the game.";
        }

        ErrorPopup.Show("Disconnected", message);
    }

    /// <summary>Host only: tell every client why they're about to be dropped, so their popup says
    /// "the host left" instead of a generic connection error.</summary>
    public static void DisconnectAllClients(string reason)
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null || !manager.IsServer)
        {
            return;
        }

        foreach (ulong clientId in new List<ulong>(manager.ConnectedClientsIds))
        {
            if (clientId != NetworkManager.ServerClientId)
            {
                manager.DisconnectClient(clientId, reason);
            }
        }
    }
}
