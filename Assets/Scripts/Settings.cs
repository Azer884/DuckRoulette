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
    private void Awake() 
    {
        animator = GetComponent<Animator>();
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
        settingsMenu.SetActive(true);

        animator.ResetTrigger("OnSettings"); // Reset any existing trigger
        if (menus[0].activeSelf)
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

        if (menus[3].activeSelf)
        {
            friends.GetComponent<Animator>().Play("FriendListOtherWay");
        }
        if (menus[0].activeSelf)
        {
            animator.Play("SettingsAndOthers");
        }
        else
        {
            animator.Play("ExiitToSettings");
        }
    }

}
