using UnityEngine;

public class LookAtCam : MonoBehaviour 
{
    private Camera cam;

    private void Start() {
        cam = Camera.main;
    }
    private void Update() {
        if (cam != null) 
        {
            transform.LookAt(transform.position + cam.transform.rotation * Vector3.forward,cam.transform.rotation * Vector3.up);
        }
    }
}