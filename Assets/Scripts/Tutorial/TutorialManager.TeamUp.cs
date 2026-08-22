using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.InputSystem;

public partial class TutorialManager
{
    private List<GameObject> validPlayers = new();
    public bool isTeamedUp = false;
    public GameObject teamMate;
    public float teamUpRaduis = 2f;
    public Transform teamUpArea;
    Collider[] teamUpResults = new Collider[1];
    public AudioClip dapSound;
    public AudioClip perfectDapSound;
    private int perfectDap = 0;
    public Transform dapPosition;
    public AudioMixerGroup audioMixerGroup;
    public Color teamColor = Color.green;
    private InputAction endTeamUpAction;

    // interactAction is declared/cached in TutorialManager.Interact.cs (CacheInteractInputActions) -
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
            MessageBox.Informate("Press E to team up with TutoBot ", Color.white, MessagePriority.Low, 0.5f);
        }
    }
    public void EndTeamUp()
    {
        isTeamedUp = false;
    }

    public void PlayDapSound(Vector3 dapPosition, bool perfectDap)
    {
        AudioClip clipToPlay = perfectDap ? perfectDapSound : dapSound;

        // Create a temporary GameObject with an AudioSource
        GameObject audioObject = new("TempAudio");
        audioObject.transform.position = dapPosition;
        AudioSource audioSource = audioObject.AddComponent<AudioSource>();
        audioSource.outputAudioMixerGroup = audioMixerGroup;

        // Set the clip and adjust the pitch for variety
        audioSource.clip = clipToPlay;
        audioSource.spatialBlend = 1.0f; // Make the sound 3D
        audioSource.pitch = UnityEngine.Random.Range(0.9f, 1.1f); // Randomize the pitch slightly

        // Play the sound and destroy the GameObject after the clip duration
        audioSource.Play();
        Destroy(audioObject, clipToPlay.length);
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
