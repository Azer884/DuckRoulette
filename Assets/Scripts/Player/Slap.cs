using System.Collections;
using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;
using UnityEngine.InputSystem;

public class Slap : NetworkBehaviour 
{
    public event System.Action OnSlap;
    public event System.Action OnSlapRecived;
    private InputActionAsset inputActions;
    [SerializeField] private Transform slapArea;
    [SerializeField] private float slapRaduis;
    [SerializeField] private float slapCoolDown = 1f;
    [SerializeField] private Animator[] animators;
    [SerializeField] private LayerMask otherPlayers;
    private Collider[] slappedPlayers;
    private Ragdoll[] players;
    private bool canSlap = true;

    // Stun related variables
    private Dictionary<GameObject, int> slapCount = new();
    private Dictionary<GameObject, int> slapLimit = new();
    private Dictionary<GameObject, Coroutine> slapCoroutines = new();
    public AudioSource slapAudio;

    public override void OnNetworkSpawn()
    {
        if (!IsOwner) enabled = false;
        base.OnNetworkSpawn();
    }

    private void OnEnable() 
    {
        inputActions = RebindSaveLoad.Instance.actions;
    }

    private void Update() 
    {
        if (inputActions.FindAction("Slap").triggered && canSlap)
        {
            foreach (Animator anim in animators)
            {
                anim.SetTrigger("Slap");
            }
            TryToSlap();

            canSlap = false;
            StartCoroutine(Timer(slapCoolDown));
        }
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(slapArea.position, slapRaduis);
    }

    private IEnumerator Timer(float waitTime)
    {
        yield return new WaitForSeconds(waitTime);
        canSlap = true;
    }

    private void TryToSlap()
    {
        OnSlap?.Invoke();
        RebindSaveLoad.Instance.RumbleGamepad(0.5f, .8f, .2f, 0.3f);
        slappedPlayers = Physics.OverlapSphere(slapArea.position, slapRaduis, otherPlayers);

        List<GameObject> validSlappedPlayers = new();
        foreach (Collider collider in slappedPlayers)
        {
            // Ensure the collider has a Slap component and is not this player
            if (collider.TryGetComponent<Slap>(out var slapComponent) && slapComponent != this)
            {
                validSlappedPlayers.Add(collider.gameObject);
            }
        }

        if (validSlappedPlayers != null && validSlappedPlayers.Count > 0)
        {
            SlapPlayer(validSlappedPlayers[0]);
        }
    }

    private void SlapPlayer(GameObject player)
    {
        // Handle slap count and stun check
        if (!slapCount.ContainsKey(player))
        {
            slapCount[player] = 0;
            slapLimit[player] = Random.Range(3, 10); // Set a random limit between 3 and 10
        }

        slapAudio.Play();
        slapCount[player]++;
        
        SlapImpactServerRpc(player.GetComponent<NetworkObject>().OwnerClientId);

        Debug.Log($"Player {player.name} has been slapped {slapCount[player]} times (Limit: {slapLimit[player]})");

        if (slapCount[player] >= slapLimit[player])
        {
            StunPlayer(player);
        }
        else
        {
            if (slapCoroutines.ContainsKey(player)) StopCoroutine(slapCoroutines[player]);
            slapCoroutines[player] = StartCoroutine(ResetSlapCountAfterOneMinute(player));
        }
    }

    // Reset slap count after 1 minute if the player hasn't been stunned
    private IEnumerator ResetSlapCountAfterOneMinute(GameObject player)
    {
        yield return new WaitForSeconds(60f);
        slapCount[player] = 0; // Reset slap count after 1 minute
        slapLimit[player] = Random.Range(3, 10);
    }

    // Stun the player
    private void StunPlayer(GameObject player)
    {
        Debug.Log($"{player.name} is stunned!");
        
        StunPlayerServerRpc(player.GetComponent<NetworkObject>().OwnerClientId);

        // Reset slap count and slap limit
        slapCount[player] = 0;
        slapLimit[player] = Random.Range(3, 10); // Generate new slap limit
        Debug.Log($"{player.name} is no longer stunned.");
    }
    
    [ServerRpc]
    private void StunPlayerServerRpc(ulong clientId)
    {
        StunPlayerClientRpc(clientId);
    }
    [ClientRpc]
    private void StunPlayerClientRpc(ulong clientId)
    {
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
        {
            // Get the player's object and trigger the ragdoll
            var playerObject = client.PlayerObject;
            if (playerObject != null)
            {
                playerObject.GetComponent<Ragdoll>().TriggerRagdoll(false);
            }
        }
    }

    [ServerRpc]
    private void SlapImpactServerRpc(ulong clientId)
    {
        SlapImpactClientRpc(clientId);
    }
    [ClientRpc]
    private void SlapImpactClientRpc(ulong clientId)
    {
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
        {
            // Get the player's object and trigger the ragdoll
            var playerObject = client.PlayerObject;
            if (playerObject != null)
            {
                OnSlapRecived?.Invoke();
                RebindSaveLoad.Instance.RumbleGamepad(0.25f, .8f, .2f, 0.3f);
            }
        }
    }
}