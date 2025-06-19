using UnityEngine;

public class LoadTuto : MonoBehaviour
{
    public void LoadTutorial()
    {
        UnityEngine.SceneManagement.SceneManager.LoadScene("Tutorial");
    }
}
