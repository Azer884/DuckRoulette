using UnityEngine;

public class Test : MonoBehaviour
{
    public Animator animator;
    /// <summary>
    /// Awake is called when the script instance is being loaded.
    /// </summary>
    void OnEnable()
    {
        animator.SetBool("HaveAGun", true);
    }

    void OnDisable()
    {
        animator.SetBool("HaveAGun", false);
    }
}
