using UnityEngine;

public class TutoBullet : MonoBehaviour
{

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Doll"))
        {
            other.GetComponent<Doll>().Hide();
            other.GetComponent<Doll>().isAlive = false;
        }
        Destroy(gameObject);
    }
}
