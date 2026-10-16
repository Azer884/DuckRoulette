using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Lets one person try every task alone in Assets/Scenes/TestingEnviroment.unity. Start as Host,
// then use the function keys (listed on screen):
//   F1..F7   hand yourself task 1..7 from TaskManager's list (group tasks included)
//   F8       put the truck engine straight in (then press Drive on the truck)
//   F9       simulate a gun hand-off: open tasks age one round (outline from round 1, task
//            withdrawn at round 2)
//   F10      drop all your tasks
//   F11      hide / show this panel
//
// While this component is enabled TaskManager.SoloTestMode is on: no automatic camping tasks, no
// "two players left" clear-out, group tasks can be held alone, and the push-truck goal is placed
// in front of the truck when the map has no river. Host only; it does nothing in a real match
// because it only lives in the testing scene.
public class TaskTestBench : MonoBehaviour
{
    [SerializeField] private bool showPanel = true;
    [SerializeField, Tooltip("Where a player who fell through the floor is put back.")]
    private Vector3 rescuePoint = new(0f, 3f, -4f);
    [SerializeField] private float fallRescueHeight = -10f;

    private readonly List<TaskManager.TaskEntry> myTasks = new();
    private readonly StringBuilder text = new();
    private GUIStyle style;

    private static readonly Key[] TaskKeys = { Key.F1, Key.F2, Key.F3, Key.F4, Key.F5, Key.F6, Key.F7 };

    private void OnEnable()
    {
        TaskManager.SoloTestMode = true;
    }

    private void OnDisable()
    {
        TaskManager.SoloTestMode = false;
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        if (keyboard[Key.F11].wasPressedThisFrame)
        {
            showPanel = !showPanel;
        }

        NetworkManager network = NetworkManager.Singleton;
        RescueFallenPlayer(network);

        TaskManager manager = TaskManager.Instance;
        if (manager == null || network == null || !network.IsServer || !manager.IsSpawned)
        {
            return;
        }

        ulong me = network.LocalClientId;

        for (int i = 0; i < TaskKeys.Length; i++)
        {
            if (keyboard[TaskKeys[i]].wasPressedThisFrame)
            {
                manager.DebugAssignTask(me, i);
            }
        }

        if (keyboard[Key.F8].wasPressedThisFrame)
        {
            TruckEngine engine = FindAnyObjectByType<TruckEngine>();
            if (engine != null)
            {
                engine.DebugInstall(me);
            }
        }

        if (keyboard[Key.F9].wasPressedThisFrame)
        {
            manager.OnGunHandedOff();
        }

        if (keyboard[Key.F10].wasPressedThisFrame)
        {
            manager.DebugClearTasks(me);
        }
    }

    // This scene has no PlayerSpawner: the player appears at the origin, partly inside the ground
    // box, and drops straight through it. Put them back on top.
    private void RescueFallenPlayer(NetworkManager network)
    {
        if (network == null || !network.IsListening)
        {
            return;
        }

        NetworkObject player = network.SpawnManager.GetLocalPlayerObject();
        if (player == null || player.transform.position.y > fallRescueHeight)
        {
            return;
        }

        CharacterController controller = player.GetComponent<CharacterController>();
        bool wasEnabled = controller != null && controller.enabled;
        if (controller != null)
        {
            controller.enabled = false;
        }

        player.transform.position = rescuePoint;

        if (controller != null)
        {
            controller.enabled = wasEnabled;
        }
    }

    private void OnGUI()
    {
        if (!showPanel)
        {
            return;
        }

        style ??= new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.UpperLeft,
            fontSize = 14,
            richText = true,
            wordWrap = false
        };

        text.Clear();
        text.AppendLine("<b>Task test bench</b>  (F11 hide)");

        TaskManager manager = TaskManager.Instance;
        NetworkManager network = NetworkManager.Singleton;
        if (network == null || !network.IsListening)
        {
            text.AppendLine("Start as Host first.");
        }
        else if (!network.IsServer)
        {
            text.AppendLine("Host only.");
        }
        else if (manager == null || manager.tasks == null)
        {
            text.AppendLine("No TaskManager yet.");
        }
        else
        {
            for (int i = 0; i < manager.tasks.Length && i < TaskKeys.Length; i++)
            {
                Challenge task = manager.tasks[i];
                string name = task != null ? task.DisplayName : "(empty)";
                string missing = task != null && task != manager.TeamUpTask && !TaskManager.IsObjectiveRegistered(task)
                    ? "  <color=#ff6060>no object in scene</color>"
                    : "";
                text.AppendLine($"F{i + 1}  {name}{(task != null ? $" [{task.taskType}]" : "")}{missing}");
            }

            text.AppendLine("F8  install truck engine    F9  next round    F10  drop my tasks");

            // The real bar lives on GameScene's HUD; this scene has none, so mirror it here.
            if (HidingSpotTimerUI.Instance == null && HidingSpotTimerUI.TryGetExternal(out float remaining, out Color color))
            {
                text.AppendLine($"<color=#{ColorUtility.ToHtmlStringRGB(color)}>Drive timer: {remaining:0.0}s</color>");
            }

            text.AppendLine();

            manager.GetLocalPlayerTasks(myTasks);
            if (myTasks.Count == 0)
            {
                text.AppendLine("My tasks: none");
            }

            foreach (TaskManager.TaskEntry entry in myTasks)
            {
                Challenge task = manager.GetTask(entry.TaskIndex);
                string state = entry.Completed ? "<color=#60ff60>done</color>" : $"open, rounds {entry.RoundsOpen}";
                text.AppendLine($"My task: {(task != null ? task.DisplayName : "?")} - {state}");
            }
        }

        GUILayout.BeginArea(new Rect(12f, 12f, 520f, 400f));
        GUILayout.Label(text.ToString(), style);
        GUILayout.EndArea();
    }
}
