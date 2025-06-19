using System.Collections;
using UnityEngine;
using Unity.Netcode;
using UnityEngine.SceneManagement;
using Steamworks;

public class LoadNextScene : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        Tutorial.Load();
        if (Tutorial.Data.hasCompletedTutorial)
        {
            StartCoroutine(LoadMainScene());
        }
        else SceneManager.LoadScene("Tutorial");
    }

    // Update is called once per frame
    IEnumerator LoadMainScene()
    {
        yield return new WaitUntil(() => NetworkManager.Singleton != null);
        if (SteamClient.IsValid)
        {
            SceneManager.LoadScene("Lobby");
        }
        else SceneManager.LoadScene("Error");
    }
}
