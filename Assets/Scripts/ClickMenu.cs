using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public class ClickMenu : MonoBehaviour
{
    public static ClickMenu Instance { get; private set; } = null;
    public TextMeshProUGUI playerName;
    public GameObject kickButton;

    // Start is called before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }
        gameObject.SetActive(false);
    }

    private void OnEnable() {
        kickButton.SetActive(LobbyManager.instance.isHost);
    }

    public bool IsPointerOverUIObject(GameObject clickObj)
    {
        // Get all objects under the cursor
        var pointerEventData = new PointerEventData(EventSystem.current);
        pointerEventData.position = Input.mousePosition;

        var raycastResults = new System.Collections.Generic.List<RaycastResult>();
        EventSystem.current.RaycastAll(pointerEventData, raycastResults);

        foreach (var result in raycastResults)
        {
            // Check if the clicked object is part of ClickMenu or the activating button
            if (result.gameObject == clickObj || result.gameObject == gameObject)
            {
                return true;
            }
        }

        return false;
    }
}
