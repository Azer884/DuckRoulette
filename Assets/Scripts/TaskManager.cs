using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class TaskManager : NetworkBehaviour
{
    public static TaskManager Instance { get; private set; }
    
    public Challenge[] tasks;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    public List<PlayerTask> GenerateTasks()
    {
        int taskCount = 3;
        int maxAttempts = 50; // Prevent infinite loops
        int attempts = 0;
        List<PlayerTask> tasksForPlayer = new();

        while (tasksForPlayer.Count < taskCount && attempts < maxAttempts)
        {
            int randomNumber = UnityEngine.Random.Range(0, tasks.Length);
            Challenge task = tasks[randomNumber];

            if (task.taskType == Challenge.TaskType.ThreePlus &&
                GameManager.Instance.AlivePlayersCount() < 3)
            {
                attempts++;
                continue;
            }

            tasksForPlayer.Add(new PlayerTask(task));
            attempts++;
        }

        // Log if we couldn't generate enough tasks
        if (tasksForPlayer.Count < taskCount)
        {
            Debug.LogWarning($"Could only generate {tasksForPlayer.Count} tasks out of {taskCount} requested");
        }

        return tasksForPlayer;
    }

}
