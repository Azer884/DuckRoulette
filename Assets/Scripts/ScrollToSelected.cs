using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class ScrollToSelected : MonoBehaviour
{
    public ScrollRect scrollRect; // Reference to the ScrollRect component
    public RectTransform content; // Reference to the content of the ScrollRect

    public void ScrollTo(RectTransform target)
    {
        // Get the viewport of the ScrollRect
        RectTransform viewport = scrollRect.viewport;

        // Calculate the world position of the target relative to the viewport
        Vector3[] targetCorners = new Vector3[4];
        target.GetWorldCorners(targetCorners);

        Vector3[] viewportCorners = new Vector3[4];
        viewport.GetWorldCorners(viewportCorners);

        float viewportHeight = viewportCorners[2].y - viewportCorners[0].y;
        float targetCenterY = (targetCorners[1].y + targetCorners[0].y) / 2f;

        float viewportCenterY = (viewportCorners[1].y + viewportCorners[0].y) / 2f;

        float offsetY = targetCenterY - viewportCenterY;

        // Calculate normalized scroll position
        float contentHeight = content.rect.height - viewportHeight;
        float normalizedPosition = 1 - Mathf.Clamp01((scrollRect.content.anchoredPosition.y + offsetY) / contentHeight);

        // Set the normalized vertical position
        scrollRect.verticalNormalizedPosition = normalizedPosition;
    }
}
