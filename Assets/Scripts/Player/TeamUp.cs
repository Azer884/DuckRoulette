using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class TeamUp : MonoBehaviour
{
    private List<GameObject> validPlayers = new List<GameObject>();
    public bool isTeamedUp = false;
    public int teamMateId = -1;
    public float teamUpRaduis = 2f;
    public LayerMask otherPlayers;
    public Transform teamUpArea;
    Collider[] teamUpResults =  new Collider[10];
    private float teamUpCooldown = 5f; // Cooldown duration in seconds
    private float lastTeamUpTime = -5f; // Initialize to allow immediate team-up
    public bool haveRequest = false;
    private ulong requesterId; // Add this line to store requesterId
    private int perfectDap = 0;
    public Transform dapPosition;
    public AudioClip dapSound;
    public AudioClip perfectDapSound;

    void Update()
    {
        if (haveRequest)
        {
            if (Input.GetKeyDown(KeyCode.E))
            {
                isTeamedUp = true;
                teamMateId = (int)requesterId;
                perfectDap = UnityEngine.Random.Range(0, 2);
                //Play the dap animation and sound

                GameManager.Instance.TeamUpResponseServerRpc(NetworkManager.Singleton.LocalClientId, requesterId, dapPosition.position, perfectDap);
                Debug.Log("You have teamed up with player " + requesterId);
                StartCoroutine(ResetHaveRequestAfterDelay(5f)); // Start the coroutine to reset haveRequest
            }
        }
        else
        {
            TryToTeamUp();
        }

        if (isTeamedUp && Input.GetKeyDown(KeyCode.X))
        {
            EndTeamUpOnServer();
            Debug.Log("You have ended the team up with player " + teamMateId);
        }
    }

    private void TryToTeamUp()
    {
        int numColliders = Physics.OverlapSphereNonAlloc(teamUpArea.position, teamUpRaduis, teamUpResults, otherPlayers);
        validPlayers.Clear();

        for (int i = 0; i < numColliders; i++)
        {
            if (teamUpResults[i].GetComponentInParent<TeamUp>() != null)
            {
                TeamUp teamUpComponent = teamUpResults[i].GetComponentInParent<TeamUp>();
                if(teamUpComponent != this)
                {
                    validPlayers.Add(teamUpComponent.gameObject);
                }
            }
        }
        if (validPlayers?.Count > 0)
        {
            if (Input.GetKeyDown(KeyCode.E) && !isTeamedUp && Time.time >= lastTeamUpTime + teamUpCooldown)
            {
                GameManager.Instance.TeamUpRequestServerRpc(validPlayers[0].GetComponent<NetworkObject>().OwnerClientId);
                lastTeamUpTime = Time.time;
            }
            Debug.Log("Press E to team up with player " + validPlayers[0].GetComponent<NetworkObject>().OwnerClientId);
        }
    }

    public void RequestTeamUp(ulong requesterId)
    {
        if (isTeamedUp)
        {
            return;
        }
        Debug.Log("Player " + requesterId + " wants to team up with you. Press E to accept.");
        this.requesterId = requesterId; // Store the requesterId
        haveRequest = true;
        
    }

    public void EndTeamUpOnServer()
    {
        GameManager.Instance.EndTeamUpServerRpc((ulong)teamMateId);
        EndTeamUp();
    }
    public void EndTeamUp()
    {
        isTeamedUp = false;
        teamMateId = -1;
    }

    public void PlayDapSound(Vector3 dapPosition, bool perfectDap)
    {
        AudioClip clipToPlay = perfectDap ? perfectDapSound : dapSound;

        // Create a temporary GameObject with an AudioSource
        GameObject audioObject = new GameObject("TempAudio");
        audioObject.transform.position = dapPosition;
        AudioSource audioSource = audioObject.AddComponent<AudioSource>();

        // Set the clip and adjust the pitch for variety
        audioSource.clip = clipToPlay;
        audioSource.spatialBlend = 1.0f; // Make the sound 3D
        audioSource.pitch = UnityEngine.Random.Range(0.9f, 1.1f); // Randomize the pitch slightly

        // Play the sound and destroy the GameObject after the clip duration
        audioSource.Play();
        Destroy(audioObject, clipToPlay.length);
    }

    private IEnumerator ResetHaveRequestAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        haveRequest = false;
    }

    private void OnDrawGizmos() {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(teamUpArea.position, teamUpRaduis);
    }
}
