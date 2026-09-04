using UnityEngine;
using UnityEngine.Events;

// The world half of a task: drop this on the campfire, the mailbox, the boombox - anything the
// player walks up to and interacts with - and assign the Challenge asset it completes. That plus
// the asset itself is the whole authoring flow; no code per task.
//
// It plugs into the existing Interact raycast, so it needs a collider on the Interact layer
// (pickUpLayerMask on the player's Interact component) exactly like a BumBox or a HidingSpot.
[RequireComponent(typeof(Collider))]
public class TaskObjective : MonoBehaviour, IInteractable
{
    [SerializeField, Tooltip("The task this object completes. Must also be in TaskManager's tasks " +
        "array (TaskManager sits on Assets/Prefabs/Player.prefab), or it can never be handed out.")]
    private Challenge task;

    [SerializeField, Tooltip("Optional: switched on locally once the interacting player completes " +
        "this task - the lit campfire, an open mailbox flap. Local only, and deliberately so: " +
        "everyone gets their own copy of the task, so everyone lights their own campfire.")]
    private GameObject completedVisual;

    [SerializeField, Tooltip("Optional extra reaction on completion - a sound, an animator trigger, " +
        "a particle burst. Fires on the interacting player's client only.")]
    private UnityEvent onCompleted;

    public bool IsHeld { get; set; }
    public bool IsPickable { get; set; } = false;

    public string InteractionPrompt => task != null ? task.InteractionPrompt : "Use";

    // The prompt only appears, and the press only counts, while this is one of the LOCAL player's
    // still-open tasks. Without this every player would see a "Light" prompt on a campfire that
    // was never on their list, press it, and get nothing.
    public bool CanInteract =>
        task != null && TaskManager.Instance != null && TaskManager.Instance.IsTaskOpenForLocalPlayer(task);

    // One press and it's done - never latch the player onto this the way a pick-up or a hiding
    // spot does, or the next Interact press would be swallowed as a "drop".
    public bool LatchesPlayer => false;

    private TaskManager subscribedManager;

    private void Awake()
    {
        if (completedVisual != null)
        {
            completedVisual.SetActive(false);
        }
    }

    private void OnEnable()
    {
        // Puts this task into rotation for as long as this object exists. A task with no objective
        // in the level is never dealt, so adding this component is the whole opt-in.
        TaskManager.RegisterObjective(task);
    }

    // TaskManager sits on the player prefab, so it does not exist yet when an in-scene prop like
    // the campfire wakes up - subscribing in OnEnable would silently bind to nothing and the
    // visual would never reset. Latch on the first frame it appears instead, and re-latch if a
    // new match brings up a different instance. Cheap: a reference compare.
    private void Update()
    {
        TaskManager manager = TaskManager.Instance;
        if (manager == subscribedManager)
        {
            return;
        }

        if (subscribedManager != null)
        {
            subscribedManager.OnTasksChanged -= OnTasksChanged;
        }

        subscribedManager = manager;

        if (subscribedManager != null)
        {
            subscribedManager.OnTasksChanged += OnTasksChanged;
        }

        OnTasksChanged();
    }

    private void OnDisable()
    {
        TaskManager.UnregisterObjective(task);

        if (subscribedManager != null)
        {
            subscribedManager.OnTasksChanged -= OnTasksChanged;
            subscribedManager = null;
        }
    }

    // Drive the visual straight off the replicated state rather than only clearing it: a campfire
    // this player lit last round has to go back to being unlit whether the new round handed them
    // the campfire again (open -> unlit) or not (unassigned -> unlit).
    private void OnTasksChanged()
    {
        if (completedVisual == null)
        {
            return;
        }

        bool shouldShow = task != null && subscribedManager != null &&
            subscribedManager.IsTaskCompletedByLocalPlayer(task);

        if (completedVisual.activeSelf != shouldShow)
        {
            completedVisual.SetActive(shouldShow);
        }
    }

    public void Interact(ulong clientId)
    {
        if (!CanInteract)
        {
            return;
        }

        TaskManager.Instance.ReportTaskCompleted(task);

        if (completedVisual != null)
        {
            completedVisual.SetActive(true);
        }

        onCompleted?.Invoke();
    }

    public void Drop()
    {
        // Nothing to release - this is never held (see LatchesPlayer).
    }
}
