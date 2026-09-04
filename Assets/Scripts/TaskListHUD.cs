using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

// The local player's task list for the current round - the Among Us checklist in the corner.
//
// All visuals live on Assets/Prefabs/Ui/TaskListHUD.prefab: drop that prefab into the game scene
// once and edit colors/layout/fonts there like any other UI, the same way ShotClockUI works. This
// script only drives it.
public class TaskListHUD : MonoBehaviour
{
    [SerializeField] private GameObject root;
    [SerializeField, Tooltip("Parent the task rows are instantiated under. Put a VerticalLayoutGroup on it.")]
    private Transform rowContainer;
    [SerializeField, Tooltip("Inactive row prefab/instance cloned per task. Needs a TextMeshProUGUI; " +
        "an Image named for the icon is optional.")]
    private GameObject rowTemplate;
    [SerializeField] private TextMeshProUGUI headerText;

    [Header("Colors")]
    [SerializeField] private Color openColor = new(0.95f, 0.95f, 0.95f, 0.95f);
    [SerializeField] private Color completedColor = new(0.45f, 0.85f, 0.4f, 0.9f);

    [Header("Text")]
    [SerializeField] private string headerFormat = "TASKS  {0}/{1}";
    [SerializeField] private string openMarker = "□";
    [SerializeField] private string completedMarker = "✓";

    private readonly List<TaskManager.TaskEntry> localTasks = new();
    private readonly List<GameObject> spawnedRows = new();
    private TaskManager subscribedManager;

    private void Awake()
    {
        if (rowTemplate != null)
        {
            rowTemplate.SetActive(false);
        }

        if (root != null)
        {
            root.SetActive(false);
        }
    }

    // TaskManager lives on the player prefab and only exists once a player has spawned, so there
    // is no single moment at startup where subscribing is guaranteed to work. Re-check each frame
    // (cheap - a reference compare) and latch on the first time it appears, and again if a new
    // match brings up a different instance.
    private void Update()
    {
        TaskManager manager = TaskManager.Instance;

        if (manager != subscribedManager)
        {
            if (subscribedManager != null)
            {
                subscribedManager.OnTasksChanged -= Refresh;
            }

            subscribedManager = manager;

            if (subscribedManager != null)
            {
                subscribedManager.OnTasksChanged += Refresh;
                Refresh();
            }
            else
            {
                Refresh();
            }
        }
    }

    private void OnDisable()
    {
        if (subscribedManager != null)
        {
            subscribedManager.OnTasksChanged -= Refresh;
            subscribedManager = null;
        }
    }

    private void Refresh()
    {
        if (root == null || rowContainer == null || rowTemplate == null)
        {
            return;
        }

        if (subscribedManager == null || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            root.SetActive(false);
            return;
        }

        subscribedManager.GetLocalPlayerTasks(localTasks);

        // Nothing assigned yet (the first few seconds of a match, or a dead player) - hide the
        // panel outright rather than showing an empty box.
        if (localTasks.Count == 0)
        {
            root.SetActive(false);
            return;
        }

        root.SetActive(true);
        EnsureRowCount(localTasks.Count);

        int completed = 0;
        for (int i = 0; i < localTasks.Count; i++)
        {
            TaskManager.TaskEntry entry = localTasks[i];
            Challenge task = subscribedManager.GetTask(entry.TaskIndex);
            if (entry.Completed)
            {
                completed++;
            }

            ApplyRow(spawnedRows[i], task, entry.Completed);
        }

        if (headerText != null)
        {
            headerText.text = string.Format(headerFormat, completed, localTasks.Count);
        }
    }

    // Rows are pooled rather than destroyed and rebuilt: this refreshes on every replicated
    // change, and churning UI objects each time would thrash the layout group for no reason.
    private void EnsureRowCount(int wanted)
    {
        while (spawnedRows.Count < wanted)
        {
            GameObject row = Instantiate(rowTemplate, rowContainer);
            spawnedRows.Add(row);
        }

        for (int i = 0; i < spawnedRows.Count; i++)
        {
            spawnedRows[i].SetActive(i < wanted);
        }
    }

    private void ApplyRow(GameObject row, Challenge task, bool isCompleted)
    {
        if (row == null)
        {
            return;
        }

        TextMeshProUGUI label = row.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null)
        {
            string marker = isCompleted ? completedMarker : openMarker;
            // A stale index (the tasks array was edited mid-match) shows as a plain "?" row
            // instead of blanking out or throwing.
            string name = task != null ? task.DisplayName : "?";
            string description = task != null ? task.taskDiscription : string.Empty;

            label.text = string.IsNullOrWhiteSpace(description)
                ? $"{marker}  {name}"
                : $"{marker}  {name} - {description}";
            label.color = isCompleted ? completedColor : openColor;
        }

        Image icon = row.GetComponentInChildren<Image>(true);
        if (icon != null && task != null && task.icon != null)
        {
            icon.sprite = task.icon;
            icon.enabled = true;
        }
        else if (icon != null)
        {
            icon.enabled = false;
        }
    }
}
