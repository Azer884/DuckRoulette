using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;

public class DeathTrigger : MonoBehaviour
{
    private ulong victimId;
    private ulong spectatedPlayerId;
    private CinemachineCamera spectatorCamera;
    private Death death;
    private NetworkObject parentNetworkObject;
    private InputAction spectateNextAction;

    // Only true once the camera has actually cut to spectate (after the dramatic death delay
    // below) - guards Update() so cycling/auto-retarget can't run against spectatedPlayerId
    // before the initial "watch your killer" call has even happened.
    private bool isSpectating;
    private float spectateValidityCheckTimer;

    private void Awake()
    {
        death = GetComponentInParent<Death>();
        parentNetworkObject = GetComponentInParent<NetworkObject>();

        // Was only ever set inside OnTriggerEnter, which the victim's own client can't be relied
        // on to ever fire (see HandleDeath) - set it here instead so it's always valid.
        victimId = parentNetworkObject.OwnerClientId;

        InputSystem inputSystem = GetComponentInParent<InputSystem>();
        if (inputSystem != null)
        {
            spectateNextAction = inputSystem.inputActions.FindAction("SpectateNext");
        }
    }

    private void Start()
    {
        death.isDead.OnValueChanged += HandleIsDeadChanged;
    }

    private void OnDestroy()
    {
        if (death != null)
        {
            death.isDead.OnValueChanged -= HandleIsDeadChanged;
        }
    }

    private void HandleIsDeadChanged(bool oldValue, bool newValue)
    {
        if (parentNetworkObject == null || !parentNetworkObject.IsOwner || newValue)
        {
            return;
        }

        // Respawned/round reset - clear spectate state so a stale camera/HUD doesn't linger.
        isSpectating = false;
        EndSpectate(spectatedPlayerId);
        SpectateHUD.HideSpectating();
    }

    public void OnTriggerEnter(Collider other)
    {
        if (!other.transform.parent.TryGetComponent(out BulletBehavior bullet))
        {
            return;
        }

        // OnTriggerEnter runs on every peer that locally simulates this collision (server,
        // victim, shooter, bystanders). The victim's own client is actually the LEAST reliable
        // detector here - their own transform has zero interpolation lag while the bullet is
        // simulated from a slightly-stale snapshot of them, so it can sail past their own collider
        // even when everyone else's screen shows it connecting. So nothing below is gated to the
        // victim's own client:
        //  - bullet despawn and kill credit: only the shooter's own client (bullet.IsOwner) - it
        //    detects exactly once per real hit, so this can't double-count kills.
        //  - alive-state + death/ragdoll: any peer that detects it, via GameManager (server-
        //    authoritative from there - see GameManager.SetPlayerDeadClientRpc).
        bool isFriendlyFire = GetComponentInParent<TeamUp>().isTeamedUp && (int)bullet.OwnerClientId == GetComponentInParent<TeamUp>().teamMateId;
        bool isValidHit = bullet.OwnerClientId != victimId && !death.isDead.Value && !isFriendlyFire;

        if (bullet.IsOwner)
        {
            bullet.DestroyServerRpc(0);
        }

        // This player has 13 DeathTrigger instances (one per hitbox collider), so the same bullet
        // can fire OnTriggerEnter on several of them in one frame - TryReportDeath dedupes that
        // per-client (see Death.cs) so a single shot can't be credited as multiple kills.
        if (!isValidHit || !death.TryReportDeath())
        {
            return;
        }

        if (bullet.IsOwner)
        {
            GameManager.Instance.UpdateKillsServerRpc(bullet.OwnerClientId, 1);
        }

        GameManager.Instance.UpdatePlayerStateServerRpc(victimId, bullet.OwnerClientId);
        Debug.Log($"Collision detected with {other.name}. Bullet Owner: {bullet.OwnerClientId}, Victim Owner: {victimId}");
    }

