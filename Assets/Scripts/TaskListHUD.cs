using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

// The local player's task - the Among Us checklist in the corner. Only players who were caught
// camping have one (see TaskManager), so for everyone else the panel never appears. While the task
// is open a warning line says the gun is off the table; once it is done the row plays its
// completion pop and the whole panel tucks itself away.
//
// All visuals live on Assets/Prefabs/Ui/TaskListHUD.prefab: drop that prefab into the game scene
// once and edit colors/layout/fonts there like any other UI, the same way ShotClockUI works. This
// script only drives it.
//
// The panel's show/hide motion is left to an optional Animator (panelAnimator, Show/Hide
// triggers). The small reactions - a row's completion pop, the header punch, the '!' flash - are
// still script-driven coroutines, keyed off what actually changed since the last refresh.
public class TaskListHUD : MonoBehaviour
{
    [SerializeField] private GameObject root;
    [SerializeField, Tooltip("The '!' icon that leads the panel's entrance and flashes on a new " +
        "task or an unfinished-task warning. Its RectTransform is what slides in from the right.")]
    private Image warningIcon;
    [SerializeField, Tooltip("The panel's background image, directly under root - faded in once " +
        "the '!' icon has finished sliding in.")]
    private Image panelBackground;
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
    [SerializeField, Tooltip("Seconds the finished task stays on screen, so its completion pop is " +
        "seen, before the panel hides.")]
    private float hideAfterCompleteDelay = 1.6f;
    // Comic Sans MS SDF is a dynamic atlas over the Comic Sans MS TTF, which covers WGL4 - so the
    // circles resolve, while the old U+2713 checkmark did not and drew as a missing-glyph box.
    [SerializeField] private string openMarker = "○";
    [SerializeField] private string completedMarker = "●";
    [SerializeField, Tooltip("Strike completed rows through as well as recolouring them.")]
    private bool strikeCompleted = true;

    [Header("Animation")]
    [SerializeField, Tooltip("Turn every animation below off and snap straight to the new state.")]
    private bool animate = true;

    [Header("Animation - panel show / hide")]
    [SerializeField, Tooltip("Optional Animator that plays the panel's show and hide animations. " +
        "Left empty, the panel just appears and disappears.")]
    private Animator panelAnimator;
    [SerializeField, Tooltip("Trigger set on Panel Animator when the panel appears.")]
    private string showTrigger = "Show";
    [SerializeField, Tooltip("Trigger set on Panel Animator when the panel goes away.")]
    private string hideTrigger = "Hide";
    [SerializeField, Tooltip("Seconds the hide animation needs before the panel is switched off. " +
        "0 = switch off straight away.")]
    private float hideAnimationDuration = 0f;

    [Header("Animation - task completed")]
    [SerializeField] private float completePunchScale = 1.14f;
    [SerializeField] private float completePunchDuration = 0.3f;
    [SerializeField, Tooltip("Colour the row flashes through on completion before settling on Completed Color.")]
    private Color completeFlashColor = Color.white;

    [Header("Animation - header counter")]
    [SerializeField] private float headerPunchScale = 1.12f;
    [SerializeField] private float headerPunchDuration = 0.22f;

    [Header("Animation - '!' icon attention flash")]
    [SerializeField, Tooltip("Colour the '!' icon flashes through - on a brand new task, and on " +
        "the unfinished-task warning at round end.")]
    private Color iconAlertColor = new(1f, 0.2f, 0.15f, 1f);
    [SerializeField] private float iconFlashDuration = 0.5f;
    [SerializeField] private float iconFlashScale = 1.35f;
    [SerializeField, Tooltip("SFX played alongside the '!' flash, for a new task and for the " +
        "unfinished-task warning alike.")]
    private AudioClip attentionClipOverride;

    private readonly List<TaskManager.TaskEntry> localTasks = new();
    private readonly List<GameObject> spawnedRows = new();
    private TaskManager subscribedManager;

    // Animation bookkeeping. completionState is keyed by task index rather than by row position, so
    // a reordered list can't read as "everything just completed".
    private readonly Dictionary<int, bool> completionState = new();
    // Tracks TaskEntry.RoundsOpen per task index, so a round ending with the task still open (the
    // server increments RoundsOpen on every gun hand-off - see TaskManager.OnGunHandedOff) can be
    // told apart from any other refresh.
    private readonly Dictionary<int, int> roundsOpenState = new();
    private readonly Dictionary<GameObject, Coroutine> rowRoutines = new();
    private readonly List<GameObject> rowsEnteringThisRefresh = new();
    private Coroutine hideAnimationRoutine;
    private Coroutine headerRoutine;
    private Coroutine hideRoutine;
    private Coroutine iconFlashRoutine;

