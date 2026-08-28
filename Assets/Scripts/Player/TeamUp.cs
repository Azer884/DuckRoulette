using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class TeamUp : NetworkBehaviour
{
    private InputAction interactAction, endTeamUpAction;
    private List<GameObject> validPlayers = new List<GameObject>();
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
    private int perfectDap = 0;
    public Transform dapPosition;
    public Renderer[] renderers;
    public Color teamColor = Color.green;

    public event System.Action OnTeamUp, OnExitTeamUp;

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            enabled = false;
            return;
        }

        InputActionAsset inputActions = GetComponent<InputSystem>().inputActions;
        interactAction = inputActions.FindAction("Interact");
        endTeamUpAction = inputActions.FindAction("EndTeamUp");
    }

    void Update()
    {
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

    // interactAction/endTeamUpAction each carry a Keyboard and a Gamepad binding at once, so
    // GetBindingDisplayString() with no group returns every matching binding joined with
    // " | " (e.g. "A | E") instead of just the one the player is actually using.
    private static string GetBindingDisplayString(InputAction action)
    {
        bool isGamepad = action.activeControl != null && action.activeControl.device is Gamepad;
        return action.GetBindingDisplayString(group: isGamepad ? "Gamepad" : "Keyboard");
    }

    private void TryToTeamUp()
    {
        int numColliders = Physics.OverlapSphereNonAlloc(teamUpArea.position, teamUpRaduis, teamUpResults, otherPlayers);
        validPlayers.Clear();

        for (int i = 0; i < numColliders; i++)
        {
            if (teamUpResults[i].GetComponentInParent<TeamUp>() != null)
            {
                TeamUp teamUpComponent = teamUpResults[i].GetComponentInParent<TeamUp>();
                if(teamUpComponent != GetComponentInParent<TeamUp>())
                {
                    validPlayers.Add(teamUpComponent.gameObject);
                }
            }
        }
        if (validPlayers?.Count > 0)
        {
            InteractionPromptHUD.Show("Team Up", GetBindingDisplayString(interactAction));

            if (interactAction.triggered)
            {
                if (GameManager.Instance != null)
                {
                    if (!isTeamedUp && Time.time >= lastTeamUpTime + teamUpCooldown && !haveRequest)
                    {
                        GameManager.Instance.TeamUpRequestServerRpc(validPlayers[0].GetComponent<NetworkObject>().OwnerClientId);
                        lastTeamUpTime = Time.time;
                    }
                    else if (haveRequest)
                    {
                        isTeamedUp = true;
                        haveRequest = false;
                        teamMateId = requesterId;
                        perfectDap = UnityEngine.Random.Range(0, 2);
                        //Play the dap animation and sound

                        GameManager.Instance.TeamUpResponseServerRpc((ulong)requesterId, dapPosition.position, perfectDap);
                        MessageBox.Informate("You have teamed up with player " + requesterId, Color.green, MessagePriority.High);

                        // Change the color of the player
                        AddTeamMate();
                    }
                }
            }
        }
        else
        {
            InteractionPromptHUD.Hide();

            if (haveRequest)
            {
                haveRequest = false;
            }
        }
    }

    public void RequestTeamUp(ulong requesterId)
    {
        if (isTeamedUp)
        {
            return;
        }
        if (GameManager.Instance != null)
        {
            MessageBox.Informate("Player " + GameManager.Instance.GetPlayerNickname(requesterId) + " wants to team up with you. Press E to accept.", Color.yellow, MessagePriority.Medium);
        }
        this.requesterId = (int)requesterId; // Store the requesterId
        haveRequest = true;
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
        if (SFXManager.Instance == null) return;

        AudioClip clipToPlay = perfectDap ? SFXManager.Instance.perfectDapSound : SFXManager.Instance.dapSound;
        SFXManager.Instance.PlayAt(clipToPlay, dapPosition, UnityEngine.Random.Range(0.9f, 1.1f), SFXManager.Instance.dapMixerGroup);
    }

    public void AddTeamMate()
    {
        teamMate = validPlayers[0];
        OnTeamUp?.Invoke();

        foreach (Renderer renderer in teamMate.GetComponent<TeamUp>().renderers)
        {
            renderer.material.SetColor("_Outline_Color", teamColor);
        }
    }

    public void RemoveTeamMate()
    {
        foreach (Renderer renderer in teamMate.GetComponent<TeamUp>().renderers)
        {
            renderer.material.SetColor("_Outline_Color", Color.black);
        }

        teamMate = null;
    }

    private void OnDrawGizmos() {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(teamUpArea.position, teamUpRaduis);
    }
}
