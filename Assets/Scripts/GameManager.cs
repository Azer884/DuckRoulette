using System.Collections;
using System.Collections.Generic;
using Steamworks;
using TMPro;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

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
    private readonly Dictionary<ulong, List<PlayerTask>> _allPlayersTasks = new();
    private Coroutine _switchPlayerRoutine;

    #region Events
    public delegate void OnWheaterChange();
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
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

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
            if (_alivePlayersCount.Value > 0)
            {
                playerWithGun.Value = GetRandomClientId();
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
        if (!IsServer || !hasShot)
        {
            return;
        }

        RoundManager.Instance?.EndRound();

        playerWithGun.Value = GetRandomClientId(clientId);
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

        playerWithGun.Value = GetRandomClientId(playerWithGun.Value);
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
            // Every turn hand-off starts with the gun hidden - HideGun (always running, even
            // while Shooting is disabled) is what lets the newly assigned holder draw it back
            // out via the Change Weapon input.
            shootingScript.enabled = false;
        }
    }

    private void CheckPlayerGunScript()
    {
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

        DistributeTasks();
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
    [ServerRpc(RequireOwnership = false)]
    public void UpdatePlayerStateServerRpc(ulong clientId, ulong killerClientId)
    {
        if (MarkPlayerInactive(clientId, reassignGun: false))
        {
            SetPlayerDeadClientRpc(clientId, killerClientId);
        }
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

        if (playerObject.TryGetComponent(out Ragdoll ragdoll))
        {
            ragdoll.TriggerRagdoll(true);
        }

        if (VfxManager.Instance != null)
        {
            VfxManager.SpawnOneShot(VfxManager.Instance.deathVfxPrefab, playerObject.transform.position, VfxManager.Instance.deathVfxLifetime);
        }

        if (clientId == NetworkManager.Singleton.LocalClientId &&
            playerObject.TryGetComponent(out DeathTrigger deathTrigger))
        {
            deathTrigger.HandleDeath(killerClientId);
        }
    }

    // RequireOwnership=false: this is called on GameManager's own NetworkObject (in-scene, owned
    // by the server) whenever ANY player slaps someone into a stun, not just the host - the
    // default RequireOwnership would only ever let the server/host's own client succeed here,
    // silently rejecting every non-host client's stun ("client cannot knockout host"). clientId is
    // just the already-validated target of an existing slap interaction, not a trust boundary.
    [ServerRpc(RequireOwnership = false)]
    public void StunPlayerServerRpc(ulong clientId)
    {
        StunPlayerClientRpc(clientId);
    }

    [ClientRpc]
    private void StunPlayerClientRpc(ulong clientId)
    {
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
        {
            var playerObject = client.PlayerObject;
            if (playerObject != null)
            {
                playerObject.GetComponent<Ragdoll>().TriggerRagdoll(isDead: false);

                if (VfxManager.Instance != null)
                {
                    Vector3 headPosition = playerObject.transform.position + VfxManager.Instance.stunVfxHeadOffset;
                    VfxManager.SpawnOneShot(VfxManager.Instance.stunVfxPrefab, headPosition, VfxManager.Instance.stunVfxLifetime);
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
        if (NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject().TryGetComponent<Stats>(out var stats))
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
        if (NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject().TryGetComponent<PauseMenu>(out var pauseMenu))
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

        _pendingTeamUpRequests.Remove(clientId);
        foreach (ulong responderId in new List<ulong>(_pendingTeamUpRequests.Keys))
        {
            if (_pendingTeamUpRequests[responderId] == clientId)
            {
                _pendingTeamUpRequests.Remove(responderId);
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

    public void OnDisable()
    {
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

    [ServerRpc(RequireOwnership = false)]
    public void UpdateKillsServerRpc(ulong shooterId, int killAmount)
    {
        // Only ever legitimately reported as 1 (one kill) per call; reject anything else
        // so a modified client can't grant itself arbitrary kills/coins via this RPC.
        if (killAmount != 1)
        {
            return;
        }

        if (!_playersKills.ContainsKey(shooterId))
        {
            _playersKills[shooterId] = 0;
        }

        _playersKills[shooterId] += killAmount;
    }

    #region TeamUp

    [ServerRpc(RequireOwnership = false)]
    public void TeamUpRequestServerRpc(ulong teamMateId, ServerRpcParams serverRpcParams = default)
    {
        ulong requesterId = serverRpcParams.Receive.SenderClientId;
        _pendingTeamUpRequests[teamMateId] = requesterId;

        var clientRpcParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new List<ulong> { teamMateId }
            }
        };
        SendTeamUpRequestClientRpc(requesterId, clientRpcParams);
    }

    [ClientRpc]
    private void SendTeamUpRequestClientRpc(ulong senderId, ClientRpcParams clientRpcParams = default)
    {
        _ = clientRpcParams;
        if (NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject().TryGetComponent<TeamUp>(out var teamUp))
        {
            if (teamUp.isTeamedUp)
            {
                return;
            }

            teamUp.RequestTeamUp(senderId);
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

    private static void OnWeatherChanged()
    {
        OnWeatherChange?.Invoke();
    }

    private void StartRain()
    {
        if (!_hasRained)
        {
            float percentageChance = Mathf.Pow(1.0155f, Time.timeSinceLevelLoad);
            Debug.Log(percentageChance);

            bool shouldRain = Percentage(percentageChance);
            Debug.Log(shouldRain);

            if (shouldRain)
            {
                _hasRained = true;
                OnWeatherChanged();
            }
        }
    }

    #region Tasks

    private void DistributeTasks()
    {
        foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if (!_playerStates.TryGetValue(clientId, out var isAlive) || !isAlive)
                continue;

            if (_allPlayersTasks.TryGetValue(clientId, out var existingTasks))
            {
                existingTasks.RemoveAll(t => t.completed);
                existingTasks.AddRange(TaskManager.Instance.GenerateTasks());
            }
            else
            {
                _allPlayersTasks[clientId] = TaskManager.Instance.GenerateTasks();
            }
        }
    }

    #endregion

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

        List<ulong> eligibleClientIds = new();

        foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if (clientId == excludedClientId)
            {
                continue;
            }

            if (_playerStates.TryGetValue(clientId, out bool isAlive) && isAlive)
            {
                eligibleClientIds.Add(clientId);
            }
        }

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

        if (reassignGun && playerWithGun.Value == clientId)
        {
            RoundManager.Instance?.EndRound();
            playerWithGun.Value = GetRandomClientId(clientId);
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


[System.Serializable]
public class PlayerTask
{
    public Challenge challenge;
    public bool completed;

    public PlayerTask(Challenge challenge)
    {
        this.challenge = challenge;
        completed = false;
    }
}
