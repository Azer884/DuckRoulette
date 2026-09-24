using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class TeamUp : NetworkBehaviour
{
    private InputAction interactAction, endTeamUpAction;
    public bool isTeamedUp = false;
    public int teamMateId = -1;
    public GameObject teamMate;
    public float teamUpRaduis = 2f;
    public LayerMask otherPlayers;
    public Transform teamUpArea;
    Collider[] teamUpResults =  new Collider[10];
    private float teamUpCooldown = 5f; // Cooldown duration in seconds
    private float lastTeamUpTime = -5f; // Initialize to allow immediate team-up
    public bool haveRequest = false;
    private int requesterId = -1; // Add this line to store requesterId
    private float requestDeadline;
    private int perfectDap = 0;
    public Transform dapPosition;
    public Renderer[] renderers;
    public Color teamColor = Color.green;

    [SerializeField, Tooltip("How directly the camera has to face another player before they are " +
        "the team-up target (dot product of camera forward and the direction to them).")]
    private float lookAtThreshold = 0.6f;

    public event System.Action OnTeamUp, OnExitTeamUp;

    // Server-authoritative so every peer (not just the two teamed players' own clients) sees the
    // outline - GameManager sets this on both players' TeamUp components when a team-up is
    // confirmed/ended (see TeamUpResponseServerRpc/EndTeamUpServerRpc), replacing what used to be
    // a purely local material mutation that only the two participants themselves could see.
    public NetworkVariable<Color> outlineColor = new(Color.black, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // responder client id -> (Time.time the block ends, block length). Mirrors the server's own
    // cooldown so the prompt can grey out and fill back up instead of sending a doomed request.
    private readonly Dictionary<ulong, (float until, float length)> declinedUntil = new();

    // The frame an incoming request was answered on. The same Interact press that accepted it
    // must not also reach Interact (pick something up) later in that frame.
    private static int requestClosedFrame = -1;
    private static bool localRequestPending;

    /// <summary>True while the local player is answering a team-up request - Interact stays
    /// disabled until they accept, reject or the timer runs out.</summary>
    public static bool BlocksInteract => localRequestPending || requestClosedFrame == Time.frameCount;

    private bool isPaused;

    private void OnEnable()
    {
        PauseMenu.OnPause += HandlePause;
        PauseMenu.OnUnPause += HandleUnpause;
    }

    private void OnDisable()
    {
        PauseMenu.OnPause -= HandlePause;
        PauseMenu.OnUnPause -= HandleUnpause;
        InteractionPromptHUD.Hide();
    }

    private void HandlePause()
    {
        isPaused = true;
        InteractionPromptHUD.Hide();
    }

    private void HandleUnpause()
    {
        isPaused = false;
    }

    public override void OnNetworkSpawn()
    {
        // Every peer needs this - it's what actually paints the outline on screen for whoever's
        // looking, including bystanders who aren't part of the team-up at all.
        outlineColor.OnValueChanged += HandleOutlineColorChanged;
        ApplyOutlineColor(outlineColor.Value);

        if (!IsOwner)
        {
            enabled = false;
            return;
        }

        InputActionAsset inputActions = GetComponent<InputSystem>().inputActions;
        interactAction = inputActions.FindAction("Interact");
        endTeamUpAction = inputActions.FindAction("EndTeamUp");
    }

    public override void OnNetworkDespawn()
    {
        outlineColor.OnValueChanged -= HandleOutlineColorChanged;

        if (IsOwner)
        {
            ClearIncomingRequest();
        }
    }

    private void HandleOutlineColorChanged(Color oldValue, Color newValue)
    {
        ApplyOutlineColor(newValue);
    }

    private void ApplyOutlineColor(Color color)
    {
        if (renderers == null)
        {
            return;
        }

        foreach (Renderer renderer in renderers)
        {
            if (renderer != null)
            {
                renderer.material.SetColor("_Outline_Color", color);
            }
        }
    }

    void Update()
    {
        if (isPaused)
        {
            return;
        }

        if (haveRequest)
        {
            UpdateIncomingRequest();
            return;
        }

        if (isTeamedUp)
        {
            if (endTeamUpAction.triggered)
            {
                if (GameManager.Instance != null)
                {
                    MessageBox.Informate("You have ended the team up with player " + GameManager.Instance.GetPlayerNickname((ulong)teamMateId), Color.red, MessagePriority.High);
                }
                OnExitTeamUp?.Invoke();

                EndTeamUpOnServer();
                if (teamMate != null)
                {
                    RemoveTeamMate();
                }
            }

            return;
        }

        TryToTeamUp();
    }

    // Accept with Interact, reject with End Team Up, or let the bar run out. Distance is checked
    // by the server when the answer arrives, so the responder does not have to stay face to face.
    private void UpdateIncomingRequest()
    {
        if (interactAction.triggered)
        {
            AcceptRequest();
            return;
        }

        if (endTeamUpAction.triggered || Time.time >= requestDeadline)
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.DeclineTeamUpServerRpc();
            }

            ClearIncomingRequest();
        }
    }

    private void AcceptRequest()
    {
        if (GameManager.Instance == null)
        {
            ClearIncomingRequest();
            return;
        }

        ulong requester = (ulong)requesterId;
        perfectDap = UnityEngine.Random.Range(0, 2);
        GameManager.Instance.TeamUpResponseServerRpc(requester, dapPosition.position, perfectDap);

        isTeamedUp = true;
        teamMateId = requesterId;
        MessageBox.Informate("You have teamed up with " + GameManager.Instance.GetPlayerNickname(requester), Color.green, MessagePriority.High);

        NetworkObject requesterObject = NetworkManager.Singleton.SpawnManager.GetPlayerNetworkObject(requester);
        AddTeamMate(requesterObject != null ? requesterObject.gameObject : null);

        ClearIncomingRequest();
    }

    private void TryToTeamUp()
    {
        TeamUp target = FindLookedAtPlayer();
        if (target == null)
        {
            InteractionPromptHUD.Hide();
            return;
        }

        ulong targetId = target.OwnerClientId;

        // Turned down recently: the prompt stays up but greyed, filling back to its own colour
        // as the block runs out, so the player can see when they may ask again.
        if (declinedUntil.TryGetValue(targetId, out var block))
        {
            float remaining = block.until - Time.time;
            if (remaining > 0f)
            {
                float progress = 1f - remaining / Mathf.Max(0.01f, block.length);
                InteractionPromptHUD.ShowCooldown("Team Up", interactAction, progress);
                return;
            }

            declinedUntil.Remove(targetId);
        }

        InteractionPromptHUD.Show("Team Up", interactAction);

        if (interactAction.triggered && GameManager.Instance != null && !BlocksInteract &&
            Time.time >= lastTeamUpTime + teamUpCooldown)
        {
            GameManager.Instance.TeamUpRequestServerRpc(targetId);
            lastTeamUpTime = Time.time;
            MessageBox.Informate("Team up request sent to " + GameManager.Instance.GetPlayerNickname(targetId), Color.yellow);
        }
    }

    // The nearby player the camera is most directly facing, if any is faced closely enough.
    private TeamUp FindLookedAtPlayer()
    {
        int numColliders = Physics.OverlapSphereNonAlloc(teamUpArea.position, teamUpRaduis, teamUpResults, otherPlayers);
        Camera cam = Camera.main;
        Transform view = cam != null ? cam.transform : transform;

        TeamUp best = null;
        float bestDot = lookAtThreshold;

        for (int i = 0; i < numColliders; i++)
        {
            TeamUp candidate = teamUpResults[i].GetComponentInParent<TeamUp>();
            if (candidate == null || candidate == this)
            {
                continue;
            }

            if (candidate.TryGetComponent(out Death death) && death.isDead.Value)
            {
                continue;
            }

            Vector3 toCandidate = candidate.transform.position + Vector3.up - view.position;
            float dot = Vector3.Dot(view.forward, toCandidate.normalized);
            if (dot > bestDot)
            {
                bestDot = dot;
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>The server forwarded another player's request. Interact is locked until this is
    /// answered, and the alert on the left counts down the time left to accept.</summary>
    public void RequestTeamUp(ulong requesterId, float timeout)
    {
        if (isTeamedUp)
        {
            return;
        }

        this.requesterId = (int)requesterId;
        haveRequest = true;
        localRequestPending = true;
        requestDeadline = Time.time + timeout;

        InteractionPromptHUD.Hide();

        string name = GameManager.Instance != null ? GameManager.Instance.GetPlayerNickname(requesterId) : "Player " + requesterId;
        TeamUpRequestHUD.Show(name, interactAction, endTeamUpAction, timeout);
    }

    /// <summary>Closes the incoming request alert without answering - the request was answered,
    /// timed out, or its sender left.</summary>
    public void ClearIncomingRequest()
    {
        if (haveRequest || localRequestPending)
        {
            requestClosedFrame = Time.frameCount;
        }

        haveRequest = false;
        localRequestPending = false;
        requesterId = -1;
        TeamUpRequestHUD.Hide();
    }

    /// <summary>The player this client asked said no, or ignored it. No asking them again for
    /// <paramref name="cooldown"/> seconds.</summary>
    public void OnRequestDeclined(ulong responderId, float cooldown)
    {
        declinedUntil[responderId] = (Time.time + cooldown, cooldown);

        if (GameManager.Instance != null)
        {
            MessageBox.Informate(GameManager.Instance.GetPlayerNickname(responderId) + " didn't team up with you.", Color.red);
        }
    }

    public void EndTeamUpOnServer()
    {
        GameManager.Instance.EndTeamUpServerRpc((ulong)teamMateId);
        EndTeamUp();
    }
    public void EndTeamUp()
    {
        isTeamedUp = false;
        teamMateId = -1;
    }

    public void PlayDapSound(Vector3 dapPosition, bool perfectDap)
    {
        if (VfxManager.Instance != null)
        {
            VfxManager.SpawnOneShot(VfxManager.Instance.teamUpDapVfxPrefab, dapPosition, VfxManager.Instance.teamUpDapVfxLifetime);
        }

        if (SFXManager.Instance == null) return;

        AudioClip clipToPlay = perfectDap ? SFXManager.Instance.perfectDapSound : SFXManager.Instance.dapSound;
        SFXManager.Instance.PlayAt(clipToPlay, dapPosition, UnityEngine.Random.Range(0.9f, 1.1f), SFXManager.Instance.dapMixerGroup);
    }

    /// <summary>Called on the requester once the server confirms the team-up.</summary>
    public void AddTeamMate()
    {
        NetworkObject mateObject = NetworkManager.Singleton != null && teamMateId >= 0
            ? NetworkManager.Singleton.SpawnManager.GetPlayerNetworkObject((ulong)teamMateId)
            : null;
        AddTeamMate(mateObject != null ? mateObject.gameObject : null);
    }

    private void AddTeamMate(GameObject mate)
    {
        teamMate = mate;
        OnTeamUp?.Invoke();
        // Outline color itself is applied server-side via outlineColor (see GameManager's
        // TeamUpResponseServerRpc) so every viewer sees it, not just this client.
    }

    public void RemoveTeamMate()
    {
        teamMate = null;
    }

    private void OnDrawGizmos() {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(teamUpArea.position, teamUpRaduis);
    }
}
