using UnityEngine;

// One in-game task, Among Us style: "Light up the campfire", "Check the mail box".
//
// Authoring a new task is two steps and no code:
//   1. Assets > Create > Scriptable Objects > Task, fill in the name/description, drop it in
//      TaskManager's `tasks` array (TaskManager lives on Assets/Prefabs/Player.prefab, next to
//      GameManager).
//   2. Add a TaskObjective component to the world object the player has to interact with and
//      assign this asset to it.
// TaskObjective handles the interaction prompt, the networking, and marking the task complete.
[CreateAssetMenu(fileName = "Task", menuName = "Scriptable Objects/Task")]
public class Challenge : ScriptableObject
{
    [Tooltip("Short label shown in the task list, e.g. \"Campfire\".")]
    public string taskName;
    [Tooltip("What the player actually has to do, e.g. \"Light up campfire\". Shown under the name.")]
    [TextArea(1, 3)]
    public string taskDiscription;
    [Tooltip("Verb used in the interaction prompt when the player looks at this task's object, " +
        "e.g. \"Light\" reads as \"Light  [E]\". Leave empty for \"Use\".")]
    public string interactionVerb;
    [Tooltip("Optional icon for the task list row.")]
    public Sprite icon;

    public enum Difficulty
    {
        Easy,
        Medium,
        Hard
    }
    public Difficulty difficulty;

    public enum TaskType
    {
        Useful,
        Useless,
        ThreePlus
    }
    [Tooltip("ThreePlus tasks are only handed out while at least three players are still alive.")]
    public TaskType taskType;

    /// <summary>Label for the interaction prompt HUD - the authored verb, or a sane default.</summary>
    public string InteractionPrompt => string.IsNullOrWhiteSpace(interactionVerb) ? "Use" : interactionVerb;

    /// <summary>Label for the task list - the authored name, or the asset name as a fallback so a
    /// half-filled asset still shows something readable instead of a blank row.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(taskName) ? name : taskName;
}
