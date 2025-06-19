using System.Collections;
using UnityEngine;

public class TutorialEnvironment : MonoBehaviour
{
    public static TutorialEnvironment Instance { get; private set; }

    [SerializeField] private GameObject[] doors;
    [SerializeField] private Vector3 upPosition = new Vector3(0, 1, 0);
    [SerializeField] private GameObject BoomBox, TutoBot, TutoDoll;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public void OpenDoor(int doorIndex)
    {
        StartCoroutine(TranslateTo(doors[doorIndex].transform, upPosition));
    }

    private IEnumerator TranslateTo(Transform obj, Vector3 positionToAdd)
    {
        yield return new WaitForSeconds(2f); 
        Vector3 currentPosition = obj.localPosition;
        Vector3 targetPosition = currentPosition + positionToAdd;
        float elapsed = 0f;
        float animationDuration = 1f; // Duration of the translation animation

        while (elapsed < animationDuration)
        {
            obj.localPosition = Vector3.Lerp(currentPosition, targetPosition, elapsed / animationDuration);
            elapsed += Time.deltaTime;
            yield return null;
        }

        obj.localPosition = targetPosition;
    }
}