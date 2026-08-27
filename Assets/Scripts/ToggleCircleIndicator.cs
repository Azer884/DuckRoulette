using UnityEngine;
using UnityEngine.UI;

public class ToggleCircleIndicator : MonoBehaviour
{
    [SerializeField] private Toggle toggle;
    [SerializeField] private Image dot;
    [SerializeField] private Color onColor = new Color(1f, 0.5568628f, 0.023529412f, 1f);
    [SerializeField] private Color offColor = Color.white;

    private void OnEnable()
    {
        toggle.onValueChanged.AddListener(SetColor);
        SetColor(toggle.isOn);
    }

    private void OnDisable()
    {
        toggle.onValueChanged.RemoveListener(SetColor);
    }

    private void SetColor(bool isOn)
    {
        dot.color = isOn ? onColor : offColor;
    }
}
