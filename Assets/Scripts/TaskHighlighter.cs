using System.Collections.Generic;
using UnityEngine;

// Helps a player who has been sitting on a task: once one of the LOCAL player's tasks has stayed
// open for TaskManager.RoundsBeforeHighlight gun hand-offs, every world object that completes it
// gets a blinking outline that shows through walls, so they know where to go.
//
// Purely local and cosmetic. World objectives register themselves (Register/Unregister, same
// lifetime as TaskManager.RegisterObjective); this decides a few times a second which renderers
// the local player should see outlined and publishes them in ActiveRenderers. The drawing itself
// is TaskOutlineFeature, a screen-space pass on the URP renderer - nothing is added to or changed
// on the objects.
public class TaskHighlighter : MonoBehaviour
{
    private const float RefreshInterval = 0.25f;

    private static readonly Dictionary<Challenge, List<GameObject>> Targets = new();
    private static readonly List<Renderer> Active = new();
    private static readonly List<Renderer> EmptyList = new();

    private static TaskHighlighter instance;

    private readonly List<TaskManager.TaskEntry> localTasks = new();
    private readonly HashSet<Renderer> seen = new();
    private float refreshTimer;

    /// <summary>The renderers TaskOutlineFeature should outline this frame.</summary>
    public static IReadOnlyList<Renderer> ActiveRenderers => instance != null ? Active : EmptyList;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (instance != null)
        {
            return;
        }

        GameObject go = new("TaskHighlighter");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<TaskHighlighter>();
    }

    /// <summary>Marks this object as something that completes the task. Call from OnEnable.</summary>
    public static void Register(Challenge task, GameObject target)
    {
        if (task == null || target == null)
        {
            return;
        }

        if (!Targets.TryGetValue(task, out List<GameObject> list))
        {
            list = new List<GameObject>();
            Targets[task] = list;
        }

        if (!list.Contains(target))
        {
            list.Add(target);
        }
    }

    /// <summary>Call from OnDisable.</summary>
    public static void Unregister(Challenge task, GameObject target)
    {
        if (task != null && target != null && Targets.TryGetValue(task, out List<GameObject> list))
        {
            list.Remove(target);
        }

        if (instance != null)
        {
            instance.refreshTimer = 0f;
        }
    }

    private void OnDestroy()
    {
        Active.Clear();
        if (instance == this)
        {
            instance = null;
        }
    }

    private void Update()
    {
        refreshTimer -= Time.unscaledDeltaTime;
        if (refreshTimer > 0f)
        {
            return;
        }

        refreshTimer = RefreshInterval;
        Refresh();
    }

    private void Refresh()
    {
        Active.Clear();
        seen.Clear();

        TaskManager manager = TaskManager.Instance;
        if (manager == null || !manager.IsSpawned)
        {
            return;
        }

        manager.GetLocalPlayerTasks(localTasks);
        foreach (TaskManager.TaskEntry entry in localTasks)
        {
            if (entry.Completed || entry.RoundsOpen < manager.RoundsBeforeHighlight)
            {
                continue;
            }

            Challenge task = manager.GetTask(entry.TaskIndex);
            if (task == null || !Targets.TryGetValue(task, out List<GameObject> objects))
            {
                continue;
            }

            foreach (GameObject target in objects)
            {
                if (target == null || !target.activeInHierarchy)
                {
                    continue;
                }

                // Every visual under the objective, however deep - props often keep their mesh
                // on a child (or several) rather than on the object with the script. Disabled
                // renderers (the engine while carried) are skipped by the pass each frame.
                foreach (Renderer r in target.GetComponentsInChildren<Renderer>(true))
                {
                    if ((r is MeshRenderer || r is SkinnedMeshRenderer) && seen.Add(r))
                    {
                        Active.Add(r);
                    }
                }
            }
        }
    }
}
