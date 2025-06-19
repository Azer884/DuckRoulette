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
            tm.enabled = false;
            other.GetComponent<CharacterController>().enabled = false;
            other.transform.position = playerSpawnPoint.position;
            Debug.Log("Player respawned at: " + playerSpawnPoint.position);
            if (tm.pickedUpObject != null)
            {
                tm.pickedUpObject.transform.position = bumBoxSpawnPoint.position;
                tm.pickedUpObject.GetComponent<IInteractable>().Drop();
                tm.pickedUpObject = null;
            }
            StartCoroutine(EnableMovement(other.gameObject));
        }
    }

    private IEnumerator EnableMovement(GameObject obj)
    {
        yield return new WaitForSeconds(1f);
        obj.GetComponent<TutorialManager>().enabled = true;
        obj.GetComponent<CharacterController>().enabled = true;
    }
}
