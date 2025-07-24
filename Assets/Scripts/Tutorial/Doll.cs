using UnityEngine;

public class Doll : MonoBehaviour
{
    public Animator animator;
    public bool isAlive = true, shown;
    public void Hide()
    {
        animator.SetTrigger("Dispawn");
        shown = false;
    }
    public void Show()
    {
        animator.SetTrigger("Spawn");
        shown = true;
        int randomDelay = Random.Range(3, 5);
        StartCoroutine(HideAfterDelay(randomDelay));
    }
    private System.Collections.IEnumerator HideAfterDelay(int delay)
    {
        yield return new WaitForSeconds(delay);
        Hide();
    }
}
