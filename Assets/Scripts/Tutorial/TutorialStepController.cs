using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class TutorialStepController : MonoBehaviour
{
    private int currentStep = 0;
    [SerializeField] private Color messageColor = Color.yellow;
    [SerializeField] private AudioSource audioSource;

    private void Start()
    {
        var tm = TutorialManager.Instance;

        tm.OnLook += Step_Looked;
        tm.OnMove += Step_Moved;
        tm.OnSprint += Step_Sprinted;
        tm.OnJump += Step_Jumped;
        tm.OnPickUp += Step_PickedUp;
        tm.OnThrow += Step_Thrown;
        tm.OnShutDown += Step_ShutDown;
        tm.OnCrouch += Step_Crouched;
        tm.OnSlide += Step_Slid;
        tm.OnSwitchToGun += Step_SwitchedToGun;
        tm.OnReload += Step_Reloaded;
        tm.OnTrigger += Step_Triggered;
        tm.OnGunShot += Step_GunShot;
        tm.OnTeamUp += Step_TeamUp;
        tm.OnTalk += Step_Talk;
        tm.OnEndTeamUp += Step_EndTeamUp;
        tm.OnSlap += Step_Slap;

        ShowStepMessage(0);
    }

    private void ShowStepMessage(int step)
    {
        int randomIndex = Random.Range(0, 5);
        if (randomIndex == 0)
        {
            audioSource.pitch = 1f + Random.Range(-0.2f, 0.2f);
            audioSource.Play();
        }

        string msg = step switch
        {
            0 => "Move your mouse to look around.",
            1 => "Use WASD to move.",
            2 => "Hold SHIFT to sprint.",
            3 => "You hear that sound? Follow it. Btw press SPACE to jump.",
            4 => "Press E to pick up the Boombox.",
            5 => "Press E again to throw it.",
            6 => "Press F to shut it down.",
            7 => "Press CONTROL to crouch.",
            8 => "Jump on ice to slide.",
            9 => "Scroll up to switch to gun.",
            10 => "Press R to reload.",
            11 => "Press RIGHT MOUSE to trigger.",
            12 => "Press LEFT MOUSE to shoot the doll (there is one bullet available in the chamber).",
            13 => "This is a TutoBot. He looks friendly. Get close and press E to dap him up.",
            14 => "Talk with your teammate dammit! Press V to talk.",
            15 => "You can end the team up and turn on each other. Press X to end this team up.",
            16 => "Press LEFT MOUSE to slap TutoBot. Knock him out!",
            _ => null
        };

        if (msg != null)
        {
            MessageBox.Informate(msg, messageColor, MessagePriority.High, 15f);
        }
        else
        {
            MessageBox.Informate("Tutorial complete! enjoy.", Color.green, MessagePriority.High, 6f);
            Tutorial.Data.hasCompletedTutorial = true;
            Tutorial.Save();
            StartCoroutine(LoadLobbyAfterDelay());
        }
    }

    private void AdvanceStep(int step, float delay = 0f)
    {
        StartCoroutine(DoAdvanceStep(step, delay));
    }

    private IEnumerator DoAdvanceStep(int step, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (currentStep != step) yield break;

        currentStep++;
        ShowStepMessage(currentStep);
    }

    // ------------------ Steps With Logic Space ------------------

    private void Step_Looked()
    {
        // e.g. Play door opening animation or sound
        Debug.Log("Step 0: Looked around");
        AdvanceStep(0, 1);
        TutorialEnvironment.Instance.OpenDoor(0);
    }

    private void Step_Moved()
    {
        // Unlock next area
        Debug.Log("Step 1: Moved");
        AdvanceStep(1, 2);
    }

    private void Step_Sprinted()
    {
        Debug.Log("Step 2: Sprinted");
        AdvanceStep(2, 2);
        TutorialEnvironment.Instance.OpenDoor(1);
    }

    private void Step_Jumped()
    {
        Debug.Log("Step 3: Jumped");
        AdvanceStep(3, 3);
    }

    private void Step_PickedUp()
    {
        Debug.Log("Step 4: Picked up");
        AdvanceStep(4, 1);
    }

    private void Step_Thrown()
    {
        Debug.Log("Step 5: Threw object");
        AdvanceStep(5, 1);
    }

    private void Step_ShutDown()
    {
        Debug.Log("Step 6: Shut down");
        AdvanceStep(6, 4);
        TutorialEnvironment.Instance.OpenDoor(2);
    }

    private void Step_Crouched()
    {
        Debug.Log("Step 7: Crouched");
        AdvanceStep(7, 4);
    }

    private void Step_Slid()
    {
        Debug.Log("Step 8: Slid");
        AdvanceStep(8, 3);
        TutorialEnvironment.Instance.OpenDoor(3);
        TutorialEnvironment.Instance.isTutoDollActive = true;
    }

    private void Step_SwitchedToGun()
    {
        Debug.Log("Step 9: Switched to gun");
        AdvanceStep(9, 1);
    }

    private void Step_Reloaded()
    {
        Debug.Log("Step 10: Reloaded");
        AdvanceStep(10, 7);
    }

    private void Step_Triggered()
    {
        Debug.Log("Step 11: Triggered");
        AdvanceStep(11, 2);
    }

    private void Step_GunShot()
    {
        Debug.Log("Step 12: Shot the gun");
        AdvanceStep(12, 10);
        TutorialEnvironment.Instance.OpenDoor(4);
        TutorialEnvironment.Instance.OpenDoor(5);
        TutorialEnvironment.Instance.ActivateTutoBot();
    }

    private void Step_TeamUp()
    {
        Debug.Log("Step 13: Teamed up");
        AdvanceStep(13, 3);
        TutorialEnvironment.Instance.TriggerTutoBotMovement();
    }

    private void Step_Talk()
    {
        Debug.Log("Step 14: Talked");
        AdvanceStep(14, 5);
    }

    private void Step_EndTeamUp()
    {
        Debug.Log("Step 15: Ended team up");
        AdvanceStep(15, 1);
    }

    private void Step_Slap()
    {
        Debug.Log("Step 16: Slapped");
        AdvanceStep(16, 2);
    }

    private IEnumerator LoadLobbyAfterDelay()
    {
        yield return new WaitForSeconds(5f);
        Cursor.lockState = CursorLockMode.None;
        SceneManager.LoadScene("Loading");
    }

    private void OnDestroy()
    {
        var tm = TutorialManager.Instance;

        tm.OnLook -= Step_Looked;
        tm.OnMove -= Step_Moved;
        tm.OnSprint -= Step_Sprinted;
        tm.OnJump -= Step_Jumped;
        tm.OnPickUp -= Step_PickedUp;
        tm.OnThrow -= Step_Thrown;
        tm.OnShutDown -= Step_ShutDown;
        tm.OnCrouch -= Step_Crouched;
        tm.OnSlide -= Step_Slid;
        tm.OnSwitchToGun -= Step_SwitchedToGun;
        tm.OnReload -= Step_Reloaded;
        tm.OnTrigger -= Step_Triggered;
        tm.OnGunShot -= Step_GunShot;
        tm.OnTeamUp -= Step_TeamUp;
        tm.OnTalk -= Step_Talk;
        tm.OnEndTeamUp -= Step_EndTeamUp;
        tm.OnSlap -= Step_Slap;
    }
}
