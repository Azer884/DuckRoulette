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

    private bool hooked;

    // Hooking up used to start from Awake, but a coroutine is killed for good when its GameObject
    // is deactivated - it does not resume on re-activation. SettingsVisibility activates and
    // deactivates the in-match settings panel on the same frame, so for whichever tab is active by
    // default (the Game tab) Awake fired, the wait for SettingsManager was killed mid-yield, and
    // that tab's controls silently never loaded or saved for the rest of the session. Starting
    // from OnEnable instead means the hookup simply retries the next time the panel is opened.
    private void OnEnable()
    {
        if (hooked)
        {
            return;
        }

        StartCoroutine(Wait());
    }

    private IEnumerator Wait()
    {
        yield return new WaitUntil(() => SettingsManager.Instance != null);

        // Guards against a second pass double-subscribing onValueChanged if this object gets
        // enabled again before the first wait completed.
        if (hooked)
        {
            yield break;
        }

        hooked = true;

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
