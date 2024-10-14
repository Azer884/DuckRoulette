using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class Movement : NetworkBehaviour
{
    private PlayerInput inputActions;
    private CharacterController controller;
    
    [SerializeField] private Transform camHolder;
    [SerializeField] private GameObject secondCamHolder;
    [SerializeField] private GameObject cam;
    [SerializeField] private float movementSpeed = 2.0f;
    [SerializeField] public float lookSensitivity = 1.0f;
    private float xRotation = 0f;

    [Header("Movement Variables"), Space]
    private Vector3 velocity;
    public float gravity = -9.81f;
    private bool grounded;
    public float speedMultiplier = 1.0f;
    [SerializeField] private float jumpHeight = 1.5f;
    

    [Header("Crouch Variables"), Space]
    private float initHeight;
    [SerializeField] private float crouchHeight;


    [SerializeField] private Animator[] animators;
    [SerializeField] private Animator handAnim;
    [SerializeField] private float velocityX = 0f;
    [SerializeField] private float velocityZ = 0f;

    
    [SerializeField] private GameObject legs;
    [SerializeField] private GameObject FPShadow;
    [SerializeField] private GameObject Hands;
    [SerializeField] private GameObject fullBody;
    
    float mouseXSmooth = 0f;


    private void Awake()
    {
        inputActions = new PlayerInput();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();


        if (!IsOwner)
        {
            cam.SetActive(false);
            camHolder.gameObject.SetActive(false);
            secondCamHolder.SetActive(false);
            ChangeLayerRecursively(fullBody, 3);
            ChangeLayerRecursively(legs, 2);
            ChangeLayerRecursively(FPShadow, 2);
            ChangeLayerRecursively(Hands, 2);
            enabled = false;
        }
        else
        {
            transform.position = new Vector3(0, 2, (int)OwnerClientId * 2);
            cam.SetActive(true);
            camHolder.gameObject.SetActive(true);
            secondCamHolder.SetActive(true);
            ChangeLayerRecursively(fullBody, 2);
            ChangeLayerRecursively(legs, 3);
            ChangeLayerRecursively(FPShadow, 3);
            ChangeLayerRecursively(Hands, LayerMask.NameToLayer("Hands"));
        }
    }

    private void Start()
    {
        controller = GetComponent<CharacterController>();
        initHeight = controller.height;
        Cursor.lockState = CursorLockMode.Locked;
    }

    private void OnEnable()
    {
        inputActions.Enable();
    }

    private void Update()
    {
        DoMovement();
        DoCrouch();
        DoLooking();
    }

    private void DoLooking()
    {
        Vector2 looking = GetPlayerLook();
        float lookX = looking.x * lookSensitivity * Time.deltaTime;
        float lookY = looking.y * lookSensitivity * Time.deltaTime;

        xRotation -= lookY;
        xRotation = Mathf.Clamp(xRotation, -85f, 75f);

        camHolder.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
        transform.Rotate(Vector3.up * lookX);

        
        mouseXSmooth = Mathf.Lerp(mouseXSmooth, looking.x, 4 * Time.deltaTime);
    }

    private void DoMovement()
    {
        grounded = controller.isGrounded;
        if (grounded && velocity.y < 0)
        {
            velocity.y = -2f;
        }

        Vector2 movement = GetPlayerMovement();
        speedMultiplier = inputActions.PlayerControls.Run.ReadValue<float>() > 0 && movement.y > 0 ? 2.0f : 1.0f;

        Vector3 move = transform.right * movement.x + transform.forward * movement.y;
        controller.Move(move * movementSpeed * speedMultiplier * Time.deltaTime);

        // Jumping
        if (grounded && inputActions.PlayerControls.Jump.triggered)
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }
        

        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);

        velocityX = Mathf.Lerp(velocityX, movement.x, 10f * Time.deltaTime);
        velocityZ = Mathf.Lerp(velocityZ, movement.y * speedMultiplier, 10f * Time.deltaTime);
        UpdateAnimator(velocityX, velocityZ);
    }

    private void UpdateAnimator(float xVelocity, float yVelocity)
    {
        foreach (Animator animator in animators)
        {
            animator.SetFloat("XVelocity", xVelocity);
            animator.SetFloat("YVelocity", yVelocity);
            animator.SetBool("IsGrounded", grounded);
            animator.SetFloat("Turning", mouseXSmooth);
        }
        handAnim.SetFloat("XVelocity", xVelocity);
        handAnim.SetFloat("YVelocity", yVelocity);
        handAnim.SetBool("IsGrounded", grounded);

    }

    private void DoCrouch()
    {
        if (inputActions.PlayerControls.Crouch.ReadValue<float>() > 0)
        {
            controller.height = crouchHeight;
        }
        else
        {
            if (!Physics.Raycast(transform.position, Vector3.up, 2.0f))
            {
                controller.height = initHeight;
            }
        }
    }

    private void OnDisable()
    {
        inputActions.Disable();
    }

    public Vector2 GetPlayerMovement()
    {
        return inputActions.PlayerControls.Move.ReadValue<Vector2>();
    }

    public Vector2 GetPlayerLook()
    {
        return inputActions.PlayerControls.Look.ReadValue<Vector2>();
    }

    public void ChangeLayerRecursively(GameObject currentGameObject, int newLayer)
    {
        currentGameObject.layer = newLayer;

        foreach (Transform child in currentGameObject.transform)
        {
            ChangeLayerRecursively(child.gameObject, newLayer);
        }
    }
}