    private CanvasGroup panelGroup;
    private RectTransform panelRect;
    private RectTransform iconRect;
    private CanvasGroup iconGroup;
    private CanvasGroup backgroundGroup;
    private Vector2 panelBasePosition;
    private Vector2 iconBasePosition;
    private Color iconBaseColor = Color.white;
    private bool panelBaseCaptured;
    private string lastHeaderText;

    private void Awake()
    {
        if (rowTemplate != null)
        {
            rowTemplate.SetActive(false);
        }

        if (root != null)
        {
            CachePanelRefs();
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
                // A new match means a new checklist - forget last round's completion/age state so
                // the first refresh seeds quietly instead of popping/warning for work from before.
                completionState.Clear();
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

            // Only a task this HUD has already seen as open can "just complete". The first sighting
            // of a task seeds the state silently, so opening the list mid-round doesn't fire a burst
            // of pops for work that was finished minutes ago.
            bool knownCompletion = completionState.TryGetValue(entry.TaskIndex, out bool previouslyCompleted);
            bool justCompleted = knownCompletion && !previouslyCompleted && entry.Completed;
            completionState[entry.TaskIndex] = entry.Completed;

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

            // A completion pop during the panel's own entrance would fight the row intro.
            if (justCompleted && animate && panelWasVisible)
            {
                PlayRowCompleted(row);
            }
        }

        UpdateHeader(localTasks, panelWasVisible);

        if (warningText != null)
        {
            warningText.text = warningMessage;
            warningText.gameObject.SetActive(!allDone);
        }

        if (allDone && hideRoutine == null)
        {
            hideRoutine = StartCoroutine(HideAfterComplete());
        }

        foreach (GameObject row in rowsEnteringThisRefresh)
        {
            ResetRowVisualState(row);
        }

        if (!panelWasVisible)
        {
            ShowPanel();
        }
        else
        {
            // The panel was already up, so a brand new task slotted into an existing list, or a
            // round ending with a task still undone, both deserve the same "look here" flash - not
            // the one the panel's own first appearance already got from ShowPanel.
            if (animate && (anyNewTask || anyRoundMissed))
            {
                PlayIconAlert();
            }
        }

        rowsEnteringThisRefresh.Clear();
    }

    private IEnumerator HideAfterComplete()
    {
        yield return new WaitForSeconds(animate ? hideAfterCompleteDelay : 0f);
        hideRoutine = null;
        HidePanel();
    }

    private void UpdateHeader(List<TaskManager.TaskEntry> tasks, bool panelWasVisible)
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
        bool changed = panelWasVisible && lastHeaderText != null && lastHeaderText != text;

        headerText.text = text;
        lastHeaderText = text;

        if (changed && animate)
        {
            if (headerRoutine != null)
            {
                StopCoroutine(headerRoutine);
            }
            headerRoutine = StartCoroutine(PunchScale(headerText.rectTransform, headerPunchScale, headerPunchDuration));
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
            GameObject row = spawnedRows[i];
            bool shouldBeActive = i < wanted;

            // A pooled row coming back into use gets the same entrance as a brand new one.
            if (shouldBeActive && !row.activeSelf)
            {
                rowsEnteringThisRefresh.Add(row);
            }
            else if (!shouldBeActive && row.activeSelf)
            {
                StopRowRoutine(row);
                ResetRowVisualState(row);
            }

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
    // Animation
    // -------------------------------------------------------------------------------------------

    private void CachePanelRefs()
    {
        if (panelRect == null)
        {
            panelRect = root.transform as RectTransform;
        }

        if (panelGroup == null)
        {
            panelGroup = root.GetComponent<CanvasGroup>();
            if (panelGroup == null)
            {
                panelGroup = root.AddComponent<CanvasGroup>();
                // Purely a readout - it must never swallow a click meant for the world or another
                // HUD element on the same canvas.
                panelGroup.interactable = false;
                panelGroup.blocksRaycasts = false;
            }
        }

        if (iconRect == null && warningIcon != null)
        {
            iconRect = warningIcon.rectTransform;
            iconGroup = GetOrAddGroup(warningIcon.gameObject);
            iconBaseColor = warningIcon.color;
        }

        if (backgroundGroup == null && panelBackground != null)
        {
            backgroundGroup = GetOrAddGroup(panelBackground.gameObject);
        }

        // The panel's authored position is the target of the slide, so it has to be captured before
        // the first intro moves it.
        if (!panelBaseCaptured)
        {
            if (panelRect != null)
            {
                panelBasePosition = panelRect.anchoredPosition;
            }
            if (iconRect != null)
            {
                iconBasePosition = iconRect.anchoredPosition;
            }
            panelBaseCaptured = true;
        }
    }

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
            if (!root.activeSelf)
            {
                lastHeaderText = null;
            }
            return;
        }

