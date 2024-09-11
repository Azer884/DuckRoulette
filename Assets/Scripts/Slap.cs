using System.Collections;
using UnityEngine;

class Slap : MonoBehaviour 
{
    private PlayerInput inputActions;
    [SerializeField]private Transform slapArea;
    [SerializeField]private float slapRaduis;
    [SerializeField]private float slapCoolDown = 1f;
    [SerializeField] private Animator[] animators;
    [SerializeField] private LayerMask otherPlayers;
    private bool canSlap = true;

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
            if(Physics.CheckSphere(slapArea.position, slapRaduis, otherPlayers))
            {

            }
            foreach (Animator anim in animators)
            {
                anim.SetTrigger("Slap");
            }

            canSlap = false;
            StartCoroutine(Timer(slapCoolDown));
        }
    }
    private void OnDrawGizmos()
    {
        Gizmos.DrawWireSphere(slapArea.position, slapRaduis);
    }

    private IEnumerator Timer(float waitTime)
    {
        yield return new WaitForSeconds(waitTime);
        canSlap = true;
    }
}