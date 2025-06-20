using UnityEngine;
using System.Collections;
public class TutorialRagdoll : MonoBehaviour
{

    [SerializeField]
    private Transform parent;

    private Rigidbody[] _ragdollRigidbodies;

    [SerializeField] private GameObject dizzy;
    [SerializeField] private GameObject botName;

    void Awake()
    {
        _ragdollRigidbodies = parent.GetComponentsInChildren<Rigidbody>();
    }
    public void EnableRagdoll()
    {
        GetComponent<Collider>().enabled = false;
        GetComponent<Animator>().enabled = false;
        foreach (Rigidbody rigidbody in _ragdollRigidbodies)
        {
            rigidbody.isKinematic = false;
        }
        botName.SetActive(false);
        EnableDizziness(5f);
    }

    private void EnableDizziness(float waitTime)
    {
        dizzy.SetActive(true);
        StartCoroutine(WaitBeforeDisactivate(waitTime));
    }

    private IEnumerator WaitBeforeDisactivate(float waitTime)
    {
        yield return new WaitForSeconds(waitTime);

        dizzy.SetActive(false);
    }
}
