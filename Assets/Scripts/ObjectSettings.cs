using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ObjectSettings : MonoBehaviour
{
    [SerializeField] private string section, key;
    private Slider slider;
    private TMP_Dropdown dropdown;
    private Toggle toggle;

    void Awake()
    {
        StartCoroutine(Wait());
    }

    private IEnumerator Wait()
    {
        yield return SettingsManager.Instance;

        if (TryGetComponent(out slider))
        {
            SettingsManager.Instance.LoadSlider(slider, section, key);
    
            slider.onValueChanged.AddListener(value => SettingsManager.Instance.SaveSlider(slider, section, key));
        }
        else if (TryGetComponent(out dropdown))
        {
            SettingsManager.Instance.LoadDropdown(dropdown, section, key);
    
            dropdown.onValueChanged.AddListener(value => SettingsManager.Instance.SaveDropdown(dropdown, section, key));
        }
        else if (TryGetComponent(out toggle))
        {
            SettingsManager.Instance.LoadToggle(toggle, section, key);
    
            toggle.onValueChanged.AddListener(value => SettingsManager.Instance.SaveToggle(toggle, section, key));
        }
    }
}
