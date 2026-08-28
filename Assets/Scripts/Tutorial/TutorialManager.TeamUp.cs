using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public partial class TutorialManager
{
    private List<GameObject> validPlayers = new();
    public bool isTeamedUp = false;
    public GameObject teamMate;
    public float teamUpRaduis = 2f;
    public Transform teamUpArea;
    Collider[] teamUpResults = new Collider[1];
    private int perfectDap = 0;
    public Transform dapPosition;
    public Color teamColor = Color.green;
    private InputAction endTeamUpAction;

    // interactAction is declared/cached in TutorialManager.cs (CacheInputActions) -
    // reused here since TeamUp's "accept" also binds to the same E/Interact action.
    private void CacheTeamUpInputActions()
    {
        endTeamUpAction = inputActions.FindAction("EndTeamUp");
    }

    void TeamUp()
    {
        if (isTeamedUp)
        {
            if (endTeamUpAction.triggered)
            {
                MessageBox.Informate("You have ended the team up with TutoBot", Color.red, MessagePriority.High);
                if (!endedTeamUp)
                {
                    endedTeamUp = true;
                    OnEndTeamUp?.Invoke();
                }

                EndTeamUp();
                if (teamMate != null)
                {
                    RemoveTeamMate();
                }
            }

            return;
        }

        TryToTeamUp();
    }

    private void TryToTeamUp()
    {
        int numColliders = Physics.OverlapSphereNonAlloc(teamUpArea.position, teamUpRaduis, teamUpResults, otherPlayers);
        validPlayers.Clear();

        for (int i = 0; i < numColliders; i++)
        {
            if (teamUpResults[i].GetComponent<TutoBot>())
            {
                validPlayers.Add(teamUpResults[i].gameObject);
            }
        }
        if (validPlayers?.Count > 0)
        {
            InteractionPromptHUD.Show("Team Up", GetBindingDisplayString(interactAction));

            if (interactAction.triggered)
            {
                if (!isTeamedUp)
                {
                    isTeamedUp = true;
                    if (!teamedUp)
                    {
                        teamedUp = true;
                        OnTeamUp?.Invoke();
                    }
                    MessageBox.Informate("You have teamed up with TutoBot", Color.green);
                    AddTeamMate();
                    PlayDapSound(dapPosition.position, perfectDap == 1);
                }

            }
        }
        else
        {
            InteractionPromptHUD.Hide();
        }
    }

    // interactAction/endTeamUpAction each carry a Keyboard and a Gamepad binding at once, so
    // GetBindingDisplayString() with no group returns every matching binding joined with
    // " | " (e.g. "A | E") instead of just the one the player is actually using.
    private static string GetBindingDisplayString(InputAction action)
    {
        bool isGamepad = action.activeControl != null && action.activeControl.device is Gamepad;
        return action.GetBindingDisplayString(group: isGamepad ? "Gamepad" : "Keyboard");
    }
    public void EndTeamUp()
    {
        isTeamedUp = false;
    }

    public void PlayDapSound(Vector3 dapPosition, bool perfectDap)
    {
        if (SFXManager.Instance == null) return;

        AudioClip clipToPlay = perfectDap ? SFXManager.Instance.perfectDapSound : SFXManager.Instance.dapSound;
        SFXManager.Instance.PlayAt(clipToPlay, dapPosition, UnityEngine.Random.Range(0.9f, 1.1f), SFXManager.Instance.dapMixerGroup);
    }

    public void AddTeamMate()
    {
        teamMate = validPlayers[0];
        teamMate.GetComponent<TutoBot>().Accept(teamColor);
    }

    public void RemoveTeamMate()
    {
        teamMate.GetComponentInChildren<TutoBot>().Accept(Color.black);
        teamMate = null;
    }
}
