using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TutorialEnvironment : MonoBehaviour
{
    public static TutorialEnvironment Instance { get; private set; }

    [SerializeField] private GameObject[] doors;
    [SerializeField] private Doll[] tutoDolls;
    [SerializeField] private Vector3 upPosition = new Vector3(0, 1, 0);
    [SerializeField] private GameObject BoomBox, TutoBot;
    public bool isTutoDollActive = false, isLooping = false;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
    }
    void Update()
    {
        if (isTutoDollActive && !isLooping)
        {
            StartCoroutine(DollShowLoop());
            isLooping = true;
        }
    }

    public void OpenDoor(int doorIndex)
    {
        StartCoroutine(TranslateTo(doors[doorIndex].transform, upPosition));
    }

    private IEnumerator TranslateTo(Transform obj, Vector3 positionToAdd)
    {
        yield return new WaitForSeconds(2f);
        if (obj.TryGetComponent(out AudioSource source))
        {
            source.pitch = 1f + Random.Range(0f, 0.5f);
            source.Play();
        }
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
    private IEnumerator DollShowLoop()
    {
        while (isTutoDollActive)
        {
            // Wait before the next batch
            float waitBeforeNextBatch = Random.Range(2f, 4f);
            yield return new WaitForSeconds(waitBeforeNextBatch);

            // Pick eligible dolls
            List<Doll> eligibleDolls = new List<Doll>();
            foreach (var doll in tutoDolls)
            {
                if (doll.isAlive && !doll.shown)
                {
                    eligibleDolls.Add(doll);
                }
            }

            if (eligibleDolls.Count > 0)
            {
                int dollsToShow = Mathf.Min(Random.Range(1, 5), eligibleDolls.Count);
                List<Doll> shownThisBatch = new List<Doll>();

                for (int i = 0; i < dollsToShow; i++)
                {
                    // Choose one from remaining eligible list
                    int index = Random.Range(0, eligibleDolls.Count);
                    var chosen = eligibleDolls[index];
                    eligibleDolls.RemoveAt(index); // Prevent duplicates

                    chosen.Show();
                    shownThisBatch.Add(chosen);

                    // Slight delay between showing each
                    yield return new WaitForSeconds(Random.Range(0.2f, 0.5f));
                }

                // Wait for all dolls in this batch to hide
                bool anyStillShown;
                do
                {
                    anyStillShown = false;
                    foreach (var doll in shownThisBatch)
                    {
                        if (doll.shown)
                        {
                            anyStillShown = true;
                            break;
                        }
                    }
                    yield return null;
                } while (anyStillShown);
            }
        }
    }



    public void ActivateTutoBot()
    {
        if (TutoBot != null)
        {
            TutoBot.SetActive(true);
        }
    }

    public void TriggerTutoBotMovement()
    {
        StartCoroutine(ActivateAnim(TutoBot.GetComponent<TutoBot>()));
    }

    private IEnumerator ActivateAnim(TutoBot tutoBot)
    {
        yield return new WaitForSeconds(2f);
        tutoBot.Move();

        yield return new WaitForSeconds(1.5f);
        tutoBot.Talk();
    }
}