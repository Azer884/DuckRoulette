using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public partial class TutorialManager
{
    [SerializeField] private Transform slapArea;
    [SerializeField] private float slapRaduis;
    [SerializeField] private float slapCoolDown = 1f;
    [SerializeField] private LayerMask otherPlayers;
    private Collider[] slapResults = new Collider[10];
    private bool canSlap = true;

    // Stun related variables
    private int slapCount = 0;
    private int slapLimit = 3;
    public AudioSource slapAudio;
    private InputAction slapAction;

    private void CacheSlapInputActions()
    {
        slapAction = inputActions.FindAction("Slap");
    }

    private void Slap()
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

    private IEnumerator Timer(float waitTime)
    {
        yield return new WaitForSeconds(waitTime);
        canSlap = true;
    }

    private void TryToSlap()
    {
        int numColliders = Physics.OverlapSphereNonAlloc(slapArea.position, slapRaduis, slapResults, otherPlayers);

        if (numColliders > 0)
        {
            slapAudio.Play();
            slapCount++;

            if (slapCount >= slapLimit)
            {
                slapResults[0].GetComponent<TutorialRagdoll>().EnableRagdoll();
                if (!slapped)
                {
                    slapped = true;
                    OnSlap?.Invoke();
                }

                slapCount = 0;
            }
        }
    }
}
