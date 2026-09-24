using System.Collections;
using System.Collections.Generic;
using Steamworks;
using TMPro;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using Weather;

public class GameManager : NetworkBehaviour
{
    public static GameManager Instance { get; private set; }
    public NetworkVariable<ulong> playerWithGun = new(ulong.MaxValue);
    public NetworkVariable<int> bulletPosition = new();
    public NetworkVariable<int> randomBulletPosition = new();
    public NetworkVariable<bool> isReloaded = new(false);
    public NetworkVariable<bool> canShoot = new(true),
        powerGunIsActive = new(false);

    private NetworkVariable<int> _alivePlayersCount = new(0);
    private readonly Dictionary<ulong, bool> _playerStates = new();
    private readonly Dictionary<ulong, int> _playersKills = new();
    private int _coinsToWin;
    private bool _isGameEnded;
    private bool _hasRained;
    private bool _isLeavingGame;
    private readonly List<(ulong, ulong)> _teams = new();
    // responderId -> requesterId, tracks requests the server actually sent out so a
    // TeamUpResponseServerRpc call can be validated against a real pending request.
    private readonly Dictionary<ulong, ulong> _pendingTeamUpRequests = new();
    // responderId -> Time.time the pending request above was sent, so an unanswered request
    // expires on the server even if the responder's client never reports back.
    private readonly Dictionary<ulong, float> _pendingTeamUpTimes = new();
    // (requester, responder) -> Time.time until which requester may not ask responder again,
    // set when a request is rejected or runs out.
    private readonly Dictionary<(ulong, ulong), float> _teamUpDeclineCooldowns = new();
    private Coroutine _switchPlayerRoutine;

    // --- Server-side anti-cheat bookkeeping (see Docs/SecurityAudit.md) ---
    // Every validated live shot, so a reported hit can be checked against a bullet that really
    // left this shooter's gun (and credited at most once) instead of trusting the reporter.
    private struct ShotRecord
    {
        public ulong ShooterId;
        public Vector3 Origin;
        public Vector3 Direction;
        public float Time;
        public bool Consumed;
    }
    private readonly List<ShotRecord> _recentShots = new();
    // Bumped on every gun hand-off; a shot stamps the serial it was fired in, so one turn can't
    // fire more than one bullet.
    private int _turnSerial;
    private int _shotFiredTurnSerial = -1;
    // (attacker, victim) -> slaps the server itself counted, gates StunPlayerServerRpc.
    private readonly Dictionary<(ulong, ulong), (int count, float lastTime)> _slapCounts = new();
    private readonly Dictionary<ulong, float> _lastTeamUpRequestTime = new();
    // Bullet lifetime (5s, BulletBehavior) plus late-report slack.
    private const float ShotRecordLifetime = 6.5f;
    private const float ShotTravelSlackSeconds = 1f;
    // Hitbox extents + owner-authoritative position lag at ~250ms RTT.
    public const float ShotHitLateralTolerance = 3.5f;
    private const float SlapMaxDistance = 5f;
    private const float SlapCountResetSeconds = 60f;
    private const float TeamUpMaxDistance = 8f;
    private const float TeamUpRequestCooldown = 4f;
    // How long the responder has to accept, and how long a turned-down requester has to wait
    // before asking that same player again.
    public const float TeamUpRequestTimeout = 8f;
    public const float TeamUpDeclineCooldown = 30f;

    [SerializeField, Tooltip("Percent chance that a shower that has already been rolled comes in " +
        "as a full storm instead - hail, a gale, and the campfire put out.")]
    private float strongRainChance = 18f;

