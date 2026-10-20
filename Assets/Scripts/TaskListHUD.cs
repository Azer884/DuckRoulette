using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

// The local player's task - the Among Us checklist in the corner. Only players who were caught
// camping have one (see TaskManager), so for everyone else the panel never appears. While the task
// is open a warning line says the gun is off the table; once it is done the
// row is struck through and the whole panel tucks itself away.
//
// All visuals live on Assets/Prefabs/Ui/TaskListHUD.prefab: drop that prefab into the game scene
// once and edit colors/layout/fonts there like any other UI, the same way ShotClockUI works. This
// script only drives it.
//
// Every bit of motion is authored in the TaskHUD Animator Controller (ShowTaskHUD / HideTaskHUD
// clips). This script never moves, scales, fades or recolours anything itself - it only fills in
// the rows and tells the Animator which state to play.
public class TaskListHUD : MonoBehaviour
{
    [SerializeField] private GameObject root;
    [SerializeField, Tooltip("Parent the task rows are instantiated under. Put a VerticalLayoutGroup on it.")]
    private Transform rowContainer;
    [SerializeField, Tooltip("Inactive row prefab/instance cloned per task. Needs an Icon Image, " +
        "a TaskTitle TextMeshProUGUI and a TaskDescription TextMeshProUGUI.")]
    private GameObject rowTemplate;
    [SerializeField] private TextMeshProUGUI headerText;
    [SerializeField, Tooltip("Line under the task telling the player they are off the gun until " +
        "it is done. Hidden once the task is finished.")]
    private TextMeshProUGUI warningText;

    [Header("Colors")]
    [SerializeField] private Color openColor = new(0.95f, 0.95f, 0.95f, 0.95f);
    [SerializeField] private Color completedColor = new(0.45f, 0.85f, 0.4f, 0.9f);

    [Header("Text")]
    [SerializeField] private string soloHeader = "TASK";
    [SerializeField, Tooltip("{0} = players sharing the task, including you.")]
    private string groupHeader = "GROUP TASK  ({0} players)";
    [SerializeField] private string warningMessage = "Do it or you won't get the gun next round!";
    [SerializeField, Tooltip("Seconds the finished task stays on screen before the panel hides.")]
    private float hideAfterCompleteDelay = 1.6f;
    // Comic Sans MS SDF is a dynamic atlas over the Comic Sans MS TTF, which covers WGL4 - so the
    // circles resolve, while the old U+2713 checkmark did not and drew as a missing-glyph box.
    [SerializeField] private string openMarker = "○";
    [SerializeField] private string completedMarker = "●";
    [SerializeField, Tooltip("Strike completed rows through as well as recolouring them.")]
    private bool strikeCompleted = true;

    [Header("Animation")]
    [SerializeField, Tooltip("Animator running the TaskHUD controller. Left empty, the panel just " +
        "appears and disappears.")]
    private Animator panelAnimator;
    [SerializeField, Tooltip("Animator state played when the panel appears.")]
    private string showState = "ShowTaskHUD";
    [SerializeField, Tooltip("Animator state played when the panel goes away. The panel is " +
        "switched off once it has finished.")]
    private string hideState = "HideTaskHUD";
    [SerializeField, Tooltip("SFX played on a brand new task and on the unfinished-task warning.")]
    private AudioClip attentionClipOverride;

    private readonly List<TaskManager.TaskEntry> localTasks = new();
    private readonly List<GameObject> spawnedRows = new();
    private TaskManager subscribedManager;

