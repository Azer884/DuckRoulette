using System.Collections;
using UnityEngine;

public class TutorialReseter : MonoBehaviour
{
    [SerializeField] private Transform playerSpawnPoint, bumBoxSpawnPoint;
    [SerializeField] private LayerMask layer;
    void OnTriggerEnter(Collider other)
    {
        if (other.TryGetComponent(out IInteractable _))
        {
            other.transform.position = bumBoxSpawnPoint.position;
        }
        else
        {
            TutorialManager tm = other.GetComponent<TutorialManager>();
            Interact interact = other.GetComponent<Interact>();
            Movement movement = other.GetComponent<Movement>();
            CharacterController cc = other.GetComponent<CharacterController>();
            if (tm == null || cc == null)
            {
                return;
            }

            // The shared Movement component drives locomotion now - disable it instead of
            // TutorialManager (which only orchestrates steps/detection).
            if (movement != null)
            {
                movement.enabled = false;
            }
            cc.enabled = false;
            other.transform.position = playerSpawnPoint.position;
            Debug.Log("Player respawned at: " + playerSpawnPoint.position);
            if (interact != null && interact.IsHoldingObject)
            {
                interact.HeldObject.transform.position = bumBoxSpawnPoint.position;
                interact.DropHeldObject();
            }
            StartCoroutine(EnableMovement(other.gameObject));
        }
    }

    private IEnumerator EnableMovement(GameObject obj)
    {
        yield return new WaitForSeconds(1f);
        Movement movement = obj.GetComponent<Movement>();
        if (movement != null)
        {
            movement.enabled = true;
        }
        obj.GetComponent<CharacterController>().enabled = true;
    }
}
