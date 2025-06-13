using UnityEngine;

public class activatedAnim : MonoBehaviour
{
    public Animator animator; // Reference to the Animator component
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void OnEnable()
    {
        animator.enabled = true; // Enable the Animator component
    }

    void OnDisable()
    {
        animator.enabled = false; // Disable the Animator component
    }
}
