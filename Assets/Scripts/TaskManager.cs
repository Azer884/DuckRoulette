using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Server-authoritative owner of player tasks.
//
// Tasks are a punishment for camping, not a chore list for everyone:
//   - The server watches every alive player who is not holding the gun. A player who stays
//     inside a small radius (small shuffles still count as standing still), or sits in a hiding
//     spot, for half the round timer is handed exactly ONE task.
//   - If three or more players are idle past that mark at the same time, they are handed the
//     SAME ThreePlus task instead and have to do it together.
//   - An open task keeps its holder off the gun at every hand-off, and it carries over from
//     round to round until it is done. It is only swapped for a different one when a group task
//     loses members (death, disconnect) or when it has stayed open for roundsBeforeSwap rounds.
//   - Two players never hold the same open Useful/Useless task, because the world objects are
//     shared now: once someone lights the campfire, everyone sees it lit.
//
// Lives on Assets/Prefabs/Player.prefab beside GameManager and uses the same first-instance-wins
// singleton guard, so exactly one copy is live per session.
public class TaskManager : NetworkBehaviour
{
    public static TaskManager Instance { get; private set; }

    // One assigned task. Only the index into `tasks` travels - the Challenge asset itself is
    // authored content every client already has.
    public struct TaskEntry : INetworkSerializable, IEquatable<TaskEntry>
    {
        public ulong ClientId;
        public int TaskIndex;
        public bool Completed;
        // 0 for a solo task. Every member of a shared ThreePlus task carries the same id.
        public int GroupId;
        // Gun hand-offs this task has survived without being finished.
        public int RoundsOpen;

