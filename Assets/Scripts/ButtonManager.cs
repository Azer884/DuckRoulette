using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class ButtonManager : MonoBehaviour
{

    [SerializeField] private float selectedScale = 1.1f; // Scale multiplier for selected state
    [SerializeField] private float animationDuration = 0.2f;
    private Vector3 originalScale;
    [SerializeField] private bool isSlider;

    private void Awake()
    {
        originalScale = transform.localScale;

        // Ensure the button has an EventTrigger
        if (TryGetComponent(out EventTrigger eventTrigger) 
            && (TryGetComponent(out Button _) || TryGetComponent(out Toggle _) || TryGetComponent(out TMP_Dropdown _) || TryGetComponent(out Slider _)))
        {
            EventTrigger.Entry selectEntry = new()
            {
                eventID = EventTriggerType.Select
            };
            selectEntry.callback.AddListener((eventData) => OnSelect());
            eventTrigger.triggers.Add(selectEntry);

            // Add OnDeselect entry
            EventTrigger.Entry deselectEntry = new()
            {
                eventID = EventTriggerType.Deselect
            };
            deselectEntry.callback.AddListener((eventData) => OnDeselect());
            eventTrigger.triggers.Add(deselectEntry);
        }
    }

    private void OnSelect()
    {
        StopAllCoroutines();
        if (!isSlider)
        {
            StartCoroutine(ScaleTo(transform, originalScale * selectedScale));
        }
        else
        {
            StartCoroutine(ScaleTo(transform.parent, originalScale * selectedScale));
        }
    }

    private void OnDeselect()
    {
        StopAllCoroutines();
        if (!isSlider)
        {
            StartCoroutine(ScaleTo(transform, originalScale));
        }
        else
        {
            StartCoroutine(ScaleTo(transform.parent, originalScale));
        }
    }

    public void OnPointerEnter(BaseEventData eventData)
    {
        if (eventData is PointerEventData pointerEventData && !TryGetComponent(out TMP_InputField _))
        {
            pointerEventData.selectedObject = pointerEventData.pointerEnter;
        }
    }

    public void OnPointerExit(BaseEventData eventData)
    {
        if (eventData is PointerEventData pointerEventData)
        {
            pointerEventData.selectedObject = null;
        }
    }

    private System.Collections.IEnumerator ScaleTo(Transform obj, Vector3 targetScale)
    {
        Vector3 currentScale = obj.localScale;
        float elapsed = 0f;

        while (elapsed < animationDuration)
        {
            obj.localScale = Vector3.Lerp(currentScale, targetScale, elapsed / animationDuration);
            elapsed += Time.deltaTime;
            yield return null;
        }

        obj.localScale = targetScale;
    }
}