    // Called on the victim's own client by GameManager.SetPlayerDeadClientRpc once the server has
    // authoritatively marked them dead - NOT from this object's own OnTriggerEnter, which can't be
    // relied on to ever fire for the victim (see the comment in OnTriggerEnter above).
    public void HandleDeath(ulong shooterId)
    {
        spectatedPlayerId = shooterId;

        string shooterName = GameManager.Instance.GetPlayerNickname(shooterId);
        string victimName = GameManager.Instance.GetPlayerNickname(victimId);
        Debug.Log($"{shooterName} killed {victimName}");

        SpectateHUD.ShowDeathBanner(shooterName);

        StartCoroutine(WaitBeforeSpctate(5f));
    }

    private IEnumerator WaitBeforeSpctate(float delay)
    {
        yield return new WaitForSeconds(delay);

        Spectate(spectatedPlayerId);

        if (parentNetworkObject.IsOwner)
        {
            isSpectating = true;
            spectateValidityCheckTimer = 0f;
            ShowSpectateHud();
        }
    }

    private void Spectate(ulong playerId)
    {
        spectatorCamera = GameManager.Instance.GetPlayerSpectateCam(playerId);
        if(spectatorCamera == null)
        {
            return;
        }
        spectatorCamera.Priority = 20;
    }
    private void EndSpectate(ulong playerId)
    {
        if(spectatorCamera == null)
        {
            return;
        }
        spectatorCamera.Priority = 0;
    }

    private void Update()
    {
        if (!parentNetworkObject.IsOwner || !death.isDead.Value || !isSpectating)
        {
            return;
        }

        if (spectateNextAction != null && spectateNextAction.triggered)
        {
            CycleSpectateTarget();
            return;
        }

        // If whoever we're currently watching died (or disconnected) in the meantime, hop to
        // the next alive target automatically instead of leaving the player staring at a corpse.
        spectateValidityCheckTimer += Time.deltaTime;
        if (spectateValidityCheckTimer >= 0.5f)
        {
            spectateValidityCheckTimer = 0f;
            if (!IsAlive(spectatedPlayerId))
            {
                CycleSpectateTarget();
            }
        }
    }

    private void CycleSpectateTarget()
    {
        List<ulong> targets = GetAliveSpectateTargets();
        if (targets.Count == 0)
        {
            return;
        }

        // Previously this incremented spectatedPlayerId (a raw clientId) and wrapped it modulo
        // ConnectedClientsList.Count - clientIds aren't small sequential integers (especially
        // over a Steam transport), so that almost never landed on a real, let alone alive,
        // player. Cycling through an explicit alive-target list fixes both problems at once.
        int currentIndex = targets.IndexOf(spectatedPlayerId);
        int nextIndex = (currentIndex + 1) % targets.Count;

        EndSpectate(spectatedPlayerId);
        spectatedPlayerId = targets[nextIndex];
        Spectate(spectatedPlayerId);
        ShowSpectateHud();
    }

    private void ShowSpectateHud()
    {
        string targetName = GameManager.Instance != null ? GameManager.Instance.GetPlayerNickname(spectatedPlayerId) : "";
        string hint = spectateNextAction != null ? $"[{spectateNextAction.GetBindingDisplayString()}] Next" : "";
        SpectateHUD.ShowSpectating(targetName, hint);
    }

    private List<ulong> GetAliveSpectateTargets()
    {
        var targets = new List<ulong>();
        if (NetworkManager.Singleton == null)
        {
            return targets;
        }

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.ClientId == victimId || client.PlayerObject == null)
            {
                continue;
            }

            if (client.PlayerObject.TryGetComponent(out Death otherDeath) && !otherDeath.isDead.Value)
            {
                targets.Add(client.ClientId);
            }
        }

        return targets;
    }

    private bool IsAlive(ulong clientId)
    {
        return NetworkManager.Singleton != null &&
               NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client) &&
               client.PlayerObject != null &&
               client.PlayerObject.TryGetComponent(out Death otherDeath) &&
               !otherDeath.isDead.Value;
    }
}
