using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Services.Matchmaker.Models;
using UnityEngine;
using UnityEngine.Audio;

public class TeamUp : NetworkBehaviour
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
    private int requesterId = -1; // Add this line to store requesterId
    private int perfectDap = 0;
    public Transform dapPosition;
    public AudioClip dapSound;
    public AudioClip perfectDapSound;
    public AudioMixerGroup audioMixerGroup;

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            enabled = false;
        }
    }

    void Update()
    {
        TryToTeamUp();

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
                if(teamUpComponent != GetComponentInParent<TeamUp>())
                {
                    validPlayers.Add(teamUpComponent.gameObject);
                }
            }
        }
        if (validPlayers?.Count > 0)
        {
            if (Input.GetKeyDown(KeyCode.E))
            {
                if (!isTeamedUp && Time.time >= lastTeamUpTime + teamUpCooldown && !haveRequest)
                {
                    GameManager.Instance.TeamUpRequestServerRpc(validPlayers[0].GetComponent<NetworkObject>().OwnerClientId);
                    lastTeamUpTime = Time.time;
                }
                else if (haveRequest)
                {
                    isTeamedUp = true;
                    teamMateId = requesterId;
                    perfectDap = UnityEngine.Random.Range(0, 2);
                    //Play the dap animation and sound

                    GameManager.Instance.TeamUpResponseServerRpc(NetworkManager.Singleton.LocalClientId, (ulong)requesterId, dapPosition.position, perfectDap);
                    Debug.Log("You have teamed up with player " + requesterId);
                }
            }
            Debug.Log("Press E to team up with player " + GameManager.Instance.GetPlayerNickname(validPlayers[0].GetComponent<NetworkObject>().OwnerClientId));
        }
        else if (haveRequest)
        {
            haveRequest = false;
        }
    }

    public void RequestTeamUp(ulong requesterId)
    {
        if (isTeamedUp)
        {
            return;
        }
        Debug.Log("Player " + GameManager.Instance.GetPlayerNickname(requesterId) + " wants to team up with you. Press E to accept.");
        this.requesterId = (int)requesterId; // Store the requesterId
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
        audioSource.outputAudioMixerGroup = audioMixerGroup;

        // Set the clip and adjust the pitch for variety
        audioSource.clip = clipToPlay;
        audioSource.spatialBlend = 1.0f; // Make the sound 3D
        audioSource.pitch = UnityEngine.Random.Range(0.9f, 1.1f); // Randomize the pitch slightly

        // Play the sound and destroy the GameObject after the clip duration
        audioSource.Play();
        Destroy(audioObject, clipToPlay.length);
    }

    private void OnDrawGizmos() {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(teamUpArea.position, teamUpRaduis);
    }
}
