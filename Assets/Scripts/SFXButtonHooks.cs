using UnityEngine;
using UnityEngine.EventSystems;

// Auto-attached by SFXManager.RegisterButton to every Button so hover/select also get sound
// without hand-wiring each one - click uses Button.onClick directly instead of this.
public class SFXButtonHooks : MonoBehaviour, IPointerEnterHandler, ISelectHandler
{
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (SFXManager.Instance != null) SFXManager.Instance.PlayButtonHover();
    }

    public void OnSelect(BaseEventData eventData)
    {
        if (SFXManager.Instance != null) SFXManager.Instance.PlayButtonSelect();
    }
}
