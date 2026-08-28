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
        if (SteamClient.IsValid)
        {
            Tutorial.Load();
            StartCoroutine(LoadMainScene());
        }
        else SceneManager.LoadScene("Error");
    }

    // Update is called once per frame
    IEnumerator LoadMainScene()
    {
        yield return new WaitUntil(() => NetworkManager.Singleton != null);

        SceneManager.LoadScene("Lobby");

    }
}