    // Tracks TaskEntry.RoundsOpen per task index, so a round ending with the task still open (the
    // server increments RoundsOpen on every gun hand-off - see TaskManager.OnGunHandedOff) can be
    // told apart from any other refresh. Keyed by task index rather than by row position, so a
    // reordered list can't read as "everything is new".
    private readonly Dictionary<int, int> roundsOpenState = new();
    private Coroutine hideAnimationRoutine;
    private Coroutine hideRoutine;

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
                // A new match means a new checklist - forget last round's age state so the first
                // refresh seeds quietly instead of warning for work from before.
                roundsOpenState.Clear();
                Refresh();
            }
            else
            {
                Refresh();
            }
        }
    }

    private void OnEnable()
    {
        // So flipping "Show Task List" in the settings takes effect without waiting for the next
        // replicated task change.
        GameplaySettings.Changed += Refresh;
    }

    private void OnDisable()
    {
        GameplaySettings.Changed -= Refresh;

        // Disabling stops coroutines; don't leave a dead hide in flight blocking the next one.
        hideAnimationRoutine = null;

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
            HidePanel();
            return;
        }

        // Game tab: players who would rather keep the corner clear can switch the checklist off.
        if (!GameplaySettings.ShowTaskList)
        {
            HidePanel();
            return;
        }

        subscribedManager.GetLocalPlayerTasks(localTasks);

        // Nothing assigned yet (the first few seconds of a match, or a dead player) - hide the
        // panel outright rather than showing an empty box.
        if (localTasks.Count == 0)
        {
            HidePanel();
            return;
        }

        bool panelWasVisible = root.activeSelf;

        // Coming back while the hide animation is still playing: cancel it and show again.
        if (hideAnimationRoutine != null)
        {
            StopCoroutine(hideAnimationRoutine);
            hideAnimationRoutine = null;
            panelWasVisible = false;
        }

        bool allDone = true;
        foreach (TaskManager.TaskEntry entry in localTasks)
        {
            allDone &= entry.Completed;
        }

        // Finished before this HUD ever showed it (joined late, settings toggled): nothing to
        // celebrate, just stay hidden.
        if (allDone && !panelWasVisible)
        {
            HidePanel();
            return;
        }

        if (hideRoutine != null && !allDone)
        {
            StopCoroutine(hideRoutine);
            hideRoutine = null;
        }

        root.SetActive(true);
        EnsureRowCount(localTasks.Count);

        int completed = 0;
        bool anyNewTask = false;
        bool anyRoundMissed = false;
        for (int i = 0; i < localTasks.Count; i++)
        {
            TaskManager.TaskEntry entry = localTasks[i];
            Challenge task = subscribedManager.GetTask(entry.TaskIndex);
            if (entry.Completed)
            {
                completed++;
            }

            GameObject row = spawnedRows[i];

            bool knownAge = roundsOpenState.TryGetValue(entry.TaskIndex, out int previousRoundsOpen);
            if (!knownAge)
            {
                // First sighting: this is either a genuinely brand new task, or the first refresh
                // after (re)subscribing - panelWasVisible tells those two apart below.
                anyNewTask = true;
            }
            else if (!entry.Completed && entry.RoundsOpen > previousRoundsOpen)
            {
                anyRoundMissed = true;
            }
            roundsOpenState[entry.TaskIndex] = entry.RoundsOpen;

            ApplyRow(row, task, entry.Completed);
        }

        UpdateHeader(localTasks);

        if (warningText != null)
        {
            warningText.text = warningMessage;
            warningText.gameObject.SetActive(!allDone);
        }

        if (allDone && hideRoutine == null)
        {
            hideRoutine = StartCoroutine(HideAfterComplete());
        }

        if (!panelWasVisible)
        {
            ShowPanel();
        }
        else if (anyNewTask || anyRoundMissed)
        {
            // The panel was already up, so a brand new task slotted into an existing list, or a
            // round ending with a task still undone, both deserve the same "look here" cue - not
            // the one the panel's own first appearance already got from ShowPanel.
            PlayAttentionSound();
        }
    }

    private IEnumerator HideAfterComplete()
    {
        yield return new WaitForSeconds(hideAfterCompleteDelay);
        hideRoutine = null;
        HidePanel();
    }

    private void UpdateHeader(List<TaskManager.TaskEntry> tasks)
    {
        if (headerText == null)
        {
            return;
        }

        int groupId = 0;
        foreach (TaskManager.TaskEntry entry in tasks)
        {
            if (!entry.Completed && entry.IsGroup)
            {
                groupId = entry.GroupId;
            }
        }

        string text = groupId != 0 && subscribedManager != null
            ? string.Format(groupHeader, subscribedManager.GetGroupSize(groupId))
            : soloHeader;
        headerText.text = text;
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
            GameObject row = spawnedRows[i];
            bool shouldBeActive = i < wanted;

            row.SetActive(shouldBeActive);
        }
    }

    private void ApplyRow(GameObject row, Challenge task, bool isCompleted)
    {
        if (row == null)
        {
            return;
        }

        RowRefs refs = GetRowRefs(row);

        // A stale index (the tasks array was edited mid-match) shows as a plain "?" row instead of
        // blanking out or throwing.
        string name = task != null ? task.DisplayName : "?";
        string description = task != null ? task.taskDiscription : string.Empty;
        string marker = isCompleted ? completedMarker : openMarker;

        if (refs.title != null)
        {
            string titleBody = $"{marker}  {name}";
            if (isCompleted && strikeCompleted)
            {
                titleBody = $"<s>{titleBody}</s>";
            }
            refs.title.text = titleBody;
            refs.title.color = isCompleted ? completedColor : openColor;
        }

        if (refs.description != null)
        {
            string descBody = isCompleted && strikeCompleted ? $"<s>{description}</s>" : description;
            refs.description.text = descBody;
            refs.description.color = isCompleted ? completedColor : openColor;
            refs.description.gameObject.SetActive(!string.IsNullOrWhiteSpace(description));
        }

        if (refs.icon != null && task != null && task.icon != null)
        {
            refs.icon.sprite = task.icon;
            refs.icon.enabled = true;
        }
        else if (refs.icon != null)
        {
            refs.icon.enabled = false;
        }
    }

    // Row child refs are looked up by name once per row instance and cached, rather than re-walked
    // every refresh - EnsureRowCount only grows the pool, so this never needs invalidating.
    private readonly Dictionary<GameObject, RowRefs> rowRefCache = new();

    private struct RowRefs
    {
        public TextMeshProUGUI title;
        public TextMeshProUGUI description;
        public Image icon;
    }

    private RowRefs GetRowRefs(GameObject row)
    {
        if (rowRefCache.TryGetValue(row, out RowRefs cached))
        {
            return cached;
        }

        RowRefs refs = default;
        Transform titleT = row.transform.Find("TaskTitle");
        Transform descT = row.transform.Find("TaskDescription");
        Transform iconT = row.transform.Find("Icon");

        refs.title = titleT != null ? titleT.GetComponent<TextMeshProUGUI>() : row.GetComponentInChildren<TextMeshProUGUI>(true);
        refs.description = descT != null ? descT.GetComponent<TextMeshProUGUI>() : null;
        refs.icon = iconT != null ? iconT.GetComponent<Image>() : row.GetComponentInChildren<Image>(true);

        rowRefCache[row] = refs;
        return refs;
    }

    // -------------------------------------------------------------------------------------------
    // Animation - all motion lives in the TaskHUD controller (ShowTaskHUD / HideTaskHUD clips).
    // -------------------------------------------------------------------------------------------

    private bool HasAnimator => panelAnimator != null && panelAnimator.isActiveAndEnabled;

    private void HidePanel()
    {
        if (hideRoutine != null)
        {
            StopCoroutine(hideRoutine);
            hideRoutine = null;
        }

        if (!root.activeSelf || hideAnimationRoutine != null)
        {
            // Already hidden, or the hide animation is already on its way out.
            return;
        }

        if (HasAnimator && !string.IsNullOrEmpty(hideState))
        {
            panelAnimator.Play(hideState, 0, 0f);
            hideAnimationRoutine = StartCoroutine(HideAfterAnimation());
            return;
        }

        SnapHidePanel();
    }

    // Waits for HideTaskHUD to actually finish instead of a hand-tuned duration, so retiming the
    // clip needs no matching change here.
    private IEnumerator HideAfterAnimation()
    {
        // Play() lands on the next Animator update.
        yield return null;

        int hideHash = Animator.StringToHash(hideState);
        while (HasAnimator)
        {
            AnimatorStateInfo state = panelAnimator.GetCurrentAnimatorStateInfo(0);
            if (state.shortNameHash != hideHash || state.normalizedTime >= 1f)
            {
                break;
            }
            yield return null;
        }

        hideAnimationRoutine = null;
        SnapHidePanel();
    }

    private void SnapHidePanel()
    {
        root.SetActive(false);
    }

    private void ShowPanel()
    {
        if (HasAnimator && !string.IsNullOrEmpty(showState))
        {
            panelAnimator.Play(showState, 0, 0f);
        }

        PlayAttentionSound();
    }

    // Played on a brand new task and on the "you missed a round" warning (Refresh's anyRoundMissed).
    private void PlayAttentionSound()
    {
        AudioClip clip = attentionClipOverride != null
            ? attentionClipOverride
            : (SFXManager.Instance != null ? SFXManager.Instance.taskCompleteClip : null);
        if (clip != null && SFXManager.Instance != null)
        {
            SFXManager.Instance.PlayUI(clip);
        }
    }
}