    #region Events
    // Carries what was actually rolled, so WeatherSystem does not have to roll a second time and
    // the two can never disagree about whether this is a shower or a storm.
    public delegate void OnWheaterChange(WeatherPhase phase);
    public static event OnWheaterChange OnWeatherChange;
    public delegate void OnHostDisconnect();
    public static event OnHostDisconnect OnHostDisconnected;
    #endregion

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnect;
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        }
    }

    // OnNetworkSpawn only ever snapshots ConnectedClientsIds once, at the moment GameManager
    // itself spawns - a client whose connection is still finishing right at that instant (real
    // Steam/Facepunch latency, not the same-machine editor case) would never make it into
    // _playerStates, and MarkPlayerInactive's TryGetValue-miss treats an unknown id as already
    // dead: shooting them would credit the kill and destroy the bullet, but they'd never actually
    // ragdoll/die - looking like "this client can't be killed". Covers anyone who connects after
    // that snapshot too, in addition to it (not instead of).
    private void OnClientConnected(ulong clientId)
    {
        if (!IsServer || _playerStates.ContainsKey(clientId))
        {
            return;
        }

        _playerStates[clientId] = true;
        _alivePlayersCount.Value++;
        if (!_playersKills.ContainsKey(clientId))
        {
            _playersKills[clientId] = 0;
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Every peer listens, but HandleGunHolderChanged only reacts on the client that just
        // received the gun - the whole game is not knowing who is holding it.
        playerWithGun.OnValueChanged += HandleGunHolderChanged;

        foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            _playerStates[clientId] = true;
            if (!_playersKills.ContainsKey(clientId))
            {
                _playersKills[clientId] = 0;
            }
        }

        if (IsServer)
        {
            _alivePlayersCount.Value = NetworkManager.Singleton.ConnectedClientsIds.Count;

            // Player objects are spawned once (in the Loading scene, see PlayerSpawner) and
            // persist across matches rather than being recreated per match - isDead is only
            // ever set true (see UpdatePlayerStateServerRpc/SetPlayerDeadClientRpc above),
            // never reset, so without this, anyone who died in a PREVIOUS match starts this
            // one still flagged dead: DeathTrigger.OnTriggerEnter's isValidHit check reads
            // isDead.Value directly, so they'd be permanently unshootable from their second
            // match onward even though GameManager itself considers them alive again.
            foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
            {
                if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client) &&
                    client.PlayerObject != null &&
                    client.PlayerObject.TryGetComponent(out Death death))
                {
                    death.isDead.Value = false;
                }
            }

            if (_alivePlayersCount.Value > 0)
            {
                SetGunHolder(GetRandomClientId());
                UpdatePlayerShootingScripts();
                CheckPlayerGunScript();
            }
        }

        _coinsToWin = NetworkManager.Singleton.ConnectedClientsIds.Count * 5;
    }

    // Not a [ServerRpc] on purpose: its only legitimate caller is Shooting's own ownership-gated
    // ServerRpc (Shooting.cs), which Netcode already verified came from the shooting player's own
    // client. Exposing this directly as a client-callable RPC previously let any client report an
    // arbitrary clientId's shot as "true" to force that player's gun/bullet state out of turn.
    public void OnClientShotChanged(ulong clientId, bool hasShot)
    {
        // Only the current holder's trigger pull ends the turn - otherwise any player could flip
        // their own hasShot to skip someone else's turn and advance the chamber.
        if (!IsServer || !hasShot || !RpcValidation.IsGunHolder(clientId, playerWithGun.Value))
        {
            return;
        }

        RoundManager.Instance?.EndRound();

        SetGunHolder(GetRandomClientId(clientId));
        UpdatePlayerShootingScripts();
        bulletPosition.Value = (bulletPosition.Value + 1) % 6;
        CheckPlayerGunScript();
    }

    // Called by RoundManager when the gun holder's turn timer runs out - no shot happens, the
    // gun just passes to another player (the bullet chamber doesn't advance either, since no
    // trigger was pulled).
    public void PassGunOnTimeout()
    {
        if (!IsServer)
        {
            return;
        }

        SetGunHolder(GetRandomClientId(playerWithGun.Value));
        UpdatePlayerShootingScripts();
        CheckPlayerGunScript();
    }

    [ClientRpc]
    private void PlayerShootingScriptClientRpc(ulong shooterClientId)
    {
        // GameManager's own in-scene spawn can flush and call this before this client's local
        // player object has finished spawning (e.g. right after PlayerSpawner spawns the first
        // player) - GetLocalPlayerObject() is null in that window, so guard instead of NRE-ing.
        NetworkObject localPlayerObject = NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject();
        if (localPlayerObject != null && localPlayerObject.TryGetComponent<Shooting>(out var shootingScript))
        {
            // Mid Trigger/Reload animation: don't cut it off on a turn hand-off - HideGun's own
            // per-frame check (which runs regardless of this RPC) hides it as soon as the current
            // action actually finishes, instead of hard-cancelling it mid-play.
            if (shootingScript.enabled && (!shootingScript.canTrigger || !shootingScript.canShoot))
            {
                return;
            }

            // Every turn hand-off starts with the gun hidden - HideGun (always running, even
            // while Shooting is disabled) is what lets the newly assigned holder draw it back
            // out via the Change Weapon input.
            shootingScript.enabled = false;
        }
    }

    private void CheckPlayerGunScript()
    {
        // Every hand-off runs through here, after the new holder was picked - so an open task has
        // already cost its holder this turn before it ages or gets swapped.
        if (TaskManager.Instance != null)
        {
            TaskManager.Instance.OnGunHandedOff();
        }
        RoundManager.Instance?.StartRound();

        // The new gun holder must be able to trigger/reload as soon as their turn starts -
        // canShoot used to stay false for the full delay below, so every new holder was locked
        // out of shooting for 5s of their round timer for no visible reason.
        canShoot.Value = true;

        if (_switchPlayerRoutine != null)
        {
            StopCoroutine(_switchPlayerRoutine);
        }

        _switchPlayerRoutine = StartCoroutine(SwitchPlayerAfterDelay(5f));
    }

    // ReSharper disable Unity.PerformanceAnalysis
    private IEnumerator SwitchPlayerAfterDelay(float waitTime)
    {
        yield return new WaitForSeconds(waitTime);
        StartRain();

        if (!powerGunIsActive.Value)
        {
            UpdatePlayerShootingScripts();
        }

        _switchPlayerRoutine = null;
    }

    public void Reload()
    {
        randomBulletPosition.Value = Random.Range(0, 6);
        isReloaded.Value = true;
    }

    // killerClientId is only used to drive the victim's own death banner/spectate target - the
    // alive-state change itself (MarkPlayerInactive) doesn't need it. See SetPlayerDeadClientRpc
    // for why the ragdoll/isDead broadcast is server-driven from here instead of victim-owned RPCs.
    //
    // Any peer may still report the hit (bystanders detect it most reliably), but the report is
    // no longer trusted on its own: TryValidateShotKill requires a live shot the server itself
    // authorized for killerClientId, whose straight-line path passes near the victim, that hasn't
    // already killed someone. A modified client can no longer kill arbitrary players by id.
    [ServerRpc(RequireOwnership = false)]
    public void UpdatePlayerStateServerRpc(ulong clientId, ulong killerClientId)
    {
        if (!TryGetPlayerObject(clientId, out NetworkObject victimObject))
        {
            return;
        }

        // Hitbox centre, not the feet pivot.
        Vector3 victimCenter = victimObject.transform.position + Vector3.up;
        if (!TryValidateShotKill(clientId, killerClientId, victimCenter, ShotHitLateralTolerance))
        {
            return;
        }

        ApplyShotKill(clientId, killerClientId);
    }

    /// <summary>Server only. Records a shot ShootServerRpc already authorized, so later hit reports
    /// can be validated against it.</summary>
    public void RegisterShot(ulong shooterId, Vector3 origin, Vector3 direction)
    {
        if (!IsServer)
        {
            return;
        }

        PruneShotRecords();
        _recentShots.Add(new ShotRecord { ShooterId = shooterId, Origin = origin, Direction = direction, Time = Time.time });
    }

    /// <summary>Server only. True (and marks this turn's shot as fired) when senderId may fire a
    /// live round right now.</summary>
    public bool TryAuthorizeShot(ulong senderId)
    {
        if (!IsServer || _isGameEnded)
        {
            return false;
        }

        if (!RpcValidation.IsShotAllowed(senderId, playerWithGun.Value, canShoot.Value, isReloaded.Value,
                _shotFiredTurnSerial == _turnSerial, bulletPosition.Value, randomBulletPosition.Value))
        {
            return false;
        }

        _shotFiredTurnSerial = _turnSerial;
        return true;
    }

    /// <summary>Server only. Whether senderId may reload right now.</summary>
    public bool IsReloadAllowed(ulong senderId)
    {
        return IsServer && !_isGameEnded &&
               RpcValidation.IsReloadAllowed(senderId, playerWithGun.Value, canShoot.Value, isReloaded.Value);
    }

    /// <summary>Server only. Validates a reported bullet hit on victimId by killerId: victim alive
    /// and not killerId's teammate, and an unconsumed shot by killerId whose path passes within
    /// lateralTolerance of hitReferencePoint. Consumes that shot on success (one kill per bullet).
    /// Does not apply the kill - call ApplyShotKill.</summary>
    public bool TryValidateShotKill(ulong victimId, ulong killerId, Vector3 hitReferencePoint, float lateralTolerance)
    {
        if (!IsServer || _isGameEnded || !TryGetPlayerObject(victimId, out NetworkObject victimObject))
        {
            return false;
        }

        // Gated on Death.isDead.Value directly - the single authoritative source of truth for
        // "is this player already dead" - instead of on MarkPlayerInactive's own _playerStates
        // bookkeeping, which can desync from isDead (e.g. OnClientDisconnect's own path).
        bool victimDead = !victimObject.TryGetComponent(out Death victimDeath) || victimDeath.isDead.Value;
        if (!RpcValidation.IsKillCreditEligible(victimId, killerId, victimDead, RpcValidation.AreTeammates(_teams, victimId, killerId)))
        {
            return false;
        }

        PruneShotRecords();
        float now = Time.time;
        for (int i = _recentShots.Count - 1; i >= 0; i--)
        {
            ShotRecord shot = _recentShots[i];
            if (shot.ShooterId != killerId || shot.Consumed ||
                !RpcValidation.IsPointNearShotPath(hitReferencePoint, shot.Origin, shot.Direction, BulletBehavior.Speed,
                    now - shot.Time, lateralTolerance, ShotTravelSlackSeconds))
            {
                continue;
            }

            shot.Consumed = true;
            _recentShots[i] = shot;
            return true;
        }

        return false;
    }

    /// <summary>Server only. Applies a kill already validated by TryValidateShotKill: marks the
    /// victim inactive, broadcasts death/ragdoll, and credits the killer server-side.</summary>
    public void ApplyShotKill(ulong victimId, ulong killerId)
    {
        if (!IsServer)
        {
            return;
        }

        _playersKills[killerId] = (_playersKills.TryGetValue(killerId, out int kills) ? kills : 0) + 1;
        MarkPlayerInactive(victimId, reassignGun: false);
        SetPlayerDeadClientRpc(victimId, killerId);
    }

    private void PruneShotRecords()
    {
        float now = Time.time;
        _recentShots.RemoveAll(shot => now - shot.Time > ShotRecordLifetime);
    }

    private void SetGunHolder(ulong clientId)
    {
        playerWithGun.Value = clientId;
        _turnSerial++;
    }

    private bool TryGetPlayerObject(ulong clientId, out NetworkObject playerObject)
    {
        playerObject = null;
        if (NetworkManager.Singleton != null &&
            NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client) &&
            client.PlayerObject != null)
        {
            playerObject = client.PlayerObject;
            return true;
        }

        return false;
    }

    private static bool IsPlayerObjectDead(NetworkObject playerObject)
    {
        return playerObject.TryGetComponent(out Death death) && death.isDead.Value;
    }

    // Server-authoritative death/ragdoll broadcast. Death used to expose owner-gated ServerRpcs so
    // only the victim's own client could report their own death, but the victim's own transform has
    // zero interpolation lag (owner-authoritative) while the incoming bullet is simulated from a
    // slightly-stale snapshot of where they were - so the victim's own hit detection was the LEAST
    // reliable of anyone's, and ragdoll/death silently never triggered even as bystanders correctly
    // saw the hit connect. UpdatePlayerStateServerRpc is already called by whichever peer reliably
    // detects the hit (see DeathTrigger.OnTriggerEnter), so drive everything from here instead.
    [ClientRpc]
    private void SetPlayerDeadClientRpc(ulong clientId, ulong killerClientId)
    {
        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client) || client.PlayerObject == null)
        {
            return;
        }

        var playerObject = client.PlayerObject;

        // NetworkVariable default write permission is Server, not Owner, so the server can set
        // this directly here without needing a dedicated RPC.
        if (IsServer && playerObject.TryGetComponent(out Death death))
        {
            death.isDead.Value = true;
        }

        // Isolated: an exception here (e.g. a missing rig reference on some prefab variant)
        // must never stop the rest of this broadcast from running - the dying player's own
        // deathTrigger.HandleDeath call below is what actually moves them into spectate, and
        // a swallowed exception higher up used to silently strand them "dead" but stuck
        // controlling their corpse forever, looking like they can't be killed.
        if (playerObject.TryGetComponent(out Ragdoll ragdoll))
        {
            try
            {
                ragdoll.TriggerRagdoll(true);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
            }
        }

        if (VfxManager.Instance != null)
        {
            VfxManager.SpawnOneShot(VfxManager.Instance.deathVfxPrefab, playerObject.transform.position, VfxManager.Instance.deathVfxLifetime);
        }

        // This RPC already runs on every peer, so the elimination is audible to the whole lobby
        // from where it happened rather than only to the victim.
        if (SFXManager.Instance != null)
        {
            Vector3 deathPosition = playerObject.transform.position;
            // Elimination is a signature moment like the gunshot - keep it audible arena-wide.
            SFXManager.Instance.PlayAt(SFXManager.Instance.deathClip, deathPosition, minDistance: 10f, maxDistance: 80f, priority: 10);
            SFXManager.Instance.PlayAt(SFXManager.Instance.RandomBodyImpact(), deathPosition);
        }

        // DeathTrigger only ever exists on the 13 per-hitbox child colliders, never on
        // playerObject's own root GameObject - TryGetComponent (unlike GetComponentInChildren)
        // only ever checks the exact GameObject it's called on, so this silently never matched
        // and HandleDeath (which starts the death-banner/spectate coroutine) never ran for
        // anyone, ever. Any one of the 13 works identically - they all derive victimId from the
        // same parent NetworkObject.
        if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            DeathTrigger deathTrigger = playerObject.GetComponentInChildren<DeathTrigger>();
            if (deathTrigger != null)
            {
                deathTrigger.HandleDeath(killerClientId);
            }
        }
    }

    // RequireOwnership=false: this is called on GameManager's own NetworkObject (in-scene, owned
    // by the server) whenever ANY player slaps someone into a stun, not just the host - the
    // default RequireOwnership would only ever let the server/host's own client succeed here,
    // silently rejecting every non-host client's stun ("client cannot knockout host"). clientId is
    // just the target of a slap interaction - validated below against the sender (alive, in slap
    // range, and the server itself counted enough validated slaps on this victim).
    [ServerRpc(RequireOwnership = false)]
    public void StunPlayerServerRpc(ulong clientId, ServerRpcParams serverRpcParams = default)
    {
        ulong attackerId = serverRpcParams.Receive.SenderClientId;
        if (attackerId == clientId ||
            !TryGetPlayerObject(attackerId, out NetworkObject attackerObject) ||
            !TryGetPlayerObject(clientId, out NetworkObject victimObject) ||
            IsPlayerObjectDead(attackerObject) || IsPlayerObjectDead(victimObject) ||
            !RpcValidation.IsWithinDistance(attackerObject.transform.position, victimObject.transform.position, SlapMaxDistance))
        {
            return;
        }

        var key = (attackerId, clientId);
        if (!_slapCounts.TryGetValue(key, out var slaps) || !RpcValidation.HasEnoughSlapsForStun(slaps.count))
        {
            return;
        }

        _slapCounts.Remove(key);
        StunPlayer(clientId);
    }

    /// <summary>Server only. Records one validated slap (from Slap.SlapImpactServerRpc) toward a
    /// later stun request.</summary>
    public void RegisterSlap(ulong attackerId, ulong victimId)
    {
        if (!IsServer)
        {
            return;
        }

        var key = (attackerId, victimId);
        float now = Time.time;
        int count = _slapCounts.TryGetValue(key, out var slaps) && now - slaps.lastTime <= SlapCountResetSeconds ? slaps.count : 0;
        _slapCounts[key] = (count + 1, now);
    }

    /// <summary>Server only. Knockout for server-originated sources (e.g. Hail). Ignores dead players.</summary>
    public void StunPlayer(ulong clientId)
    {
        // A dead player's ragdoll is still a spawned player object - stunning it would run the
        // wake-up timer and stand the corpse back up.
        if (!IsServer || !TryGetPlayerObject(clientId, out NetworkObject playerObject) || IsPlayerObjectDead(playerObject))
        {
            return;
        }

        // Rolled once here and broadcast, not rolled per peer inside Ragdoll: the ragdoll recovery
        // state machine now runs on every copy of the player (it has to - it is what re-enables
        // their Animators), so a per-peer roll would have the same knockout last a different
        // length of time on every screen.
        StunPlayerClientRpc(clientId, Random.Range(3f, 6f));
    }

    [ClientRpc]
    private void StunPlayerClientRpc(ulong clientId, float wakeUpTime)
    {
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
        {
            var playerObject = client.PlayerObject;
            if (playerObject != null)
            {
                playerObject.GetComponent<Ragdoll>().TriggerRagdoll(isDead: false, wakeUpTime);

                if (VfxManager.Instance != null)
                {
                    Vector3 headPosition = playerObject.transform.position + VfxManager.Instance.stunVfxHeadOffset;
                    VfxManager.SpawnOneShot(VfxManager.Instance.stunVfxPrefab, headPosition, VfxManager.Instance.stunVfxLifetime);
                }

                if (SFXManager.Instance != null)
                {
                    SFXManager.Instance.PlayAt(SFXManager.Instance.stunClip, playerObject.transform.position);
                    SFXManager.Instance.PlayAt(SFXManager.Instance.RandomBodyImpact(), playerObject.transform.position);
                }
            }
        }
    }

    private void EndGame(ulong winnerId)
    {
        if (_isGameEnded)
        {
            return;
        }

        RoundManager.Instance?.EndRound();

        if (_switchPlayerRoutine != null)
        {
            StopCoroutine(_switchPlayerRoutine);
            _switchPlayerRoutine = null;
        }

        StartCoroutine(EndGameAfterStatsSync(winnerId));
    }

    // shotCounter/emptyShots/timeSurvived on each player's Stats are Owner-written NetworkVariables
    // that only ever get set once, here, via UpdateStatsClientRpc - before that they're still the
    // NetworkVariable default (0). Sending EndGameClientRpc in the same instant only guarantees the
    // LOCAL player's own write is visible immediately; every other player's write still has to round
    // trip client -> server -> every other client before EndGameClientRpc's TryGetPlayerStats reads
    // would see it, so without a wait here the end screen showed stale/zeroed stats for everyone but
    // yourself.
    private IEnumerator EndGameAfterStatsSync(ulong winnerId)
    {
        UpdateStatsClientRpc();
        yield return new WaitForSeconds(0.5f);

        var playerIds = new List<ulong>();
        var killCounts = new List<int>();
        foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            playerIds.Add(clientId);
            killCounts.Add(_playersKills.TryGetValue(clientId, out var killCount) ? killCount : 0);
        }

        EndGameClientRpc(winnerId, playerIds.ToArray(), killCounts.ToArray());
    }

    [ClientRpc]
    private void UpdateStatsClientRpc()
    {
        NetworkObject localPlayerObject = NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject();
        if (localPlayerObject != null && localPlayerObject.TryGetComponent<Stats>(out var stats))
        {
            stats.timeSurvived.Value = StatTracker.Instance.timeSurvived;
            stats.shotCounter.Value = stats.GetComponent<Shooting>().shotCounter;
            stats.emptyShots.Value = stats.GetComponent<Shooting>().emptyShots;
        }
    }

    [ClientRpc]
    private void EndGameClientRpc(ulong winnerId, ulong[] playerIds, int[] killCounts)
    {
        if (_isGameEnded) return;
        _isGameEnded = true;

        Cursor.lockState = CursorLockMode.Confined;
        PlayerSpawner.Instance.isStarted = false;

        Debug.Log($"Game Over! {GetPlayerNickname(winnerId)} Won.");
        NetworkObject localPlayerObject = NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject();
        if (localPlayerObject != null && localPlayerObject.TryGetComponent<PauseMenu>(out var pauseMenu))
        {
            pauseMenu.End();
            int localCoinReward = 0;

            for (int i = 0; i < playerIds.Length; i++)
            {
                ulong clientId = playerIds[i];
                int playerKillCount = i < killCounts.Length ? killCounts[i] : 0;

                GameObject currentPlayer = Instantiate(pauseMenu.playerStatsObj, pauseMenu.endGamePanel.transform.GetChild(0).GetChild(6));

                TextMeshProUGUI stat = currentPlayer.transform.GetChild(0).GetComponent<TextMeshProUGUI>();
                stat.text = GetPlayerNickname(clientId);
                if (clientId == winnerId)
                {
                    stat.color = Color.yellow;
                }

                stat = currentPlayer.transform.GetChild(1).GetComponent<TextMeshProUGUI>();
                stat.text = playerKillCount.ToString();

                if (clientId == NetworkManager.Singleton.LocalClientId)
                    StatTracker.Instance.kills = playerKillCount;

                int coins = playerKillCount * 2 + 1;
                if (clientId == winnerId)
                {
                    coins += _coinsToWin;
                }

                stat = currentPlayer.transform.GetChild(2).GetComponent<TextMeshProUGUI>();
                stat.text = $"{coins}";

                if (clientId == NetworkManager.Singleton.LocalClientId)
                {
                    localCoinReward = coins;
                    StatTracker.Instance.coinsWon = coins;
                }

                if (TryGetPlayerStats(clientId, out var playerStats))
                {
                    int minutes = Mathf.FloorToInt(playerStats.timeSurvived.Value / 60f);
                    int seconds = Mathf.FloorToInt(playerStats.timeSurvived.Value % 60f);
                    string formattedTime = $"{minutes:D2}m {seconds:D2}s";

                    stat = currentPlayer.transform.GetChild(3).GetComponent<TextMeshProUGUI>();
                    stat.text = formattedTime;

                    stat = currentPlayer.transform.GetChild(4).GetComponent<TextMeshProUGUI>();
                    stat.text = "0%";
                    if (playerStats.shotCounter.Value > 0)
                    {
                        stat.text = ((playerKillCount / (float)playerStats.shotCounter.Value) * 100f).ToString("0") + "%";
                    }

                    if (currentPlayer.transform.childCount > 5)
                    {
                        int totalTriggerPulls = playerStats.shotCounter.Value + playerStats.emptyShots.Value;
                        float luck = totalTriggerPulls > 0
                            ? (playerStats.emptyShots.Value / (float)totalTriggerPulls) * 100f
                            : 0f;

                        stat = currentPlayer.transform.GetChild(5).GetComponent<TextMeshProUGUI>();
                        stat.text = $"{luck:0}%";
                    }
                }
            }

            if (Coin.Instance != null && localCoinReward > 0)
            {
                Coin.Instance.UpdateCoinAmount(localCoinReward);

                if (SFXManager.Instance != null)
                {
                    SFXManager.Instance.PlayUI(SFXManager.Instance.coinRewardClip);
                }
            }

            // Local-player result stinger. PlayUI (2D) on purpose: the match is over and the
            // camera is on the results panel, so a positional one-shot would be inaudible.
            bool localPlayerWon = NetworkManager.Singleton.LocalClientId == winnerId;
            if (SFXManager.Instance != null)
            {
                SFXManager.Instance.PlayUI(localPlayerWon
                    ? SFXManager.Instance.victoryClip
                    : SFXManager.Instance.defeatClip);
            }

            if (localPlayerWon && VfxManager.Instance != null &&
                NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject() != null)
            {
                VfxManager.SpawnOneShot(
                    VfxManager.Instance.victoryVfxPrefab,
                    NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject().transform.position,
                    VfxManager.Instance.victoryVfxLifetime);
            }
        }
    }

    public string GetPlayerNickname(ulong clientId)
    {
        if (NetworkManager.Singleton == null)
        {
            return "Unknown Player";
        }

        foreach (var playerObject in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (playerObject.ClientId == clientId && playerObject.PlayerObject != null)
            {
                if (playerObject.PlayerObject.TryGetComponent<Username>(out var username))
                {
                    return username.playerName.Value.ToString();
                }
            }
        }

        return "Unknown Player";
    }

    private void OnClientDisconnect(ulong clientId)
    {
        if (!IsServer)
        {
            return;
        }

        Debug.Log($"{GetPlayerNickname(clientId)} has left the game.");
        MarkPlayerInactive(clientId, reassignGun: true);

        _lastTeamUpRequestTime.Remove(clientId);
        foreach (var slapKey in new List<(ulong, ulong)>(_slapCounts.Keys))
        {
            if (slapKey.Item1 == clientId || slapKey.Item2 == clientId)
            {
                _slapCounts.Remove(slapKey);
            }
        }

        _pendingTeamUpRequests.Remove(clientId);
        _pendingTeamUpTimes.Remove(clientId);
        foreach (ulong responderId in new List<ulong>(_pendingTeamUpRequests.Keys))
        {
            if (_pendingTeamUpRequests[responderId] == clientId)
            {
                _pendingTeamUpRequests.Remove(responderId);
                _pendingTeamUpTimes.Remove(responderId);
                CancelTeamUpRequestClientRpc(TargetOnly(responderId));
            }
        }

        foreach (var cooldownKey in new List<(ulong, ulong)>(_teamUpDeclineCooldowns.Keys))
        {
            if (cooldownKey.Item1 == clientId || cooldownKey.Item2 == clientId)
            {
                _teamUpDeclineCooldowns.Remove(cooldownKey);
            }
        }

        // Without this, a surviving teammate stays isTeamedUp = true pointing at a player who's
        // gone - stuck "teamed up" with a ghost, unable to team up with anyone else.
        var brokenTeams = _teams.FindAll(team => team.Item1 == clientId || team.Item2 == clientId);
        foreach (var team in brokenTeams)
        {
            ulong survivorId = team.Item1 == clientId ? team.Item2 : team.Item1;
            _teams.Remove(team);
            SetPlayerOutlineColor(survivorId, Color.black);

            var clientRpcParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new List<ulong> { survivorId }
                }
            };
            SendEndTeamUpClientRpc(clientRpcParams);
        }
    }

    // Turn hand-off had no feedback of any kind: the gun could arrive silently mid-walk and the
    // 30s shot clock would already be running before the holder noticed.
    private void HandleGunHolderChanged(ulong previousHolder, ulong newHolder)
    {
        if (NetworkManager.Singleton == null || newHolder != NetworkManager.Singleton.LocalClientId ||
            previousHolder == newHolder)
        {
            return;
        }

        if (SFXManager.Instance != null)
        {
            SFXManager.Instance.PlayUI(SFXManager.Instance.turnStartClip);
        }

        NetworkObject localPlayerObject = NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject();
        if (VfxManager.Instance != null && localPlayerObject != null)
        {
            VfxManager.SpawnOneShot(
                VfxManager.Instance.turnStartVfxPrefab,
                localPlayerObject.transform.position,
                VfxManager.Instance.turnStartVfxLifetime);
        }
    }

    public void OnDisable()
    {
        playerWithGun.OnValueChanged -= HandleGunHolderChanged;

        if (this == Instance)
        {
            OnHostDisconnected?.Invoke();

            if (!_isLeavingGame)
            {
                LeaveGame();
            }
        }

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnect;
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        }
    }

    public void LeaveGame()
    {
        if (_isLeavingGame)
        {
            return;
        }

        _isLeavingGame = true;
        LeaveSteamLobby();

        // InteractionPromptHUD is DontDestroyOnLoad, so a prompt visible the instant the player
        // leaves would otherwise survive the scene load and stay stuck on screen in the Lobby.
        InteractionPromptHUD.Hide();

        PlayerSpawner.Instance.isStarted = false;
        Cursor.lockState = CursorLockMode.Confined;
        SceneManager.LoadScene("Lobby");

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.Shutdown();
        }
    }

    public void LeaveSteamLobby()
    {
        if (SteamClient.IsValid && LobbySaver.instance != null && LobbySaver.instance.currentLobby != null)
        {
            LobbySaver.instance.currentLobby?.Leave();

            if (LobbyManager.instance != null)
            {
                LobbyManager.instance.playerInfo.Remove(OwnerClientId);
            }

            Debug.Log("Left Steam lobby successfully.");
        }
    }

    // Intentionally a no-op, kept only so older call sites/builds still bind. Kill credit used to
    // be whatever shooterId any client named here (anyone could inflate anyone's kills and the
    // coin reward derived from them); it is now awarded server-side in ApplyShotKill, only for a
    // kill TryValidateShotKill accepted.
    [ServerRpc(RequireOwnership = false)]
    public void UpdateKillsServerRpc(ulong shooterId, int killAmount)
    {
    }

    #region TeamUp

    [ServerRpc(RequireOwnership = false)]
    public void TeamUpRequestServerRpc(ulong teamMateId, ServerRpcParams serverRpcParams = default)
    {
        ulong requesterId = serverRpcParams.Receive.SenderClientId;
        float now = Time.time;

        // The target must be a different, connected, in-range player, neither side already
        // teamed, and requests are rate-limited (client cooldown is 5s) - otherwise any client
        // could spam/hijack pending requests for any player from anywhere on the map.
        if (teamMateId == requesterId ||
            !TryGetPlayerObject(requesterId, out NetworkObject requesterObject) ||
            !TryGetPlayerObject(teamMateId, out NetworkObject teamMateObject) ||
            RpcValidation.IsInAnyTeam(_teams, requesterId) || RpcValidation.IsInAnyTeam(_teams, teamMateId) ||
            !RpcValidation.IsWithinDistance(requesterObject.transform.position, teamMateObject.transform.position, TeamUpMaxDistance) ||
            (_lastTeamUpRequestTime.TryGetValue(requesterId, out float lastRequest) &&
             !RpcValidation.IsCooldownElapsed(lastRequest, now, TeamUpRequestCooldown)) ||
            // The target is already answering someone, or turned this requester down recently.
            _pendingTeamUpRequests.ContainsKey(teamMateId) ||
            (_teamUpDeclineCooldowns.TryGetValue((requesterId, teamMateId), out float blockedUntil) && now < blockedUntil))
        {
            return;
        }

        _lastTeamUpRequestTime[requesterId] = now;
        _pendingTeamUpRequests[teamMateId] = requesterId;
        _pendingTeamUpTimes[teamMateId] = now;

        // "Try to team up" only asks for the attempt - the other player does not have to accept.
        if (TaskManager.Instance != null)
        {
            TaskManager.Instance.CompleteTaskForPlayer(requesterId, TaskManager.Instance.TeamUpTask);
        }

        SendTeamUpRequestClientRpc(requesterId, TeamUpRequestTimeout, TargetOnly(teamMateId));
    }

    private static ClientRpcParams TargetOnly(ulong clientId)
    {
        return new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new List<ulong> { clientId } }
        };
    }

    [ClientRpc]
    private void SendTeamUpRequestClientRpc(ulong senderId, float timeout, ClientRpcParams clientRpcParams = default)
    {
        _ = clientRpcParams;
        NetworkObject localPlayer = NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject();
        if (localPlayer != null && localPlayer.TryGetComponent<TeamUp>(out var teamUp))
        {
            if (teamUp.isTeamedUp)
            {
                return;
            }

            teamUp.RequestTeamUp(senderId, timeout);
        }
    }

    /// <summary>The responder said no (or let the timer run out). The requester is blocked from
    /// asking this player again for TeamUpDeclineCooldown seconds.</summary>
    [ServerRpc(RequireOwnership = false)]
    public void DeclineTeamUpServerRpc(ServerRpcParams serverRpcParams = default)
    {
        ulong responderId = serverRpcParams.Receive.SenderClientId;
        if (_pendingTeamUpRequests.TryGetValue(responderId, out ulong requesterId))
        {
            ExpireTeamUpRequest(responderId, requesterId);
        }
    }

    private void ExpireTeamUpRequest(ulong responderId, ulong requesterId)
    {
        _pendingTeamUpRequests.Remove(responderId);
        _pendingTeamUpTimes.Remove(responderId);
        _teamUpDeclineCooldowns[(requesterId, responderId)] = Time.time + TeamUpDeclineCooldown;

        TeamUpDeclinedClientRpc(responderId, TeamUpDeclineCooldown, TargetOnly(requesterId));
        CancelTeamUpRequestClientRpc(TargetOnly(responderId));
    }

    // The server's own clock for unanswered requests - a responder whose client never reports
    // back (alt-tabbed, crashed) must not hold the requester or themselves in limbo.
    private void Update()
    {
        if (!IsServer || _pendingTeamUpTimes.Count == 0)
        {
            return;
        }

        float now = Time.time;
        foreach (ulong responderId in new List<ulong>(_pendingTeamUpTimes.Keys))
        {
            // Small grace so the responder's own timeout (same length) lands first.
            if (now - _pendingTeamUpTimes[responderId] > TeamUpRequestTimeout + 1f &&
                _pendingTeamUpRequests.TryGetValue(responderId, out ulong requesterId))
            {
                ExpireTeamUpRequest(responderId, requesterId);
            }
        }
    }

    [ClientRpc]
    private void TeamUpDeclinedClientRpc(ulong responderId, float cooldown, ClientRpcParams clientRpcParams = default)
    {
        _ = clientRpcParams;
        NetworkObject localPlayer = NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject();
        if (localPlayer != null && localPlayer.TryGetComponent<TeamUp>(out var teamUp))
        {
            teamUp.OnRequestDeclined(responderId, cooldown);
        }
    }

    [ClientRpc]
    private void CancelTeamUpRequestClientRpc(ClientRpcParams clientRpcParams = default)
    {
        _ = clientRpcParams;
        NetworkObject localPlayer = NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject();
        if (localPlayer != null && localPlayer.TryGetComponent<TeamUp>(out var teamUp))
        {
            teamUp.ClearIncomingRequest();
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void TeamUpResponseServerRpc(ulong requesterId, Vector3 soundPosition, int isPerfectDap, ServerRpcParams serverRpcParams = default)
    {
        ulong responderId = serverRpcParams.Receive.SenderClientId;

        // Only accept a response to a request the server actually sent this responder - closes
        // an exploit where a client could fabricate an arbitrary requesterId to fake a team-up.
        if (!_pendingTeamUpRequests.TryGetValue(responderId, out ulong pendingRequesterId) || pendingRequesterId != requesterId)
        {
            return;
        }
        _pendingTeamUpRequests.Remove(responderId);
        _pendingTeamUpTimes.Remove(responderId);

        // A stale request can't create a second team for someone already teamed, or join two
        // players who have since walked apart.
        if (RpcValidation.IsInAnyTeam(_teams, requesterId) || RpcValidation.IsInAnyTeam(_teams, responderId) ||
            !TryGetPlayerObject(requesterId, out NetworkObject requesterObject) ||
            !TryGetPlayerObject(responderId, out NetworkObject responderObject) ||
            !RpcValidation.IsWithinDistance(requesterObject.transform.position, responderObject.transform.position, TeamUpMaxDistance))
        {
            return;
        }

        // Cosmetic, but broadcast to everyone - don't let it be placed anywhere on the map.
        if (!RpcValidation.IsWithinDistance(soundPosition, responderObject.transform.position, TeamUpMaxDistance))
        {
            soundPosition = responderObject.transform.position;
        }

        var clientRpcParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new List<ulong> { requesterId }
            }
        };
        bool isPerfectDapBool = isPerfectDap == 1;
        PlayDapSoundClientRpc(soundPosition, isPerfectDapBool);

        _teams.Add((requesterId, responderId));

        Color color = Color.green;
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(responderId, out var responderClient) &&
            responderClient.PlayerObject != null &&
            responderClient.PlayerObject.TryGetComponent(out TeamUp responderTeamUp))
        {
            color = responderTeamUp.teamColor;
        }
        SetPlayerOutlineColor(requesterId, color);
        SetPlayerOutlineColor(responderId, color);

        SendTeamUpResponseClientRpc(responderId, clientRpcParams);
    }

    // Server-authoritative outline color so every peer (not just the two teamed players) sees it
    // on both players, not just each other's local view of them.
    private void SetPlayerOutlineColor(ulong clientId, Color color)
    {
        if (NetworkManager.Singleton != null &&
            NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client) &&
            client.PlayerObject != null &&
            client.PlayerObject.TryGetComponent(out TeamUp teamUp))
        {
            teamUp.outlineColor.Value = color;
        }
    }

    [ClientRpc]
    private void SendTeamUpResponseClientRpc(ulong teamMateId, ClientRpcParams clientRpcParams = default)
    {
        _ = clientRpcParams;
        if (NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject().TryGetComponent<TeamUp>(out var teamUp))
        {
            teamUp.isTeamedUp = true;
            teamUp.teamMateId = (int)teamMateId;
            teamUp.AddTeamMate();
            MessageBox.Informate("You have teamed up with " + GetPlayerNickname(teamMateId), Color.green);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void EndTeamUpServerRpc(ulong teamMateId, ServerRpcParams serverRpcParams = default)
    {
        // Only a team the sender is actually part of can be ended - teamMateId used to be trusted
        // outright, letting any client break up (and un-outline) any other player's team.
        if (!RpcValidation.AreTeammates(_teams, serverRpcParams.Receive.SenderClientId, teamMateId))
        {
            return;
        }

        var clientRpcParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new List<ulong> { teamMateId }
            }
        };

        _teams.RemoveAll(team => (team.Item1 == serverRpcParams.Receive.SenderClientId && team.Item2 == teamMateId) ||
                                 (team.Item1 == teamMateId && team.Item2 == serverRpcParams.Receive.SenderClientId));

        SetPlayerOutlineColor(serverRpcParams.Receive.SenderClientId, Color.black);
        SetPlayerOutlineColor(teamMateId, Color.black);

        SendEndTeamUpClientRpc(clientRpcParams);
    }

    [ClientRpc]
    private void SendEndTeamUpClientRpc(ClientRpcParams clientRpcParams = default)
    {
        _ = clientRpcParams;
        if (NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject().TryGetComponent<TeamUp>(out var teamUp))
        {
            teamUp.EndTeamUp();
            teamUp.RemoveTeamMate();
        }
    }

    [ClientRpc]
    private void PlayDapSoundClientRpc(Vector3 soundPosition, bool isPerfectDap)
    {
        if (NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject().TryGetComponent<TeamUp>(out var teamUp))
        {
            teamUp.PlayDapSound(soundPosition, isPerfectDap);
        }
    }

    #endregion

    #region  Spectate

    public CinemachineCamera GetPlayerSpectateCam(ulong clientId)
    {
        foreach (var playerObject in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (playerObject.ClientId == clientId && playerObject.PlayerObject != null)
            {
                if (playerObject.PlayerObject.transform.GetChild(playerObject.PlayerObject.transform.childCount - 1).TryGetComponent<CinemachineCamera>(out var cam))
                {
                    return cam;
                }
            }
        }

        return null;
    }

    #endregion

    public bool Percentage(float percentageChance)
    {
        if (percentageChance < 100)
        {
            int randomValue = Random.Range(0, 100);
            return randomValue < percentageChance;
        }
        return true;
    }

    [ClientRpc]
    private void NotifyPlayersClientRpc(string message, bool activateCoolDown = false, int coolDownTime = 0)
    {
        Debug.Log(message);

        if (activateCoolDown && NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject().TryGetComponent<UIManager>(out var uiManager))
        {
            uiManager.StartCoolDown(coolDownTime);
        }
    }

    private static void OnWeatherChanged(WeatherPhase phase)
    {
        OnWeatherChange?.Invoke(phase);
    }

    private void StartRain()
    {
        // The roll is a match decision, so it stays on the server like every other one here.
        if (!IsServer || _hasRained)
        {
            return;
        }

        float percentageChance = Mathf.Pow(1.0155f, Time.timeSinceLevelLoad);

        // Nights are wetter: the same climbing roll is simply worth more after dusk.
        if (WeatherSystem.Instance != null)
        {
            percentageChance *= WeatherSystem.Instance.RainChanceMultiplier;
        }

        if (!Percentage(percentageChance))
        {
            return;
        }

        _hasRained = true;

        // The small second roll: most showers stay showers, a few become the storm that brings
        // hail, the gale and the doused campfire.
        WeatherPhase phase = Percentage(strongRainChance) ? WeatherPhase.Storm : WeatherPhase.Rain;
        OnWeatherChanged(phase);
    }

    public bool IsGameEnded => _isGameEnded;

    /// <summary>Server only. Alive and still in the match.</summary>
    public bool IsPlayerAlive(ulong clientId) => _playerStates.TryGetValue(clientId, out bool isAlive) && isAlive;

    /// <summary>Server only. Whether this player is currently teamed up with someone.</summary>
    public bool IsInAnyTeam(ulong clientId) => RpcValidation.IsInAnyTeam(_teams, clientId);

    public int AlivePlayersCount()
    {
        return _alivePlayersCount.Value;
    }

    private void UpdatePlayerShootingScripts()
    {
        if (NetworkManager.Singleton == null)
        {
            return;
        }

        // One broadcast (each client compares against the synced gun-holder locally) instead of
        // one broadcast per connected client - was O(N^2) network messages for N players since
        // every one of those per-client RPCs still went out to all N clients.
        PlayerShootingScriptClientRpc(playerWithGun.Value);
    }

    private ulong GetRandomClientId(ulong excludedClientId = ulong.MaxValue)
    {
        if (NetworkManager.Singleton == null)
        {
            return ulong.MaxValue;
        }

        List<ulong> aliveClientIds = new();
        // Alive AND finished everything they were handed last round - the gun is the reward for
        // getting your tasks done, so an idle player keeps getting passed over.
        List<ulong> taskCompleteClientIds = new();

        foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if (clientId == excludedClientId)
            {
                continue;
            }

            if (_playerStates.TryGetValue(clientId, out bool isAlive) && isAlive)
            {
                aliveClientIds.Add(clientId);

                if (TaskManager.Instance == null || TaskManager.Instance.HasCompletedAllTasks(clientId))
                {
                    taskCompleteClientIds.Add(clientId);
                }
            }
        }

        // Falling back to the full alive pool is deliberate: if a whole round goes by and nobody
        // finishes their tasks, the hand-off still has to happen or the match just stops. Task
        // completion decides WHO gets the gun, it never decides whether the game continues.
        List<ulong> eligibleClientIds = taskCompleteClientIds.Count > 0 ? taskCompleteClientIds : aliveClientIds;

        if (eligibleClientIds.Count == 0)
        {
            foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
            {
                if (clientId != excludedClientId)
                {
                    eligibleClientIds.Add(clientId);
                }
            }
        }

        if (eligibleClientIds.Count == 0)
        {
            return excludedClientId;
        }

        return eligibleClientIds[Random.Range(0, eligibleClientIds.Count)];
    }

    private bool TryGetPlayerStats(ulong clientId, out Stats stats)
    {
        stats = null;

        if (NetworkManager.Singleton == null)
        {
            return false;
        }

        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client) && client.PlayerObject != null)
        {
            return client.PlayerObject.TryGetComponent(out stats);
        }

        return false;
    }

    // Returns whether this call actually transitioned the player from alive to inactive (false
    // when they were already inactive) - callers use that to avoid re-broadcasting death effects.
    private bool MarkPlayerInactive(ulong clientId, bool reassignGun)
    {
        if (!_playerStates.TryGetValue(clientId, out bool isAlive) || !isAlive)
        {
            return false;
        }

        _playerStates[clientId] = false;
        _alivePlayersCount.Value = Mathf.Max(0, _alivePlayersCount.Value - 1);
        if (TaskManager.Instance != null)
        {
            TaskManager.Instance.OnPlayerRemoved(clientId);
        }

        if (reassignGun && playerWithGun.Value == clientId)
        {
            RoundManager.Instance?.EndRound();
            SetGunHolder(GetRandomClientId(clientId));
            UpdatePlayerShootingScripts();

            // Don't start a new round for the sole survivor - the game is about to end below.
            if (playerWithGun.Value != clientId && playerWithGun.Value != ulong.MaxValue && _alivePlayersCount.Value > 1)
            {
                CheckPlayerGunScript();
            }
        }

        if (_alivePlayersCount.Value <= 1)
        {
            EndGame(GetAlivePlayerId());
        }

        return true;
    }

    private ulong GetAlivePlayerId()
    {
        foreach (var playerState in _playerStates)
        {
            if (playerState.Value)
            {
                return playerState.Key;
            }
        }

        return ulong.MaxValue;
    }
}
