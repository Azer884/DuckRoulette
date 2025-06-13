using UnityEngine;

public class Test1 : MonoBehaviour
{
    public Animator animator;
    /// <summary>
    /// Awake is called when the script instance is being loaded.
    /// </summary>
    void OnEnable()
    {
        animator.Play("Shooting");
    }
}
