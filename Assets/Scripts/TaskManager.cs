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
        List<PlayerTask> tasksForPlayer = new();

        while (tasksForPlayer.Count < taskCount)
        {
            int randomNumber = UnityEngine.Random.Range(0, tasks.Length);
            Challenge task = tasks[randomNumber];

            if (task.taskType == Challenge.TaskType.ThreePlus &&
                GameManager.Instance.AlivePlayersCount() < 3)
                continue;

            tasksForPlayer.Add(new PlayerTask(task));
        }

        return tasksForPlayer;
    }

}
