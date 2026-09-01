using System;
using System.Collections;
using System.Collections.Generic;
using Steamworks;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

[RequireComponent(typeof(NetworkObject))]
public class LoadingScreenController : NetworkBehaviour
{
    public struct ProgressEntry : INetworkSerializable, IEquatable<ProgressEntry>
    {
        public ulong ClientId;
        public float Progress;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref Progress);
        }

        public bool Equals(ProgressEntry other) => ClientId == other.ClientId && Progress.Equals(other.Progress);
    }

    // LobbyManager (and its playerInfo name/steamId dictionary) is scene-local and not
    // DontDestroyOnLoad, so it's already gone by the time this scene loads - RefreshAllBars used
    // to look names up there and silently got nothing, leaving every other player's row blank.
    // Each client self-reports its own Steam name here instead, independent of the Lobby scene.
    public struct PlayerNameEntry : INetworkSerializable, IEquatable<PlayerNameEntry>
    {
        public ulong ClientId;
        public ulong SteamId;
        public FixedString64Bytes Name;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref SteamId);
            serializer.SerializeValue(ref Name);
        }

        public bool Equals(PlayerNameEntry other) => ClientId == other.ClientId && SteamId == other.SteamId && Name.Equals(other.Name);
    }

    // One row of the "other players" list. These used to be three parallel dictionaries keyed by
    // client id, which made it easy to add a row to one of them and forget the others, and
    // impossible to remove a leaver's row cleanly. Keeping a player's widgets and interpolation
    // state together means a row is created, updated, and destroyed as a single unit.
    private class PlayerRow
    {
        public GameObject Root;
        public Slider Bar;
        public TextMeshProUGUI Label;
        public RawImage ProfilePic;
        // Target is the last value that arrived over the network; Displayed is what the slider is
        // currently showing and chases Target every frame. See Update.
        public float Target;
        public float Displayed;
    }

    [Header("My Progress")]
    public Slider myProgressBar;
    public TextMeshProUGUI myPercentText;
    public RawImage myProfilePic;

    [Header("Other Players")]
    public Transform otherPlayersContainer;
    public GameObject otherPlayerBarTemplate;

    [Header("Bottom")]
    public TextMeshProUGUI tipText;
    public string[] tips;

    [Header("Progress Pacing")]
    // Loading happens in two phases and the bar gives each one half of its width:
    //   0% -> 50%   waiting for every peer to finish loading this screen. Nothing is actually
    //               loading locally in this phase, so it is paced by a timer - but a FIXED one,
    //               identical on every machine. It used to be Random.Range(minFake, maxFake) rolled
    //               independently on each client, which is precisely why the bars never lined up:
    //               one player's bar raced to the cap in 1.5s while another crawled for 3s, and
    //               neither number had anything to do with the other player's real state.
    //   50% -> 100% the real load of the game scene, driven by AsyncOperation.progress.
    public float phaseOneRampSeconds = 2.5f;
    // Bar units per second. The network only carries ~10 samples a second (see reportsPerSecond),
    // so every bar - local and remote - chases its target at this rate and the motion stays
    // continuous instead of stepping once per packet.
    public float barSmoothingSpeed = 2.5f;
    // Progress used to be reported on every whole-percent change, i.e. up to ~100 ServerRpcs per
    // player across a two second load, each one writing the progress NetworkList and forcing a full
    // RefreshAllBars on every client. With a full lobby that flood is what made remote bars stutter
    // and lag behind their owner. A fixed sample rate plus local interpolation looks smoother and
    // costs an order of magnitude less traffic.
    public float reportsPerSecond = 10f;
    // Safety valve: once at least one player has genuinely finished loading, nobody waits longer
    // than this for the stragglers before the scene is activated for everyone. A client still
    // loading when the grace expires activates as soon as its own load finishes, because
    // sceneActivationAllowed is a latch and not a one-shot signal.
    public float activationGraceSeconds = 15f;

    // Where phase one stops. The timed ramp can never go past this, so a fast machine cannot show a
    // number implying the real load has even started, let alone finished.
    private const float PhaseOneCeiling = 0.5f;
    // Unity parks a load whose activation is held at 0.9, so 0..0.9 is the whole real load curve.
    private const float HeldLoadProgress = 0.9f;
    private const string LoadingSceneName = "LoadingScreen";

    private readonly NetworkList<ProgressEntry> progress = new NetworkList<ProgressEntry>();
    private readonly NetworkList<PlayerNameEntry> playerNames = new NetworkList<PlayerNameEntry>();
    // Latched, rather than the one-shot ActivateSceneClientRpc this replaces. That RPC only had an
    // effect on clients whose pendingOp already existed at the instant it arrived; anyone whose
    // game-scene load event landed a moment later stayed on the loading screen forever with nothing
    // left to re-trigger activation. A NetworkVariable can be read at any time, so a load that
    // shows up late activates itself immediately.
    private readonly NetworkVariable<bool> sceneActivationAllowed = new NetworkVariable<bool>();

    private readonly Dictionary<ulong, PlayerRow> otherRows = new();
    // Scratch list reused by PruneDepartedRows so pruning allocates nothing per call.
    private readonly List<ulong> rowsToRemove = new();
    // SteamIds an avatar fetch has already been kicked off for - GetLargeAvatarAsync is a real
    // network round trip, so this stops RefreshAllBars (called on every progress tick) from
    // re-requesting the same avatar every frame.
    private readonly HashSet<ulong> avatarFetchStarted = new();

    private AsyncOperation pendingOp;
    private Coroutine activationGraceRoutine;
    private float myDisplayedProgress;
    // Local mirror of the go-ahead. Set by either the NetworkVariable or the ClientRpc below,
    // whichever arrives first, and read by OnSceneLoadBegin so a load that only shows up after the
    // signal already passed never gets held.
    private bool activationLatched;

    private void Awake()
    {
        if (tipText != null && tips != null && tips.Length > 0)
        {
            tipText.text = tips[UnityEngine.Random.Range(0, tips.Length)];
        }

        if (otherPlayerBarTemplate != null)
        {
            otherPlayerBarTemplate.SetActive(false);
        }
    }

    public override void OnNetworkSpawn()
    {
        NetworkManager.Singleton.SceneManager.OnLoad += OnSceneLoadBegin;

        if (IsServer)
        {
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += OnLoadingScreenReached;
            // A player joining or dropping mid-load used to leave the progress list wrong in both
            // directions: a joiner never got an entry, so its ReportProgressServerRpc found nothing
            // to write and it never appeared on anyone's screen; a leaver kept a stale entry that
            // nothing would ever complete. Worse, the readiness check only ever ran from a progress
            // report, so if the last player everyone was waiting on dropped, no report was coming
            // and the whole lobby sat on the loading screen indefinitely.
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;

            progress.Clear();
            foreach (ulong id in NetworkManager.Singleton.ConnectedClientsIds)
            {
                progress.Add(new ProgressEntry { ClientId = id, Progress = 0f });
            }
        }

        progress.OnListChanged += OnProgressChanged;
        playerNames.OnListChanged += OnPlayerNamesChanged;
        sceneActivationAllowed.OnValueChanged += OnActivationAllowedChanged;
        RefreshAllBars();

        ReportNameServerRpc(SteamClient.Name, (ulong)SteamClient.SteamId);
        FetchAndApplyAvatar((ulong)SteamClient.SteamId, myProfilePic);

        // Started here, the moment the loading screen itself comes up - not from OnSceneLoadBegin.
        // The game scene's load only begins once EVERY peer has finished loading this screen, so
        // starting the bar there left it frozen at a flat 0% for the whole first phase.
        StartCoroutine(TrackMyProgress());
    }

    public override void OnNetworkDespawn()
    {
        if (NetworkManager.Singleton != null)
        {
            if (NetworkManager.Singleton.SceneManager != null)
            {
                NetworkManager.Singleton.SceneManager.OnLoad -= OnSceneLoadBegin;
                if (IsServer)
                {
                    NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= OnLoadingScreenReached;
                }
            }

            if (IsServer)
            {
                NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            }
        }

        progress.OnListChanged -= OnProgressChanged;
        playerNames.OnListChanged -= OnPlayerNamesChanged;
        sceneActivationAllowed.OnValueChanged -= OnActivationAllowedChanged;
    }

    [ServerRpc(RequireOwnership = false)]
    private void ReportNameServerRpc(string name, ulong steamId, ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;
        for (int i = 0; i < playerNames.Count; i++)
        {
            if (playerNames[i].ClientId == clientId)
            {
                playerNames[i] = new PlayerNameEntry { ClientId = clientId, SteamId = steamId, Name = name };
                return;
            }
        }

        playerNames.Add(new PlayerNameEntry { ClientId = clientId, SteamId = steamId, Name = name });
    }

    private bool TryGetPlayerNameEntry(ulong clientId, out PlayerNameEntry result)
    {
        foreach (PlayerNameEntry entry in playerNames)
        {
            if (entry.ClientId == clientId)
            {
                result = entry;
                return true;
            }
        }

        result = default;
        return false;
    }

    private async void FetchAndApplyAvatar(ulong steamId, RawImage target)
    {
        if (target == null || steamId == 0 || !avatarFetchStarted.Add(steamId))
        {
            return;
        }

        var image = await SteamFriends.GetLargeAvatarAsync(steamId);
        if (!image.HasValue || target == null)
        {
            return;
        }

        target.texture = SteamFriendsManager.GetTextureFromImage(image.Value);
    }

    // Server only: everyone has reached the loading screen itself, now kick off the real load of the game scene.
    private void OnLoadingScreenReached(string sceneName, LoadSceneMode mode, List<ulong> completed, List<ulong> timedOut)
    {
        if (sceneName != LoadingSceneName) return;

        NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= OnLoadingScreenReached;
        NetworkManager.Singleton.SceneManager.LoadScene(GameNetworkManager.Instance.PendingGameSceneName, LoadSceneMode.Single);
    }

    // Every peer: intercept its own load of the game scene and hold it just before activation.
    private void OnSceneLoadBegin(ulong clientId, string sceneName, LoadSceneMode mode, AsyncOperation asyncOperation)
    {
        if (clientId != NetworkManager.Singleton.LocalClientId) return;

        // Matched by exclusion rather than against a replicated copy of the target scene name.
        // This object only exists while the loading screen is up, and the only scene load that can
        // begin in that window is the game scene - so any load that is not this screen's own is it.
        if (asyncOperation == null || sceneName == LoadingSceneName) return;

        pendingOp = asyncOperation;
        // Read the latch instead of blindly holding: if the go-ahead was already given while this
        // load was still being set up, holding it here would strand this client on the loading
        // screen with no second signal ever coming.
        pendingOp.allowSceneActivation = activationLatched || sceneActivationAllowed.Value;
    }

    private void OnActivationAllowedChanged(bool previous, bool current)
    {
        if (current)
        {
            LatchActivation();
        }
    }

    // Sent alongside the NetworkVariable write. The RPC is what actually gets the signal out in
    // time - a NetworkVariable delta waits for the next tick, and the host's own scene swap can
    // despawn this object before that tick happens. The NetworkVariable is still worth keeping as
    // the durable copy for anyone who spawns this object after the signal was already sent.
    [ClientRpc]
    private void ActivateSceneClientRpc()
    {
        LatchActivation();
    }

    private void LatchActivation()
    {
        if (activationLatched) return;

        activationLatched = true;
        StartCoroutine(ActivateNextFrame());
    }

    private IEnumerator ActivateNextFrame()
    {
        // One frame of slack before the host lets its own scene through, so Netcode has flushed the
        // outgoing batch carrying this very signal to everyone else. Activating in the same frame
        // tears this object (and the message with it) down while the other players are still
        // waiting to be told they can go.
        yield return null;

        if (pendingOp != null)
        {
            pendingOp.allowSceneActivation = true;
        }
    }

    // Server only.
    private void OnClientConnected(ulong clientId)
    {
        for (int i = 0; i < progress.Count; i++)
        {
            if (progress[i].ClientId == clientId) return;
        }

        progress.Add(new ProgressEntry { ClientId = clientId, Progress = 0f });
    }

    // Server only.
    private void OnClientDisconnected(ulong clientId)
    {
        for (int i = progress.Count - 1; i >= 0; i--)
        {
            if (progress[i].ClientId == clientId) progress.RemoveAt(i);
        }

        for (int i = playerNames.Count - 1; i >= 0; i--)
        {
            if (playerNames[i].ClientId == clientId) playerNames.RemoveAt(i);
        }

        // The player everyone was still waiting on may be the one that just left.
        CheckEveryoneReady();
    }

    // Drives the local bar and this client's progress reports. The displayed value chases a target
    // that is honest about which phase the load is in (see the Progress Pacing header), and 100% is
    // only ever reached once pendingOp shows the real load finished - the timed ramp is capped well
    // below it and cannot claim completion on its own.
    private IEnumerator TrackMyProgress()
    {
        float elapsed = 0f;
        float lastReported = -1f;
        float nextReportTime = 0f;

        while (true)
        {
            elapsed += Time.unscaledDeltaTime;
            float target = CalculateTargetProgress(elapsed);

            myDisplayedProgress = Mathf.MoveTowards(myDisplayedProgress, target, barSmoothingSpeed * Time.unscaledDeltaTime);
            SetMyProgress(myDisplayedProgress);

            if (myDisplayedProgress >= 1f)
            {
                ReportProgressServerRpc(1f);
                yield break;
            }

            if (Time.unscaledTime >= nextReportTime && !Mathf.Approximately(myDisplayedProgress, lastReported))
            {
                lastReported = myDisplayedProgress;
                nextReportTime = Time.unscaledTime + 1f / Mathf.Max(reportsPerSecond, 1f);
                ReportProgressServerRpc(myDisplayedProgress);
            }

            yield return null;
        }
    }

    private float CalculateTargetProgress(float elapsed)
    {
        // Phase one: no local work is happening yet, so pace it on a clock that ticks at the same
        // rate on every machine.
        if (pendingOp == null)
        {
            return Mathf.Min(PhaseOneCeiling, PhaseOneCeiling * elapsed / Mathf.Max(phaseOneRampSeconds, 0.01f));
        }

        // Phase two: the real load. progress >= 0.9 means Unity has finished and is only waiting on
        // the activation this screen is holding back.
        if (pendingOp.progress >= HeldLoadProgress)
        {
            return 1f;
        }

        return Mathf.Lerp(PhaseOneCeiling, 0.99f, Mathf.Clamp01(pendingOp.progress / HeldLoadProgress));
    }

    private void SetMyProgress(float value)
    {
        if (myProgressBar != null)
        {
            myProgressBar.value = value;
        }

        // No separate name field on the "my progress" row - fold it into the existing percent
        // text instead of adding new UI.
        if (myPercentText != null)
        {
            myPercentText.text = $"{SteamClient.Name} - {Mathf.RoundToInt(value * 100)}%";
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void ReportProgressServerRpc(float value, ServerRpcParams rpcParams = default)
    {
        // The sender comes from the transport, never from the caller, so a client can only ever
        // move its own bar.
        ulong clientId = rpcParams.Receive.SenderClientId;
        float clamped = Mathf.Clamp01(value);

        for (int i = 0; i < progress.Count; i++)
        {
            if (progress[i].ClientId != clientId) continue;

            // Progress only ever moves forward. Reports are sampled off a smoothed local value and
            // can arrive out of order, and a bar that flicks backwards reads as "desynced" even
            // when the underlying load is perfectly fine.
            if (clamped > progress[i].Progress)
            {
                progress[i] = new ProgressEntry { ClientId = clientId, Progress = clamped };
            }

            CheckEveryoneReady();
            return;
        }

        // No entry yet (a client that arrived after this object spawned): create one instead of
        // dropping the report on the floor.
        progress.Add(new ProgressEntry { ClientId = clientId, Progress = clamped });
        CheckEveryoneReady();
    }

    // Server only: don't activate the game scene for anyone until every connected player's real
    // load has actually reached 100%.
    private void CheckEveryoneReady()
    {
        if (!IsServer || sceneActivationAllowed.Value) return;

        bool allReady = true;
        bool anyReady = false;
        bool anyConnected = false;

        foreach (ProgressEntry entry in progress)
        {
            // A client that dropped between reaching this screen and finishing the load would
            // otherwise never report 100%, and everyone still here would sit on a held scene
            // waiting for it forever.
            if (!NetworkManager.Singleton.ConnectedClients.ContainsKey(entry.ClientId)) continue;

            anyConnected = true;
            if (entry.Progress >= 1f)
            {
                anyReady = true;
            }
            else
            {
                allReady = false;
            }
        }

        if (anyConnected && allReady)
        {
            AllowActivation();
            return;
        }

        if (anyReady && activationGraceRoutine == null)
        {
            activationGraceRoutine = StartCoroutine(ActivationGrace());
        }
    }

    private IEnumerator ActivationGrace()
    {
        yield return new WaitForSecondsRealtime(activationGraceSeconds);

        if (!sceneActivationAllowed.Value)
        {
            Debug.LogWarning($"LoadingScreenController: still waiting on a player after {activationGraceSeconds}s, activating the scene anyway.");
            AllowActivation();
        }

        activationGraceRoutine = null;
    }

    // Server only. Both signals go out together - see ActivateSceneClientRpc for why neither one
    // alone is enough.
    private void AllowActivation()
    {
        sceneActivationAllowed.Value = true;
        ActivateSceneClientRpc();
    }

    private void OnProgressChanged(NetworkListEvent<ProgressEntry> change)
    {
        RefreshAllBars();
    }

    private void OnPlayerNamesChanged(NetworkListEvent<PlayerNameEntry> change)
    {
        RefreshAllBars();
    }

    private void Update()
    {
        // Remote bars advance here rather than in RefreshAllBars so they keep moving between the
        // ~10 progress samples a second that actually cross the network.
        foreach (KeyValuePair<ulong, PlayerRow> pair in otherRows)
        {
            PlayerRow row = pair.Value;
            row.Displayed = Mathf.MoveTowards(row.Displayed, row.Target, barSmoothingSpeed * Time.unscaledDeltaTime);
            if (row.Bar != null)
            {
                row.Bar.value = row.Displayed;
            }
        }
    }

    private void RefreshAllBars()
    {
        if (NetworkManager.Singleton == null) return;
        ulong myId = NetworkManager.Singleton.LocalClientId;

        foreach (ProgressEntry entry in progress)
        {
            if (entry.ClientId == myId) continue;

            if (!otherRows.TryGetValue(entry.ClientId, out PlayerRow row))
            {
                if (otherPlayerBarTemplate == null || otherPlayersContainer == null) continue;

                GameObject instance = Instantiate(otherPlayerBarTemplate, otherPlayersContainer);
                instance.SetActive(true);
                row = new PlayerRow
                {
                    Root = instance,
                    Bar = instance.GetComponentInChildren<Slider>(),
                    Label = instance.GetComponentInChildren<TextMeshProUGUI>(),
                    ProfilePic = instance.GetComponentInChildren<RawImage>()
                };
                otherRows[entry.ClientId] = row;
            }

            row.Target = entry.Progress;

            // Refreshed every call (not just at row creation): this client's own name/steamId
            // report can arrive after its progress row already exists.
            if (TryGetPlayerNameEntry(entry.ClientId, out PlayerNameEntry nameEntry))
            {
                if (row.Label != null)
                {
                    string name = nameEntry.Name.ToString();
                    if (!string.IsNullOrEmpty(name))
                    {
                        row.Label.text = name;
                    }
                }

                if (row.ProfilePic != null)
                {
                    FetchAndApplyAvatar(nameEntry.SteamId, row.ProfilePic);
                }
            }
        }

        PruneDepartedRows(myId);
    }

    // Rows used to outlive their player: someone who dropped mid-load left a frozen, half-filled
    // bar on everyone else's screen for the rest of the loading screen.
    private void PruneDepartedRows(ulong myId)
    {
        rowsToRemove.Clear();

        foreach (KeyValuePair<ulong, PlayerRow> pair in otherRows)
        {
            bool stillListed = false;
            foreach (ProgressEntry entry in progress)
            {
                if (entry.ClientId == pair.Key && entry.ClientId != myId)
                {
                    stillListed = true;
                    break;
                }
            }

            if (!stillListed)
            {
                rowsToRemove.Add(pair.Key);
            }
        }

        foreach (ulong clientId in rowsToRemove)
        {
            if (otherRows.TryGetValue(clientId, out PlayerRow row) && row.Root != null)
            {
                Destroy(row.Root);
            }

            otherRows.Remove(clientId);
        }
    }
}
