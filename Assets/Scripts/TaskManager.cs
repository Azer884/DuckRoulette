using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Server-authoritative owner of every player's per-round tasks.
//
// Previously this only had GenerateTasks(): GameManager kept the results in a private dictionary
// that was never replicated, no UI ever read it, nothing could set PlayerTask.completed, and the
// result had no effect on the match. The whole feature was inert. It now:
//   - hands every alive player a fresh set of tasks at each gun hand-off,
//   - replicates them so each client can draw its own list (TaskListHUD),
//   - lets a world object mark one complete through a sender-validated ServerRpc (TaskObjective),
//   - and answers HasCompletedAllTasks, which GameManager uses to decide who is allowed the gun.
//
// Lives on Assets/Prefabs/Player.prefab beside GameManager and uses the same first-instance-wins
// singleton guard, so exactly one copy is live per session.
public class TaskManager : NetworkBehaviour
{
    public static TaskManager Instance { get; private set; }

    // One assigned task. Only the index into `tasks` travels - the Challenge asset itself is
    // authored content every client already has, so sending anything more would be redundant.
    public struct TaskEntry : INetworkSerializable, IEquatable<TaskEntry>
    {
        public ulong ClientId;
        public int TaskIndex;
        public bool Completed;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref TaskIndex);
            serializer.SerializeValue(ref Completed);
        }

        public bool Equals(TaskEntry other) =>
            ClientId == other.ClientId && TaskIndex == other.TaskIndex && Completed == other.Completed;
    }

    [Tooltip("Every task that can be handed out. Add a new Challenge asset here to put it in " +
        "rotation; a task with no TaskObjective in the level can never be completed, so keep the " +
        "two in step.")]
    public Challenge[] tasks;

    [SerializeField, Tooltip("How many tasks each alive player gets per round.")]
    private int tasksPerRound = 3;

    // Everyone's tasks, not just the local player's. A client filters to its own for the HUD.
    // Other players' task lists are not secret information in this game - knowing that someone
    // else has to light the campfire gives no advantage - so this stays one flat list rather
    // than a per-client channel that would need its own late-join resync.
    private readonly NetworkList<TaskEntry> assignedTasks = new();

    /// <summary>Raised locally whenever the replicated task list changes, so UI doesn't poll.</summary>
    public event Action OnTasksChanged;

    // Challenge asset -> its index in `tasks`, so TaskObjective can hand us the asset it was
    // authored with and we can turn it into the index the network actually carries.
    private readonly Dictionary<Challenge, int> taskIndices = new();

    // Tasks that actually have something in the loaded level to complete them. Static and
    // registered by the objectives themselves, because a scene prop wakes up long before the
    // player prefab that carries this singleton exists, so there is nothing to register into yet.
    //
    // Without this, a task with no objective in the map (a mailbox task on a map with no mailbox)
    // would still be dealt, could never be ticked off, and would keep its holder off the gun
    // forever. Adding a TaskObjective to a prop is therefore the whole opt-in: no objective, no
    // rotation.
    private static readonly HashSet<Challenge> registeredObjectives = new();

    private static bool _warnedAboutShortDeal;

    /// <summary>Called by a live objective (TaskObjective, or BumBox for the boombox) to put its
    /// task into rotation while it exists.</summary>
    public static void RegisterObjective(Challenge task)
    {
        if (task != null)
        {
            registeredObjectives.Add(task);
        }
    }

    /// <summary>Counterpart to <see cref="RegisterObjective"/> - called when the objective goes
    /// away (scene unload, object disabled).</summary>
    public static void UnregisterObjective(Challenge task)
    {
        if (task != null)
        {
            registeredObjectives.Remove(task);
        }
    }

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
        OnTasksChanged?.Invoke();
    }

    public override void OnNetworkDespawn()
    {
        assignedTasks.OnListChanged -= OnAssignedTasksChanged;
    }

    private void OnAssignedTasksChanged(NetworkListEvent<TaskEntry> change)
    {
        OnTasksChanged?.Invoke();
    }

    #region Server: assignment

    /// <summary>Server only. Clears every listed player's tasks and deals them a fresh set for the
    /// round that is starting. Called from GameManager at each gun hand-off.</summary>
    /// <remarks>The old set is replaced outright rather than keeping unfinished tasks around. Gun
    /// eligibility is read at the hand-off, which happens before this runs, so a player who
    /// skipped last round's tasks has already paid for it by the time they get a clean slate -
    /// and carrying the misses forward would let an unfinished list grow without bound.</remarks>
    public void DistributeTasks(IReadOnlyList<ulong> aliveClientIds)
    {
        if (!IsServer || aliveClientIds == null)
        {
            return;
        }

        assignedTasks.Clear();

        if (registeredObjectives.Count == 0)
        {
            Debug.LogWarning("TaskManager: no TaskObjective is live in this scene, so no task can " +
                "be completed - handing out none rather than tasks nobody could finish.");
            return;
        }

        int dealtPerPlayer = 0;
        foreach (ulong clientId in aliveClientIds)
        {
            List<int> picked = PickTasksForPlayer();
            dealtPerPlayer = picked.Count;

            foreach (int taskIndex in picked)
            {
                assignedTasks.Add(new TaskEntry { ClientId = clientId, TaskIndex = taskIndex, Completed = false });
            }
        }

        // Once per session, not once per round: a short deal is normal (ThreePlus tasks drop out
        // of the pool below three alive players) and would otherwise spam the console every turn.
        if (aliveClientIds.Count > 0 && dealtPerPlayer < tasksPerRound && !_warnedAboutShortDeal)
        {
            _warnedAboutShortDeal = true;
            Debug.LogWarning($"TaskManager: only {dealtPerPlayer} of {tasksPerRound} tasks are " +
                "completable right now - add a TaskObjective for the rest to put them in rotation.");
        }
    }

    // Picks up to tasksPerRound DISTINCT task indices. The old GenerateTasks drew with
    // replacement, so a player could be handed the same task two or three times over and see a
    // list with duplicate rows that all completed at once.
    private List<int> PickTasksForPlayer()
    {
        List<int> picked = new();
        if (tasks == null || tasks.Length == 0)
        {
            Debug.LogWarning("TaskManager: no tasks authored, nobody will get any.");
            return picked;
        }

        List<int> pool = new();
        for (int i = 0; i < tasks.Length; i++)
        {
            if (tasks[i] == null)
            {
                continue;
            }

            // Nothing in this level completes it, so handing it out would only lock its holder
            // out of the gun for a round they had no way to finish.
            if (!registeredObjectives.Contains(tasks[i]))
            {
                continue;
            }

            // Tasks that need a crowd stop being handed out once the lobby has thinned out.
            if (tasks[i].taskType == Challenge.TaskType.ThreePlus &&
                GameManager.Instance != null && GameManager.Instance.AlivePlayersCount() < 3)
            {
                continue;
            }

            pool.Add(i);
        }

        int wanted = Mathf.Min(tasksPerRound, pool.Count);
        for (int i = 0; i < wanted; i++)
        {
            int swapWith = UnityEngine.Random.Range(i, pool.Count);
            (pool[i], pool[swapWith]) = (pool[swapWith], pool[i]);
            picked.Add(pool[i]);
        }

        return picked;
    }

    #endregion

    #region Completion

    /// <summary>Called on the interacting client by TaskObjective. Turns the authored asset into
    /// the index the server understands and asks the server to mark it done.</summary>
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

    // RequireOwnership=false: this object is owned by whichever player object happens to host the
    // surviving singleton, so every other client would be rejected by the default. The caller
    // cannot name a victim - the client id comes from the transport - so a client can only ever
    // complete a task that was actually assigned to itself.
    [ServerRpc(RequireOwnership = false)]
    private void CompleteTaskServerRpc(int taskIndex, ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;

        for (int i = 0; i < assignedTasks.Count; i++)
        {
            TaskEntry entry = assignedTasks[i];
            if (entry.ClientId != clientId || entry.TaskIndex != taskIndex || entry.Completed)
            {
                continue;
            }

            entry.Completed = true;
            assignedTasks[i] = entry;
            return;
        }
    }

    /// <summary>Server only. Marks a task complete for a player without them having pressed
    /// anything themselves - for a group task like blackjack, where the thing that completes it
    /// is a shared outcome the server resolves (a round finishing with enough players at the
    /// table), not a solo interaction.</summary>
    public void CompleteTaskForPlayer(ulong clientId, Challenge task)
    {
        if (!IsServer || task == null || !taskIndices.TryGetValue(task, out int taskIndex))
        {
            return;
        }

        for (int i = 0; i < assignedTasks.Count; i++)
        {
            TaskEntry entry = assignedTasks[i];
            if (entry.ClientId != clientId || entry.TaskIndex != taskIndex || entry.Completed)
            {
                continue;
            }

            entry.Completed = true;
            assignedTasks[i] = entry;
            return;
        }
    }

    /// <summary>Server only. True when this player has nothing outstanding - which is also true
    /// when they were never given anything, so the first round of a match and any player who
    /// joined mid-match are never locked out.</summary>
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

    /// <summary>True when this task is currently assigned to the local player and still open -
    /// what TaskObjective checks before offering an interaction prompt.</summary>
    public bool IsTaskOpenForLocalPlayer(Challenge task)
    {
        if (task == null || NetworkManager.Singleton == null || !taskIndices.TryGetValue(task, out int taskIndex))
        {
            return false;
        }

        ulong localId = NetworkManager.Singleton.LocalClientId;
        foreach (TaskEntry entry in assignedTasks)
        {
            if (entry.ClientId == localId && entry.TaskIndex == taskIndex && !entry.Completed)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when this task was assigned to the local player this round and they have
    /// already finished it - what a TaskObjective uses to decide whether its "done" visual (the
    /// lit campfire) should be showing right now.</summary>
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

    /// <summary>Fills <paramref name="results"/> with the local player's tasks for this round, in
    /// assignment order. Takes the list to fill so the HUD can refresh every change without
    /// allocating.</summary>
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

    /// <summary>The authored asset behind a TaskEntry, or null if the index is stale (the tasks
    /// array was edited between the assignment and this read).</summary>
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
