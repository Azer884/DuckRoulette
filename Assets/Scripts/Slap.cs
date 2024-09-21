using System.Collections;
using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

public class Slap : NetworkBehaviour 
{
    private PlayerInput inputActions;
    [SerializeField] private Transform slapArea;
    [SerializeField] private float slapRaduis;
    [SerializeField] private float slapCoolDown = 1f;
    [SerializeField] private Animator[] animators;
    [SerializeField] private LayerMask otherPlayers;
    private Collider[] slappedPlayers;
    private bool canSlap = true;

    // Stun related variables
    private Dictionary<GameObject, int> slapCount = new();
    private Dictionary<GameObject, int> slapLimit = new();
    private Dictionary<GameObject, Coroutine> slapCoroutines = new();

    public override void OnNetworkSpawn()
    {
        if (!IsOwner) enabled = false;
        base.OnNetworkSpawn();
    }

    private void Awake() 
    {
        inputActions = new PlayerInput();
    }

    private void OnEnable() 
    {
        inputActions.Enable();
    }

    private void OnDisable() 
    {
        inputActions.Disable();
    }

    private void Update() 
    {
        if (inputActions.PlayerControls.Slap.triggered && canSlap)
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
        slappedPlayers = Physics.OverlapSphere(slapArea.position, slapRaduis, otherPlayers);

        if (slappedPlayers != null && slappedPlayers.Length > 1)
        {
            GameObject slappedPlayer = slappedPlayers[0].gameObject;
            if (slappedPlayer != gameObject)
            {
                SlapPlayer(slappedPlayer);
            }
            else
            {
                slappedPlayer = slappedPlayers[1].gameObject;
                SlapPlayer(slappedPlayer);
            }
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

        slapCount[player]++;
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
        GameManager.Instance.StunPlayerClientRpc(clientId);
    }
}
