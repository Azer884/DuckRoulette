using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class Settings : MonoBehaviour
{
    [SerializeField] private List<GameObject> menus;
    [SerializeField] private GameObject friends, settingsMenu;
    private List<bool> isMenusActivated = new();
    private bool isFriendsActive;
    private Animator animator;

    private InputAction returnAction;

    private void Awake()
    {
        animator = GetComponent<Animator>();
        if (animator == null)
        {
            Debug.LogError("Settings: Animator component not found on this GameObject!");
        }
    }

    private void OnEnable()
    {
        returnAction = RebindSaveLoad.Instance != null ? RebindSaveLoad.Instance.actions.FindAction("Return") : null;
        if (returnAction != null)
        {
            returnAction.performed += HandleReturn;
        }
    }

    private void OnDisable()
    {
        if (returnAction != null)
        {
            returnAction.performed -= HandleReturn;
        }
    }

    // "Return" also carries a Keyboard/Escape binding, which PauseMenu's own "Pause" action
    // already handles - only react to the gamepad B press here so the two don't double-fire.
    private void HandleReturn(InputAction.CallbackContext context)
    {
        if (context.control?.device is Gamepad && settingsMenu != null && settingsMenu.activeSelf)
        {
            OnReturnClick();
        }
    }

    public void OnClick()
    {
        if (!settingsMenu.activeSelf)
        {
            OnSettingsClick();
        }
        else
        {
            OnReturnClick();
        }
    }

    public void OnSettingsClick()
    {
        if (animator == null)
        {
            Debug.LogError("Settings: Animator is null!");
            return;
        }

        settingsMenu.SetActive(true);

        animator.ResetTrigger("OnSettings"); // Reset any existing trigger
        if (menus.Count > 0 && menus[0].activeSelf)
        {
            animator.Play("Settings");
            animator.SetTrigger("OnSettings");
        }
        else
        {
            animator.Play("Exit");
        }

        isMenusActivated = new List<bool>(new bool[menus.Count]);
        for (int i = 0; i < menus.Count; i++)
        {
            isMenusActivated[i] = menus[i].activeSelf;
            menus[i].SetActive(false);
        }

        isFriendsActive = friends.activeSelf;
        friends.SetActive(false);
    }

    public void OnReturnClick()
    {
        settingsMenu.SetActive(false);

        for (int i = 0; i < menus.Count; i++)
        {
            menus[i].SetActive(isMenusActivated[i]);
        }
        friends.SetActive(isFriendsActive);

        // Check if menus list has enough elements before accessing index 3
        if (menus.Count > 3 && menus[3].activeSelf)
        {
            friends.GetComponent<Animator>().Play("FriendListOtherWay");
        }
        
        if (menus.Count > 0 && menus[0].activeSelf)
        {
            if (animator != null)
            {
                animator.Play("SettingsAndOthers");
            }
        }
        else
        {
            if (animator != null)
            {
                animator.Play("ExiitToSettings");
            }
        }
    }

}
