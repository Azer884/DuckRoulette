using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Shooting : MonoBehaviour
{
    public GameObject bulletPrefab;
    private GameObject bullet;
    public float bulletSpeed = 10f;
    public Transform spawnPt;
    public Transform cam;
    private PlayerInput inputActions;
    public Animator animator;
    // Start is called before the first frame update
    void Awake()
    {
        inputActions = new PlayerInput();
    }
    private void OnEnable() 
    {
        inputActions.Enable();
    }
    private void OnDisable()
    {
        inputActions.Disable();
    }

    // Update is called once per frame
    void Update()
    {
        if (inputActions.PlayerControls.Shoot.triggered)
        {
            animator.Play("Shooting");
            bullet = Instantiate(bulletPrefab, spawnPt.position, Quaternion.identity);

            if (bullet.TryGetComponent<Rigidbody>(out var bulletRigidbody))
            {
                Vector3 direction = cam.forward;

                bulletRigidbody.rotation = Quaternion.LookRotation(direction);
                bulletRigidbody.velocity = direction * bulletSpeed;
            }
        }
        
    }
}
