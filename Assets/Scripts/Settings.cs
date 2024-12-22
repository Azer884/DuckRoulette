using System.Collections.Generic;
using UnityEngine;

public class Settings : MonoBehaviour
{
    [SerializeField] private List<GameObject> menus;
    [SerializeField] private GameObject friends;
    private List<bool> isMenusActivated = new();
    private bool isFriendsActive;
    private Animator animator;
    private void Awake() 
    {
        animator = GetComponent<Animator>();
    }

    public void OnSettingsClick()
    {
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
        for (int i = 0; i < menus.Count; i++)
        {
            menus[i].SetActive(isMenusActivated[i]);
        }
        friends.SetActive(isFriendsActive);
        
        if (menus[3].activeSelf)
        {
            friends.GetComponent<Animator>().Play("FriendListOtherWay");
        }
        
        else if (menus[0].activeSelf)
        {
            animator.Play("SettingsAndOthers");
        }
        else
        {
            animator.Play("ExiitToSettings");
        }
    }
}
