using System.Collections;
using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;
using UnityEngine.InputSystem;

public class Slap : NetworkBehaviour
{
    public event System.Action OnSlap, OnSlapTriggered;
    public event System.Action OnSlapRecived;
    private InputActionAsset inputActions;
    private InputAction slapAction;
    [SerializeField] private Transform slapArea;
    [SerializeField] private float slapRaduis;
    [SerializeField] private float slapCoolDown = 1f;
    [SerializeField] private Animator[] animators;
    [SerializeField] private LayerMask otherPlayers;
    private Collider[] slapResults = new Collider[10];
    private bool canSlap = true;

    // Stun related variables
    private Dictionary<GameObject, int> slapCount = new();
    private Dictionary<GameObject, int> slapLimit = new();
    private Dictionary<GameObject, Coroutine> slapCoroutines = new();
    public AudioSource slapAudio;
    [SerializeField] private GameObject impactVfxPrefab;
    [SerializeField] private float impactVfxLifetime = 1.5f;

    public override void OnNetworkSpawn()
    {
        if(!IsOwner) enabled = false;
        base.OnNetworkSpawn();
    }

    private void Awake() {
        inputActions = GetComponent<InputSystem>().inputActions;
        slapAction = inputActions.FindAction("Slap");
    }

    private void Update()
    {
        if (slapAction.triggered && canSlap)
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
        Gizmos.color = UnityEngine.Color.yellow;
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
        Debug.Log("Slap!");
        
        int numColliders = Physics.OverlapSphereNonAlloc(slapArea.position, slapRaduis, slapResults, otherPlayers);

        List<GameObject> validSlappedPlayers = new();
        for (int i = 0; i < numColliders; i++)
        {
            Slap slapRes = slapResults[i].GetComponentInParent<Slap>();
            if (slapRes != this)
            {
                validSlappedPlayers.Add(slapRes.gameObject);
            }
        }
        Debug.Log($"{validSlappedPlayers.Count} Players can be slapped");
        if (validSlappedPlayers.Count > 0)
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

        PlaySlapSound(slapArea != null ? slapArea.position : transform.position);
        OnSlapTriggered?.Invoke();
        slapCount[player]++;
        
        SlapImpactServerRpc(player.GetComponent<NetworkObject>().OwnerClientId);

        //Major error: Debug.Log($"Player {player.name} has been slapped {slapCount[player]} times (Limit: {slapLimit[player]})");

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
        
        GameManager.Instance.StunPlayerServerRpc(player.GetComponent<NetworkObject>().OwnerClientId);

        // Reset slap count and slap limit
        slapCount[player] = 0;
        slapLimit[player] = Random.Range(3, 10); // Generate new slap limit
        Debug.Log($"{player.name} is no longer stunned.");
    }

    [ServerRpc]
    private void SlapImpactServerRpc(ulong clientId)
    {
        SlapImpactClientRpc(clientId);
    }

    private void PlaySlapSound(Vector3 position)
    {
        if (CanUseNetcode())
        {
            PlaySlapVfxServerRpc(position);
            return;
        }

        PlayLocalOneShot(GetSlapClip(), position);
    }

    [ServerRpc(RequireOwnership = false)]
    private void PlaySlapVfxServerRpc(Vector3 position)
    {
        PlaySlapSoundClientRpc(position);
        SpawnImpactVfxClientRpc(position);
    }

    [ClientRpc]
    private void PlaySlapSoundClientRpc(Vector3 position)
    {
        PlayLocalOneShot(GetSlapClip(), position);
    }

    [ClientRpc]
    private void SpawnImpactVfxClientRpc(Vector3 position)
    {
        SpawnImpactVfx(position);
    }

    private void SpawnImpactVfx(Vector3 position)
    {
        if (impactVfxPrefab == null)
        {
            return;
        }

        GameObject instance = Instantiate(impactVfxPrefab, position, Quaternion.identity);

        if (instance.TryGetComponent(out ParticleSystem impactParticleSystem))
        {
            impactParticleSystem.Clear(true);
            impactParticleSystem.Play(true);
        }

        Destroy(instance, impactVfxLifetime);
    }

    private AudioClip GetSlapClip()
    {
        if (SFXManager.Instance != null && SFXManager.Instance.slapClip != null)
        {
            return SFXManager.Instance.slapClip;
        }

        return slapAudio != null ? slapAudio.clip : null;
    }

    private void PlayLocalOneShot(AudioClip clip, Vector3 position)
    {
        if (SFXManager.Instance != null)
        {
            SFXManager.Instance.PlayAt(clip, position);
        }
    }

    private bool CanUseNetcode()
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
    }

    [ClientRpc]
    private void SlapImpactClientRpc(ulong clientId)
    {
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
        {
            // Fire "getting slapped" on the VICTIM's own Slap instance, not this slapper's -
            // NoiseHandler.OnEnable only ever subscribes to the Slap component on its own
            // GameObject, so invoking OnSlapRecived on `this` (the slapper) misdirected the
            // hit-shake to the slapper instead of the player who actually got slapped.
            var playerObject = client.PlayerObject;
            if (playerObject != null && playerObject.TryGetComponent(out Slap victimSlap))
            {
                victimSlap.OnSlapRecived?.Invoke();
            }
        }
    }
}