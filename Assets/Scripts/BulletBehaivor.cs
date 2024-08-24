using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BulletBehaivor : MonoBehaviour
{
    

    private void Awake() 
    {
        GetComponent<Rigidbody>().useGravity = false;
        Destroy(gameObject, 5f);
    }


    // Update is called once per frame
    void Update()
    {
        
    }
    void OnCollisionEnter(Collision collision)
    {
        GetComponent<Rigidbody>().linearVelocity = Vector3.zero;
        GetComponent<Rigidbody>().useGravity = true;
    }
}