        public bool IsGroup => GroupId != 0;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref TaskIndex);
            serializer.SerializeValue(ref Completed);
            serializer.SerializeValue(ref GroupId);
            serializer.SerializeValue(ref RoundsOpen);
        }

        public bool Equals(TaskEntry other) =>
            ClientId == other.ClientId && TaskIndex == other.TaskIndex && Completed == other.Completed &&
            GroupId == other.GroupId && RoundsOpen == other.RoundsOpen;
    }

    [Tooltip("Every task that can be handed out. Add a new Challenge asset here to put it in " +
        "rotation; a task with no live objective in the level is never dealt, so keep the two in step.")]
    public Challenge[] tasks;

    [Header("Idle detection")]
    [SerializeField, Tooltip("A player who stays within this many metres of where they settled " +
        "counts as standing still. Small shuffles, turning around and crouching all stay inside it.")]
    private float stationaryRadius = 4f;

    [SerializeField, Range(0.1f, 1f), Tooltip("How much of the round timer a player has to spend " +
        "standing still or hidden before they are handed a task. 0.5 = half the round.")]
    private float idleFractionOfRound = 0.5f;

    [SerializeField, Tooltip("How often the server re-checks everyone, in seconds.")]
    private float evaluateInterval = 0.5f;

    [Header("Groups and carry-over")]
    [SerializeField, Tooltip("Idle players needed at the same time for a shared ThreePlus task.")]
    private int minGroupSize = 3;

    [SerializeField, Tooltip("A task still open after this many gun hand-offs is swapped for a " +
        "different one.")]
    private int roundsBeforeSwap = 2;

    [SerializeField, Tooltip("The \"try to team up\" task. It has no world object - sending any " +
        "valid team-up request completes it - so it is always in rotation while more than two " +
        "players are alive.")]
    private Challenge teamUpTask;

    public Challenge TeamUpTask => teamUpTask;

    // Everyone's tasks, not just the local player's. A client filters to its own for the HUD.
    private readonly NetworkList<TaskEntry> assignedTasks = new();

    // Task indices whose world object is in its finished state right now (the lit campfire).
    // Shared, so everyone sees the same world. A task leaves this list when it is handed out
    // again, which is what puts the campfire back out for the next person who has to light it.
    private readonly NetworkList<int> doneInWorld = new();

    /// <summary>Raised locally whenever the replicated task state changes, so UI doesn't poll.</summary>
    public event Action OnTasksChanged;

    /// <summary>Server only. Raised when a task is handed out, so its world object can reset
    /// itself (the truck engine goes back to where it was found, the push-truck goal is rolled).</summary>
    public static event Action<Challenge> ServerTaskAssigned;

    private readonly Dictionary<Challenge, int> taskIndices = new();

    // Tasks that have something in the loaded level to complete them, ref-counted because a map
    // can hold two boomboxes. Static and registered by the objectives themselves, because a scene
    // prop wakes up long before the player prefab carrying this singleton exists.
    private static readonly Dictionary<Challenge, int> registeredObjectives = new();

    private struct IdleState
    {
        public Vector3 Anchor;
        public float Seconds;
        public bool HasAnchor;
    }

    private readonly Dictionary<ulong, IdleState> idle = new();
    private readonly List<ulong> scratchIds = new();
    private readonly List<ulong> scratchCandidates = new();
    private readonly List<int> scratchPool = new();
    private float evaluateTimer;
    private int nextGroupId = 1;

    public static void RegisterObjective(Challenge task)
    {
        if (task == null)
        {
            return;
        }

        registeredObjectives.TryGetValue(task, out int count);
        registeredObjectives[task] = count + 1;
    }

    public static void UnregisterObjective(Challenge task)
    {
        if (task == null || !registeredObjectives.TryGetValue(task, out int count))
        {
            return;
        }

        if (count <= 1)
        {
            registeredObjectives.Remove(task);
        }
        else
        {
            registeredObjectives[task] = count - 1;
        }
    }

    public static bool IsObjectiveRegistered(Challenge task) =>
        task != null && registeredObjectives.ContainsKey(task);

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        RebuildTaskIndices();
    }

    public override void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        base.OnDestroy();
    }

    private void RebuildTaskIndices()
    {
        taskIndices.Clear();
        if (tasks == null)
        {
            return;
        }

        for (int i = 0; i < tasks.Length; i++)
        {
            if (tasks[i] != null)
            {
                taskIndices[tasks[i]] = i;
            }
        }
    }

    public override void OnNetworkSpawn()
    {
        assignedTasks.OnListChanged += OnAssignedTasksChanged;
        doneInWorld.OnListChanged += OnDoneInWorldChanged;
        OnTasksChanged?.Invoke();
    }

    public override void OnNetworkDespawn()
    {
        assignedTasks.OnListChanged -= OnAssignedTasksChanged;
        doneInWorld.OnListChanged -= OnDoneInWorldChanged;
    }

    private void OnAssignedTasksChanged(NetworkListEvent<TaskEntry> change) => OnTasksChanged?.Invoke();

    private void OnDoneInWorldChanged(NetworkListEvent<int> change) => OnTasksChanged?.Invoke();

    #region Server: idle detection

    private void Update()
    {
        if (!IsServer || !IsSpawned)
        {
            return;
        }

        GameManager game = GameManager.Instance;
        if (game == null || game.IsGameEnded || NetworkManager.Singleton == null)
        {
            return;
        }

        float dt = Time.deltaTime;
        TrackIdlePlayers(game, dt);

        evaluateTimer += dt;
        if (evaluateTimer < evaluateInterval)
        {
            return;
        }

        evaluateTimer = 0f;
        AssignIdleTasks(game);
    }

    private void TrackIdlePlayers(GameManager game, float dt)
    {
        foreach (KeyValuePair<ulong, NetworkClient> pair in NetworkManager.Singleton.ConnectedClients)
        {
            ulong clientId = pair.Key;
            NetworkObject player = pair.Value.PlayerObject;

            // The gun holder is busy - standing still with the gun is the whole game, not camping.
            if (player == null || !game.IsPlayerAlive(clientId) || game.playerWithGun.Value == clientId)
            {
                idle.Remove(clientId);
                continue;
            }

            Vector3 position = player.transform.position;
            idle.TryGetValue(clientId, out IdleState state);

            bool hidden = HidingSpot.IsClientHiding(clientId);
            Vector3 flat = position - state.Anchor;
            flat.y = 0f;

            if (hidden || (state.HasAnchor && flat.sqrMagnitude <= stationaryRadius * stationaryRadius))
            {
                state.Seconds += dt;
            }
            else
            {
                state.Anchor = position;
                state.HasAnchor = true;
                state.Seconds = 0f;
            }

            idle[clientId] = state;
        }
    }

    private float IdleThreshold =>
        (RoundManager.Instance != null ? RoundManager.Instance.RoundDuration : 30f) * idleFractionOfRound;

    private void AssignIdleTasks(GameManager game)
    {
        float threshold = IdleThreshold;

        // Everyone idle past the mark whose current task (if any) is a solo one. A solo task is
        // upgraded into a shared one when enough players camp at once.
        scratchCandidates.Clear();
        foreach (KeyValuePair<ulong, IdleState> pair in idle)
        {
            if (pair.Value.Seconds >= threshold && !HasOpenGroupTask(pair.Key))
            {
                scratchCandidates.Add(pair.Key);
            }
        }

        if (scratchCandidates.Count == 0)
        {
            return;
        }

        // Copied: handing a task out raises ServerTaskAssigned, whose handlers may cancel tasks,
        // and that reuses the scratch lists.
        ulong[] candidates = scratchCandidates.ToArray();

        if (candidates.Length >= minGroupSize && game.AlivePlayersCount() >= minGroupSize)
        {
            int groupTask = PickGroupTask();
            if (groupTask >= 0)
            {
                int groupId = nextGroupId++;
                foreach (ulong clientId in candidates)
                {
                    RemoveEntriesFor(clientId);
                    AddEntry(clientId, groupTask, groupId);
                }

                OnTaskHandedOut(groupTask, candidates, true);
                return;
            }
        }

        foreach (ulong clientId in candidates)
        {
            if (HasOpenTask(clientId))
            {
                continue;
            }

            int solo = PickSoloTask(clientId, -1);
            if (solo < 0)
            {
                continue;
            }

            RemoveEntriesFor(clientId);
            AddEntry(clientId, solo, 0);
            OnTaskHandedOut(solo, new[] { clientId }, false);
        }
    }

    #endregion

    #region Server: picking

    // A solo task nobody else is holding open right now. Two players can't share one: the world
    // object is shared, so the first to finish it would finish it for both.
    private int PickSoloTask(ulong clientId, int excludedIndex)
    {
        scratchPool.Clear();
        if (tasks == null)
        {
            return -1;
        }

        GameManager game = GameManager.Instance;
        for (int i = 0; i < tasks.Length; i++)
        {
            Challenge task = tasks[i];
            if (task == null || i == excludedIndex || task.taskType == Challenge.TaskType.ThreePlus || IsTaskOpenForAnyone(i))
            {
                continue;
            }

            if (task == teamUpTask)
            {
                // Pointless with two players left (the last one standing wins), and impossible for
                // someone who is already on a team.
                if (game == null || game.AlivePlayersCount() <= 2 || game.IsInAnyTeam(clientId))
                {
                    continue;
                }
            }
            else if (!registeredObjectives.ContainsKey(task))
            {
                continue;
            }

            scratchPool.Add(i);
        }

        return scratchPool.Count == 0 ? -1 : scratchPool[UnityEngine.Random.Range(0, scratchPool.Count)];
    }

    private int PickGroupTask()
    {
        scratchPool.Clear();
        if (tasks == null)
        {
            return -1;
        }

        for (int i = 0; i < tasks.Length; i++)
        {
            Challenge task = tasks[i];
            if (task != null && task.taskType == Challenge.TaskType.ThreePlus &&
                registeredObjectives.ContainsKey(task) && !IsTaskOpenForAnyone(i))
            {
                scratchPool.Add(i);
            }
        }

        return scratchPool.Count == 0 ? -1 : scratchPool[UnityEngine.Random.Range(0, scratchPool.Count)];
    }

    #endregion

    #region Server: round flow

    /// <summary>Server only. Called by GameManager at every gun hand-off, after the new holder
    /// was picked (so an open task has already cost its holder this hand-off). Clears finished
    /// tasks, ages open ones, and swaps the ones that can no longer or will no longer be done.</summary>
    public void OnGunHandedOff()
    {
        if (!IsServer)
        {
            return;
        }

        for (int i = assignedTasks.Count - 1; i >= 0; i--)
        {
            TaskEntry entry = assignedTasks[i];
            if (entry.Completed)
            {
                assignedTasks.RemoveAt(i);
                continue;
            }

            entry.RoundsOpen++;
            assignedTasks[i] = entry;
        }

        DissolveUndersizedGroups();

        scratchIds.Clear();
        foreach (TaskEntry entry in assignedTasks)
        {
            if (!entry.Completed && entry.RoundsOpen >= roundsBeforeSwap && !scratchIds.Contains(entry.ClientId))
            {
                scratchIds.Add(entry.ClientId);
            }
        }

        // Copy: SwapToSoloTask reuses scratchIds for its own notification.
        ulong[] stale = scratchIds.ToArray();
        foreach (ulong clientId in stale)
        {
            SwapToSoloTask(clientId);
        }
    }

    /// <summary>Server only. A player died or left: their tasks go, and a group they were in is
    /// broken up if it is now too small to do its task.</summary>
    public void OnPlayerRemoved(ulong clientId)
    {
        if (!IsServer)
        {
            return;
        }

        idle.Remove(clientId);
        RemoveEntriesFor(clientId);
        DissolveUndersizedGroups();
    }

    private void DissolveUndersizedGroups()
    {
        Dictionary<int, int> sizes = new();
        foreach (TaskEntry entry in assignedTasks)
        {
            if (entry.IsGroup && !entry.Completed)
            {
                sizes.TryGetValue(entry.GroupId, out int size);
                sizes[entry.GroupId] = size + 1;
            }
        }

        scratchIds.Clear();
        foreach (TaskEntry entry in assignedTasks)
        {
            if (entry.IsGroup && !entry.Completed && sizes[entry.GroupId] < minGroupSize)
            {
                scratchIds.Add(entry.ClientId);
            }
        }

        ulong[] stranded = scratchIds.ToArray();
        foreach (ulong clientId in stranded)
        {
            SwapToSoloTask(clientId);
        }
    }

    // Replaces a player's open task with a different solo one. With nothing else available the
    // task is dropped rather than kept: a task nobody can finish must never lock anyone out.
    private void SwapToSoloTask(ulong clientId)
    {
        int current = -1;
        foreach (TaskEntry entry in assignedTasks)
        {
            if (entry.ClientId == clientId && !entry.Completed)
            {
                current = entry.TaskIndex;
            }
        }

        RemoveEntriesFor(clientId);

        int next = PickSoloTask(clientId, current);
        if (next < 0)
        {
            return;
        }

        AddEntry(clientId, next, 0);
        OnTaskHandedOut(next, new[] { clientId }, false);
    }

    #endregion

    #region Server: list helpers

    private void AddEntry(ulong clientId, int taskIndex, int groupId)
    {
        assignedTasks.Add(new TaskEntry { ClientId = clientId, TaskIndex = taskIndex, GroupId = groupId });
    }

    private void RemoveEntriesFor(ulong clientId)
    {
        for (int i = assignedTasks.Count - 1; i >= 0; i--)
        {
            if (assignedTasks[i].ClientId == clientId)
            {
                assignedTasks.RemoveAt(i);
            }
        }
    }

    private bool HasOpenTask(ulong clientId)
    {
        foreach (TaskEntry entry in assignedTasks)
        {
            if (entry.ClientId == clientId && !entry.Completed)
            {
                return true;
            }
        }

        return false;
    }

    private bool HasOpenGroupTask(ulong clientId)
    {
        foreach (TaskEntry entry in assignedTasks)
        {
            if (entry.ClientId == clientId && !entry.Completed && entry.IsGroup)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsTaskOpenForAnyone(int taskIndex)
    {
        foreach (TaskEntry entry in assignedTasks)
        {
            if (entry.TaskIndex == taskIndex && !entry.Completed)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True while any player still has this task open - lets a world object (the
    /// push-truck goal) know whether it still matters.</summary>
    public bool IsTaskOpenForAnyone(Challenge task) =>
        task != null && taskIndices.TryGetValue(task, out int index) && IsTaskOpenForAnyone(index);

    /// <summary>Server only. Whether this specific player has this task open.</summary>
    public bool IsTaskOpenFor(ulong clientId, Challenge task)
    {
        if (task == null || !taskIndices.TryGetValue(task, out int taskIndex))
        {
            return false;
        }

        foreach (TaskEntry entry in assignedTasks)
        {
            if (entry.ClientId == clientId && entry.TaskIndex == taskIndex && !entry.Completed)
            {
                return true;
            }
        }

        return false;
    }

    // The world object goes back to its unfinished state, and the new holders are told.
    private void OnTaskHandedOut(int taskIndex, ulong[] recipients, bool isGroup)
    {
        doneInWorld.Remove(taskIndex);

        Challenge task = GetTask(taskIndex);
        if (task != null)
        {
            ServerTaskAssigned?.Invoke(task);
        }

        TaskAssignedClientRpc(taskIndex, isGroup ? recipients.Length : 1, new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = recipients }
        });
    }

    [ClientRpc]
    private void TaskAssignedClientRpc(int taskIndex, int groupSize, ClientRpcParams rpcParams = default)
    {
        Challenge task = GetTask(taskIndex);
        string name = task != null ? task.DisplayName : "a task";
        string message = groupSize > 1
            ? $"Group task with {groupSize - 1} other players: {name}. Finish it or you won't get the gun next round!"
            : $"You've been camping! Task: {name}. Finish it or you won't get the gun next round!";
        MessageBox.Informate(message, new Color(1f, 0.75f, 0.2f), MessagePriority.High);
    }

    #endregion

    #region Completion

    /// <summary>Called on the interacting client by a world objective. Turns the authored asset
    /// into the index the server understands and asks the server to mark it done.</summary>
    public void ReportTaskCompleted(Challenge task)
    {
        if (task == null || !IsSpawned)
        {
            return;
        }

        if (!taskIndices.TryGetValue(task, out int taskIndex))
        {
            Debug.LogWarning($"TaskManager: '{task.name}' is not in the tasks array, so it can never be assigned or completed.");
            return;
        }

        CompleteTaskServerRpc(taskIndex);
    }

    // RequireOwnership=false: this object is owned by whichever player object hosts the singleton.
    // The client id comes from the transport, so a client can only complete its own task.
    [ServerRpc(RequireOwnership = false)]
    private void CompleteTaskServerRpc(int taskIndex, ServerRpcParams rpcParams = default)
    {
        CompleteEntry(rpcParams.Receive.SenderClientId, taskIndex);
    }

    /// <summary>Server only. Marks a task complete for a player without them pressing anything
    /// themselves - for a shared outcome the server resolves (blackjack round, truck at the river,
    /// a team-up request).</summary>
    public void CompleteTaskForPlayer(ulong clientId, Challenge task)
    {
        if (!IsServer || task == null || !taskIndices.TryGetValue(task, out int taskIndex))
        {
            return;
        }

        CompleteEntry(clientId, taskIndex);
    }

    /// <summary>Server only. Completes this task for every player who has it open - the push
    /// truck reaching the river finishes it for the whole group at once.</summary>
    public void CompleteTaskForAllHolders(Challenge task)
    {
        if (!IsServer || task == null || !taskIndices.TryGetValue(task, out int taskIndex))
        {
            return;
        }

        scratchIds.Clear();
        foreach (TaskEntry entry in assignedTasks)
        {
            if (entry.TaskIndex == taskIndex && !entry.Completed)
            {
                scratchIds.Add(entry.ClientId);
            }
        }

        ulong[] holders = scratchIds.ToArray();
        foreach (ulong clientId in holders)
        {
            CompleteEntry(clientId, taskIndex);
        }
    }

    private void CompleteEntry(ulong clientId, int taskIndex)
    {
        for (int i = 0; i < assignedTasks.Count; i++)
        {
            TaskEntry entry = assignedTasks[i];
            if (entry.ClientId != clientId || entry.TaskIndex != taskIndex || entry.Completed)
            {
                continue;
            }

            entry.Completed = true;
            assignedTasks[i] = entry;

            if (!doneInWorld.Contains(taskIndex))
            {
                doneInWorld.Add(taskIndex);
            }

            // They just moved to do it; the camping clock starts over.
            idle.Remove(clientId);
            NotifyTaskCompleted(clientId);
            return;
        }
    }

    public static void CancelTaskEverywhere(Challenge task)
    {
        if (Instance != null)
        {
            Instance.CancelTask(task);
        }
    }

    /// <summary>Server only. The thing that completes this task stopped existing (the rain put
    /// the campfire out). Every open copy is swapped for a different task, so nobody is left
    /// holding something they cannot finish.</summary>
    public void CancelTask(Challenge task)
    {
        if (!IsServer || task == null || !taskIndices.TryGetValue(task, out int taskIndex))
        {
            return;
        }

        List<ulong> holders = new();
        foreach (TaskEntry entry in assignedTasks)
        {
            if (entry.TaskIndex == taskIndex && !entry.Completed)
            {
                holders.Add(entry.ClientId);
            }
        }

        foreach (ulong clientId in holders)
        {
            SwapToSoloTask(clientId);
        }
    }

    // Only the player who finished it gets the cue.
    private void NotifyTaskCompleted(ulong clientId)
    {
        TaskCompletedFeedbackClientRpc(new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
        });
    }

    [ClientRpc]
    private void TaskCompletedFeedbackClientRpc(ClientRpcParams rpcParams = default)
    {
        if (SFXManager.Instance != null)
        {
            SFXManager.Instance.PlayUI(SFXManager.Instance.taskCompleteClip);
        }

        if (VfxManager.Instance == null || NetworkManager.Singleton == null)
        {
            return;
        }

        NetworkObject localPlayer = NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject();
        if (localPlayer != null)
        {
            VfxManager.SpawnOneShot(
                VfxManager.Instance.taskCompleteVfxPrefab,
                localPlayer.transform.position,
                VfxManager.Instance.taskCompleteVfxLifetime);
        }
    }

    /// <summary>Server only. True when this player has nothing outstanding - also true when they
    /// were never given anything.</summary>
    public bool HasCompletedAllTasks(ulong clientId)
    {
        foreach (TaskEntry entry in assignedTasks)
        {
            if (entry.ClientId == clientId && !entry.Completed)
            {
                return false;
            }
        }

        return true;
    }

    #endregion

    #region Local queries (UI)

    public bool IsTaskOpenForLocalPlayer(Challenge task)
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening &&
               IsTaskOpenForClient(NetworkManager.Singleton.LocalClientId, task);
    }

    private bool IsTaskOpenForClient(ulong clientId, Challenge task)
    {
        if (task == null || !taskIndices.TryGetValue(task, out int taskIndex))
        {
            return false;
        }

        foreach (TaskEntry entry in assignedTasks)
        {
            if (entry.ClientId == clientId && entry.TaskIndex == taskIndex && !entry.Completed)
            {
                return true;
            }
        }

        return false;
    }

    public bool IsTaskCompletedByLocalPlayer(Challenge task)
    {
        if (task == null || NetworkManager.Singleton == null || !taskIndices.TryGetValue(task, out int taskIndex))
        {
            return false;
        }

        ulong localId = NetworkManager.Singleton.LocalClientId;
        foreach (TaskEntry entry in assignedTasks)
        {
            if (entry.ClientId == localId && entry.TaskIndex == taskIndex && entry.Completed)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True while this task's world object should show its finished state for everyone
    /// (the lit campfire).</summary>
    public bool IsTaskDoneInWorld(Challenge task)
    {
        return task != null && taskIndices.TryGetValue(task, out int taskIndex) && doneInWorld.Contains(taskIndex);
    }

    public void GetLocalPlayerTasks(List<TaskEntry> results)
    {
        results.Clear();
        if (NetworkManager.Singleton == null)
        {
            return;
        }

        ulong localId = NetworkManager.Singleton.LocalClientId;
        foreach (TaskEntry entry in assignedTasks)
        {
            if (entry.ClientId == localId)
            {
                results.Add(entry);
            }
        }
    }

    /// <summary>How many players share this group task, including the local player.</summary>
    public int GetGroupSize(int groupId)
    {
        if (groupId == 0)
        {
            return 1;
        }

        int size = 0;
        foreach (TaskEntry entry in assignedTasks)
        {
            if (entry.GroupId == groupId)
            {
                size++;
            }
        }

        return size;
    }

    public Challenge GetTask(int taskIndex)
    {
        if (tasks == null || taskIndex < 0 || taskIndex >= tasks.Length)
        {
            return null;
        }

        return tasks[taskIndex];
    }

    #endregion
}
