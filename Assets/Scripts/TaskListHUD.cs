using System.Collections;
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
//
// Movement is script-driven coroutines rather than an Animator, matching ParryFeedbackHUD and
// SpectateHUD. The list is rebuilt wholesale from replicated state at arbitrary moments, so the
// animation is keyed off what actually changed since the last refresh rather than off a clip.
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
    // Comic Sans MS SDF is a dynamic atlas over the Comic Sans MS TTF, which covers WGL4 - so the
    // circles resolve, while the old U+2713 checkmark did not and drew as a missing-glyph box.
    [SerializeField] private string openMarker = "○";
    [SerializeField] private string completedMarker = "●";
    [SerializeField, Tooltip("Strike completed rows through as well as recolouring them.")]
    private bool strikeCompleted = true;

    [Header("Animation")]
    [SerializeField, Tooltip("Turn every animation below off and snap straight to the new state.")]
    private bool animate = true;

    [Header("Animation - panel appearing")]
    [SerializeField] private float panelFadeDuration = 0.22f;
    [SerializeField, Tooltip("How far left the panel slides in from, in reference pixels.")]
    private float panelSlideDistance = 26f;

    [Header("Animation - rows appearing")]
    [SerializeField] private float rowFadeDuration = 0.2f;
    [SerializeField, Tooltip("Delay between each row's entrance, so the list deals itself out.")]
    private float rowStagger = 0.05f;
    [SerializeField, Range(0.5f, 1f)] private float rowIntroScale = 0.92f;

    [Header("Animation - task completed")]
    [SerializeField] private float completePunchScale = 1.14f;
    [SerializeField] private float completePunchDuration = 0.3f;
    [SerializeField, Tooltip("Colour the row flashes through on completion before settling on Completed Color.")]
    private Color completeFlashColor = Color.white;

    [Header("Animation - header counter")]
    [SerializeField] private float headerPunchScale = 1.12f;
    [SerializeField] private float headerPunchDuration = 0.22f;

    private readonly List<TaskManager.TaskEntry> localTasks = new();
    private readonly List<GameObject> spawnedRows = new();
    private TaskManager subscribedManager;

    // Animation bookkeeping. completionState is keyed by task index rather than by row position, so
    // a reordered list can't read as "everything just completed".
    private readonly Dictionary<int, bool> completionState = new();
    private readonly Dictionary<GameObject, Coroutine> rowRoutines = new();
    private readonly List<GameObject> rowsEnteringThisRefresh = new();
    private Coroutine panelRoutine;
    private Coroutine headerRoutine;

    private CanvasGroup panelGroup;
    private RectTransform panelRect;
    private Vector2 panelBasePosition;
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
                // A new match means a new checklist - forget last round's completion state so the
                // first refresh seeds quietly instead of popping every already-done task.
                completionState.Clear();
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

            GameObject row = spawnedRows[i];

            // Only a task this HUD has already seen as open can "just complete". The first sighting
            // of a task seeds the state silently, so opening the list mid-round doesn't fire a burst
            // of pops for work that was finished minutes ago.
            bool known = completionState.TryGetValue(entry.TaskIndex, out bool previouslyCompleted);
            bool justCompleted = known && !previouslyCompleted && entry.Completed;
            completionState[entry.TaskIndex] = entry.Completed;

            ApplyRow(row, task, entry.Completed);

            // A completion pop during the panel's own entrance would fight the row intro.
            if (justCompleted && animate && panelWasVisible)
            {
                PlayRowCompleted(row);
            }
        }

        UpdateHeader(completed, localTasks.Count, panelWasVisible);

        if (!panelWasVisible)
        {
            PlayPanelIntro();
        }
        else if (rowsEnteringThisRefresh.Count > 0)
        {
            PlayRowIntros(rowsEnteringThisRefresh, 0f);
        }

        rowsEnteringThisRefresh.Clear();
    }

    private void UpdateHeader(int completed, int total, bool panelWasVisible)
    {
        if (headerText == null)
        {
            return;
        }

        string text = string.Format(headerFormat, completed, total);
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

        TextMeshProUGUI label = row.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null)
        {
            string marker = isCompleted ? completedMarker : openMarker;
            // A stale index (the tasks array was edited mid-match) shows as a plain "?" row
            // instead of blanking out or throwing.
            string name = task != null ? task.DisplayName : "?";
            string description = task != null ? task.taskDiscription : string.Empty;

            string body = string.IsNullOrWhiteSpace(description) ? name : $"{name} - {description}";
            if (isCompleted && strikeCompleted)
            {
                body = $"<s>{body}</s>";
            }

            label.text = $"{marker}  {body}";
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

        // The panel's authored position is the target of the slide, so it has to be captured before
        // the first intro moves it.
        if (!panelBaseCaptured && panelRect != null)
        {
            panelBasePosition = panelRect.anchoredPosition;
            panelBaseCaptured = true;
        }
    }

    private void HidePanel()
    {
        if (root.activeSelf)
        {
            // Snap rather than fade out: the panel hides when the player dies or the round resets,
            // and a lingering ghost of a list that no longer applies reads worse than a clean cut.
            StopAllRowRoutines();

            if (panelRoutine != null)
            {
                StopCoroutine(panelRoutine);
                panelRoutine = null;
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

    private void PlayPanelIntro()
    {
        CachePanelRefs();

        if (!animate)
        {
            if (panelGroup != null)
            {
                panelGroup.alpha = 1f;
            }
            if (panelRect != null && panelBaseCaptured)
            {
                panelRect.anchoredPosition = panelBasePosition;
            }

            foreach (GameObject row in spawnedRows)
            {
                ResetRowVisualState(row);
            }
            return;
        }

        if (panelRoutine != null)
        {
            StopCoroutine(panelRoutine);
        }
        panelRoutine = StartCoroutine(PanelIntro());

        // Rows trail the panel, so the box arrives first and then fills itself in.
        PlayRowIntros(spawnedRows, panelFadeDuration * 0.5f, localTasks.Count);
    }

    private IEnumerator PanelIntro()
    {
        Vector2 from = panelBasePosition + new Vector2(-panelSlideDistance, 0f);
        float t = 0f;

        panelGroup.alpha = 0f;
        panelRect.anchoredPosition = from;

        while (t < panelFadeDuration)
        {
            t += Time.deltaTime;
            float k = EaseOut(Mathf.Clamp01(t / panelFadeDuration));
            panelGroup.alpha = k;
            panelRect.anchoredPosition = Vector2.LerpUnclamped(from, panelBasePosition, k);
            yield return null;
        }

        panelGroup.alpha = 1f;
        panelRect.anchoredPosition = panelBasePosition;
        panelRoutine = null;
    }

    private void PlayRowIntros(List<GameObject> rows, float delay, int limit = int.MaxValue)
    {
        int staggerIndex = 0;
        for (int i = 0; i < rows.Count && i < limit; i++)
        {
            GameObject row = rows[i];
            if (row == null || !row.activeSelf)
            {
                continue;
            }

            StopRowRoutine(row);

            if (!animate)
            {
                ResetRowVisualState(row);
                continue;
            }

            rowRoutines[row] = StartCoroutine(RowIntro(row, delay + staggerIndex * rowStagger));
            staggerIndex++;
        }
    }

    private IEnumerator RowIntro(GameObject row, float delay)
    {
        CanvasGroup group = GetRowGroup(row);
        RectTransform rect = row.transform as RectTransform;

        group.alpha = 0f;
        rect.localScale = Vector3.one * rowIntroScale;

        if (delay > 0f)
        {
            yield return new WaitForSeconds(delay);
        }

        float t = 0f;
        while (t < rowFadeDuration)
        {
            // The panel can be switched off mid-entrance (death, settings toggle) - bail rather
            // than keep driving a row nobody is looking at.
            if (root == null || !root.activeSelf)
            {
                break;
            }

            t += Time.deltaTime;
            float k = EaseOut(Mathf.Clamp01(t / rowFadeDuration));
            group.alpha = k;
            rect.localScale = Vector3.LerpUnclamped(Vector3.one * rowIntroScale, Vector3.one, k);
            yield return null;
        }

        group.alpha = 1f;
        rect.localScale = Vector3.one;
        rowRoutines.Remove(row);
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
        TextMeshProUGUI label = row.GetComponentInChildren<TextMeshProUGUI>(true);

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

            if (label != null)
            {
                label.color = Color.LerpUnclamped(completedColor, completeFlashColor, punch);
            }

            yield return null;
        }

        rect.localScale = Vector3.one;
        if (label != null)
        {
            label.color = completedColor;
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
        CanvasGroup group = row.GetComponent<CanvasGroup>();
        if (group == null)
        {
            group = row.AddComponent<CanvasGroup>();
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

    private static float EaseOut(float k)
    {
        // Cubic ease-out: quick off the mark, settles softly. Cheap, and matches the snappy feel of
        // the other HUD pops.
        float inv = 1f - k;
        return 1f - inv * inv * inv;
    }
}
