using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class PassScrollToParent : MonoBehaviour, IScrollHandler
{
    public void OnScroll(PointerEventData eventData)
    {
        // Find the parent ScrollRect and pass the scroll event to it
        ScrollRect parentScrollRect = GetComponentInParent<ScrollRect>();
        if (parentScrollRect != null)
        {
            parentScrollRect.OnScroll(eventData);
        }
    }
}
