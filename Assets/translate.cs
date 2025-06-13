using UnityEngine;

public class translate : MonoBehaviour
{
    
    public float speed = 5.0f; // Speed of translation
    // Update is called once per frame
    void Update()
    {
        transform.Translate(speed * Time.deltaTime * Vector3.forward); // Move the object to the right at a speed of 5 units per second
    }
}
