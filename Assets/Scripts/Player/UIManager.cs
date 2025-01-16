using UnityEngine;
using System.Collections;

public class UIManager : MonoBehaviour
{
    public void StartCoolDown(float time)
    {
        StartCoroutine(CoolDownCoroutine(time));
    }

    private IEnumerator CoolDownCoroutine(float time)
    {
        while (time > 0)
        {
            Debug.Log("Cooldown: " + time);
            yield return new WaitForSeconds(1f);
            time--;
        }
        Debug.Log("Cooldown complete");
    }
}
