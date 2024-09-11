using System.Collections;
using UnityEngine;
using Unity.Netcode;

public class Slap : NetworkBehaviour 
{
    private PlayerInput inputActions;
    [SerializeField]private Transform slapArea;
    [SerializeField]private float slapRaduis;
    [SerializeField]private float slapCoolDown = 1f;
    [SerializeField] private Animator[] animators;
    [SerializeField] private LayerMask otherPlayers;
    [SerializeField] private float slapForce = 5.0f;
    private Collider[] slappedPlayers;
    private bool canSlap = true;

    public override void OnNetworkSpawn()
    {
        if (!IsOwner) enabled = false;

        base.OnNetworkSpawn();
    }
    private void Awake() {
        inputActions = new PlayerInput();
    }
    private void OnEnable() {
        inputActions.Enable();
    }
    private void OnDisable() {
        inputActions.Disable();
    }
    private void Update() {
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
        CharacterController targetController = player.GetComponent<CharacterController>();
        if (targetController != null)
        {
            Vector3 slapDirection = (player.transform.position - transform.position).normalized;
            Vector3 slapVelocity = slapDirection * slapForce;
            
            StartCoroutine(ApplySlap(targetController, slapVelocity));
        }
    }

    private IEnumerator ApplySlap(CharacterController targetController, Vector3 slapVelocity)
    {
        float duration = 0.2f;
        float timer = 0;

        while (timer < duration)
        {
            targetController.Move(slapVelocity * Time.deltaTime);
            timer += Time.deltaTime;
            yield return null;
        }
    }
}