        if (panelAnimator != null && !string.IsNullOrEmpty(hideTrigger) && hideAnimationDuration > 0f &&
            panelAnimator.isActiveAndEnabled)
        {
            panelAnimator.SetTrigger(hideTrigger);
            hideAnimationRoutine = StartCoroutine(HideAfterAnimation());
            return;
        }

        SnapHidePanel();
    }

    private IEnumerator HideAfterAnimation()
    {
        yield return new WaitForSeconds(hideAnimationDuration);
        hideAnimationRoutine = null;
        SnapHidePanel();
    }

    private void SnapHidePanel()
    {
        if (root.activeSelf)
        {
            StopAllRowRoutines();

            if (iconFlashRoutine != null)
            {
                StopCoroutine(iconFlashRoutine);
                iconFlashRoutine = null;
            }

            foreach (GameObject row in spawnedRows)
            {
                ResetRowVisualState(row);
            }

            CachePanelRefs();
            if (panelGroup != null)
            {
                panelGroup.alpha = 1f;
            }
            if (iconGroup != null)
            {
                iconGroup.alpha = 1f;
            }
            if (backgroundGroup != null)
            {
                backgroundGroup.alpha = 1f;
            }
            if (iconRect != null && panelBaseCaptured)
            {
                iconRect.anchoredPosition = iconBasePosition;
                iconRect.localScale = Vector3.one;
            }
            if (warningIcon != null)
            {
                warningIcon.color = iconBaseColor;
            }
            if (panelRect != null && panelBaseCaptured)
            {
                panelRect.anchoredPosition = panelBasePosition;
            }

            root.SetActive(false);
        }

        // Next time it appears it should play its entrance again, and the header count should not
        // punch just because it changed while hidden.
        lastHeaderText = null;
    }

    // The panel snaps to its authored layout; any entrance motion is Panel Animator's "Show".
    private void ShowPanel()
    {
        CachePanelRefs();

        if (panelGroup != null) panelGroup.alpha = 1f;
        if (iconGroup != null) iconGroup.alpha = 1f;
        if (backgroundGroup != null) backgroundGroup.alpha = 1f;
        if (panelRect != null && panelBaseCaptured) panelRect.anchoredPosition = panelBasePosition;
        if (iconRect != null && panelBaseCaptured) iconRect.anchoredPosition = iconBasePosition;

        foreach (GameObject row in spawnedRows)
        {
            ResetRowVisualState(row);
        }

        if (panelAnimator != null && !string.IsNullOrEmpty(showTrigger) && panelAnimator.isActiveAndEnabled)
        {
            if (!string.IsNullOrEmpty(hideTrigger))
            {
                panelAnimator.ResetTrigger(hideTrigger);
            }
            panelAnimator.SetTrigger(showTrigger);
        }

        // A brand new task still gets the '!' attention flash.
        if (animate)
        {
            PlayIconAlert();
        }
    }

    // The "!" icon flashing red and scaling up - on a brand new task appearing, and again on the
    // "you missed a round" warning (Refresh's anyRoundMissed). Same visual both times; only the
    // trigger differs, so there is one attention cue to associate with "the task list wants you".
    private void PlayIconAlert()
    {
        if (iconRect == null || warningIcon == null)
        {
            return;
        }

        if (iconFlashRoutine != null)
        {
            StopCoroutine(iconFlashRoutine);
        }
        iconFlashRoutine = StartCoroutine(IconAlertFlash());

        AudioClip clip = attentionClipOverride != null
            ? attentionClipOverride
            : (SFXManager.Instance != null ? SFXManager.Instance.taskCompleteClip : null);
        if (clip != null && SFXManager.Instance != null)
        {
            SFXManager.Instance.PlayUI(clip);
        }
    }

    private IEnumerator IconAlertFlash()
    {
        float t = 0f;
        while (t < iconFlashDuration)
        {
            if (root == null || !root.activeSelf)
            {
                break;
            }

            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / iconFlashDuration);
            // One rise-and-settle arc, same shape as the row completion punch: quick flash to the
            // alert colour and a scale bump, easing back to rest.
            float punch = Mathf.Sin(k * Mathf.PI);

            warningIcon.color = Color.LerpUnclamped(iconBaseColor, iconAlertColor, punch);
            iconRect.localScale = Vector3.one * Mathf.LerpUnclamped(1f, iconFlashScale, punch);

            yield return null;
        }

        warningIcon.color = iconBaseColor;
        iconRect.localScale = Vector3.one;
        iconFlashRoutine = null;
    }

    private void PlayRowCompleted(GameObject row)
    {
        if (row == null || !row.activeSelf)
        {
            return;
        }

        StopRowRoutine(row);
        rowRoutines[row] = StartCoroutine(RowCompleted(row));
    }

    private IEnumerator RowCompleted(GameObject row)
    {
        RectTransform rect = row.transform as RectTransform;
        RowRefs refs = GetRowRefs(row);

        CanvasGroup group = GetRowGroup(row);
        group.alpha = 1f;

        float t = 0f;
        while (t < completePunchDuration)
        {
            if (root == null || !root.activeSelf)
            {
                break;
            }

            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / completePunchDuration);

            // One rise-and-settle arc: sin gives the overshoot and the return from a single curve
            // instead of two chained lerps.
            float punch = Mathf.Sin(k * Mathf.PI);
            rect.localScale = Vector3.one * Mathf.LerpUnclamped(1f, completePunchScale, punch);

            if (refs.title != null)
            {
                refs.title.color = Color.LerpUnclamped(completedColor, completeFlashColor, punch);
            }
            if (refs.description != null)
            {
                refs.description.color = Color.LerpUnclamped(completedColor, completeFlashColor, punch);
            }

            yield return null;
        }

        rect.localScale = Vector3.one;
        if (refs.title != null)
        {
            refs.title.color = completedColor;
        }
        if (refs.description != null)
        {
            refs.description.color = completedColor;
        }
        rowRoutines.Remove(row);
    }

    private IEnumerator PunchScale(RectTransform target, float scale, float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            if (root == null || !root.activeSelf)
            {
                break;
            }

            t += Time.deltaTime;
            float punch = Mathf.Sin(Mathf.Clamp01(t / duration) * Mathf.PI);
            target.localScale = Vector3.one * Mathf.LerpUnclamped(1f, scale, punch);
            yield return null;
        }

        target.localScale = Vector3.one;
        headerRoutine = null;
    }

    // Rows are scaled and faded, so each one needs its own CanvasGroup. Added on demand rather than
    // authored on the template, so the script still works against an older prefab.
    private CanvasGroup GetRowGroup(GameObject row)
    {
        return GetOrAddGroup(row);
    }

    private static CanvasGroup GetOrAddGroup(GameObject target)
    {
        CanvasGroup group = target.GetComponent<CanvasGroup>();
        if (group == null)
        {
            group = target.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;
        }
        return group;
    }

    private void StopRowRoutine(GameObject row)
    {
        if (row != null && rowRoutines.TryGetValue(row, out Coroutine routine))
        {
            if (routine != null)
            {
                StopCoroutine(routine);
            }
            rowRoutines.Remove(row);
        }
    }

    private void StopAllRowRoutines()
    {
        foreach (KeyValuePair<GameObject, Coroutine> entry in rowRoutines)
        {
            if (entry.Value != null)
            {
                StopCoroutine(entry.Value);
            }
        }
        rowRoutines.Clear();
    }

    // A pooled row must go back to full alpha and unit scale, or it gets reused mid-punch.
    private void ResetRowVisualState(GameObject row)
    {
        if (row == null)
        {
            return;
        }

        row.transform.localScale = Vector3.one;

        CanvasGroup group = row.GetComponent<CanvasGroup>();
        if (group != null)
        {
            group.alpha = 1f;
        }
    }
}
