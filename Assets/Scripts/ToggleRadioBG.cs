using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ToggleRadioBG : MonoBehaviour
{
    [SerializeField] private Toggle toggle;
    [SerializeField] private Image background;
    [SerializeField] private GameObject dot;
    [SerializeField] private Sprite checkedSprite;
    [SerializeField] private Sprite uncheckedSprite;
    [SerializeField] private TMP_Text label;
    [SerializeField] private Color checkedColor = new Color(1f, 0.5568628f, 0.023529412f, 1f);
    [SerializeField] private Color uncheckedColor = new Color(0.9450981f, 0.95294124f, 0.91372555f, 1f);

    private void OnEnable()
    {
        toggle.onValueChanged.AddListener(SetState);
        SetState(toggle.isOn);
    }

    private void OnDisable()
    {
        toggle.onValueChanged.RemoveListener(SetState);
    }

    private void SetState(bool isOn)
    {
        background.sprite = isOn ? checkedSprite : uncheckedSprite;
        if (dot != null)
            dot.SetActive(isOn);
        if (label != null)
            label.color = isOn ? checkedColor : uncheckedColor;
    }
}
