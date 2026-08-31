using System.Collections;
using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;
using UnityEngine.InputSystem;

public class Slap : NetworkBehaviour
{
    public event System.Action OnSlap, OnSlapTriggered;
    public event System.Action OnSlapRecived;
    // Separate from OnSlapRecived (additive, not a breaking signature change for its existing
    // subscribers) - carries the attacker's world position so listeners (e.g. SlapFeedback) can
    // work out which direction the slap came from.
    public event System.Action<Vector3> OnSlapRecivedFrom;
    public ShakeProfile slapShakeProfile;
    public ShakeProfile slapReceivedShakeProfile;
    private CameraShaker cameraShaker;
    private InputActionAsset inputActions;
    private InputAction slapAction;
    [SerializeField] private Transform slapArea;
    public Transform SlapArea => slapArea;
    // The actual swinging hand bone (visible to other players), so the impact VFX/sound land
    // where the hand touches the victim instead of on the static, non-animated slapArea sensor.
    [SerializeField] private Transform handTransform;
    public Transform HandTransform => handTransform;
    [SerializeField] private float slapRaduis;
    [SerializeField] private float slapCoolDown = 1f;
    [SerializeField] private Animator[] animators;
    [SerializeField] private LayerMask otherPlayers;
    [SerializeField] private float slapRaycastDistance = 2.5f;
    private Transform mainCameraTransform;
    private Collider[] slapResults = new Collider[10];
    private bool canSlap = true;

    // Stun related variables
    private Dictionary<GameObject, int> slapCount = new();
    private Dictionary<GameObject, int> slapLimit = new();
    private Dictionary<GameObject, Coroutine> slapCoroutines = new();
    public AudioSource slapAudio;

    public override void OnNetworkSpawn()
    {
        if(!IsOwner) enabled = false;
        base.OnNetworkSpawn();
    }

    private void Awake() {
        inputActions = GetComponent<InputSystem>().inputActions;
        slapAction = inputActions.FindAction("Slap");
        cameraShaker = CameraShaker.GetOrAdd(gameObject);
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
        if (cameraShaker != null)
        {
            cameraShaker.Shake(slapShakeProfile);
        }
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

        Vector3 impactPosition = ResolveImpactPosition(player);
        PlaySlapSound(impactPosition);
        OnSlapTriggered?.Invoke();
        slapCount[player]++;

        SlapImpactServerRpc(player.GetComponent<NetworkObject>().OwnerClientId, transform.position);

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

    // Impact sound/VFX should land where the attacker was actually looking when the hand connected
    // - raycasting from the camera is what makes the effect line up with what's on screen instead
    // of a fixed anchor. Falls back to the hand/slapArea midpoint when the camera isn't aimed
    // squarely at the victim (slap detection is a lenient overlap sphere, not aim-dependent), then
    // to whichever single anchor is available, then to a raw position.
    private Vector3 ResolveImpactPosition(GameObject player)
    {
        if (mainCameraTransform == null)
        {
            mainCameraTransform = Camera.main != null ? Camera.main.transform : null;
        }

        if (mainCameraTransform != null &&
            Physics.Raycast(mainCameraTransform.position, mainCameraTransform.forward, out RaycastHit hit, slapRaycastDistance, otherPlayers))
        {
            if (player != null && hit.collider.GetComponentInParent<Slap>()?.gameObject == player)
            {
                return hit.point;
            }
        }

        Transform victimSlapArea = player != null && player.TryGetComponent(out Slap victimSlap) ? victimSlap.SlapArea : null;
        Transform attackerHand = handTransform != null ? handTransform : slapArea;
        if (attackerHand != null && victimSlapArea != null)
        {
            return Vector3.Lerp(attackerHand.position, victimSlapArea.position, 0.5f);
        }
        if (victimSlapArea != null)
        {
            return victimSlapArea.position;
        }
        if (attackerHand != null)
        {
            return attackerHand.position;
        }
        return player != null ? player.transform.position : transform.position;
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
    private void SlapImpactServerRpc(ulong clientId, Vector3 attackerPosition)
    {
        SlapImpactClientRpc(clientId, attackerPosition);
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
        GameObject prefab = VfxManager.Instance != null ? VfxManager.Instance.slapImpactVfxPrefab : null;
        if (prefab == null)
        {
            return;
        }

        GameObject instance = Instantiate(prefab, position, Quaternion.identity);

        if (instance.TryGetComponent(out ParticleSystem impactParticleSystem))
        {
            impactParticleSystem.Clear(true);
            impactParticleSystem.Play(true);
        }

        Destroy(instance, VfxManager.Instance.slapImpactVfxLifetime);
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
    private void SlapImpactClientRpc(ulong clientId, Vector3 attackerPosition)
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
                victimSlap.OnSlapRecivedFrom?.Invoke(attackerPosition);
                if (victimSlap.cameraShaker != null)
                {
                    victimSlap.cameraShaker.Shake(victimSlap.slapReceivedShakeProfile);
                }
            }
        }
    }
}