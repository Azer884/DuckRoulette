using UnityEngine;
using UnityEngine.SceneManagement;

// Shown once in the Lobby (see Assets/Prefabs/Ui/TutorialPromptDialog.prefab) instead of the
// old behavior of forcing a new player straight into the Tutorial scene. Lets them choose.
public class TutorialPromptDialog : MonoBehaviour
{
    [SerializeField] private GameObject panel;

    private void Start()
    {
        if (Tutorial.Data.hasCompletedTutorial)
        {
            panel.SetActive(false);
        }
    }

    public void PlayTutorial()
    {
        SceneManager.LoadScene("Tutorial");
    }

    public void Dismiss()
    {
        Tutorial.Data.hasCompletedTutorial = true;
        Tutorial.Save();
        panel.SetActive(false);
    }
}
