using System.Collections.Generic;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class Movement : NetworkBehaviour
{
    private InputActionAsset inputActions; // Use InputActionAsset from RebindSaveLoad
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
    public float initHeight;
    public float crouchHeight;
    public bool isCrouched;

    [SerializeField] private Animator[] animators;
    [SerializeField] private Animator handAnim;
    [SerializeField] private float velocityX = 0f;
    [SerializeField] private float velocityZ = 0f;

    [SerializeField] private GameObject legs;
    [SerializeField] private GameObject FPShadow;
    [SerializeField] private GameObject Hands;
    [SerializeField] private GameObject fullBody;

    private Vector3 lastPosition; // To store the last frame's position
    [HideInInspector]public float realMovementSpeed;  // To store the calculated speed
    
    public CinemachineImpulseSource jumpImpulseSource;

    float mouseXSmooth = 0f;

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
        inputActions = GetComponent<InputSystem>().inputActions;
        initHeight = controller.height;
        Cursor.lockState = CursorLockMode.Locked;

        lastPosition = transform.position;
    }

    private void Update()
    {
        DoMovement();
        DoCrouch();
        DoLooking();
        Vector3 currentPos = transform.position;
        Vector3 deltaPosition = currentPos - lastPosition;
        realMovementSpeed = deltaPosition.magnitude / Time.deltaTime;
        lastPosition = currentPos;
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

        mouseXSmooth = Mathf.Lerp(mouseXSmooth, looking.x / 20, 4 * Time.deltaTime);
        mouseXSmooth = Mathf.Clamp(mouseXSmooth, -1, 1);
    }

    private void DoMovement()
    {
        grounded = controller.isGrounded;
        if (grounded && velocity.y < 0)
        {
            velocity.y = -2f;
        }

        Vector2 movement = GetPlayerMovement();
        speedMultiplier = inputActions.FindAction("Run").ReadValue<float>() > 0 && movement.y > 0 && isCrouched ? 2.0f : 1.0f;

        Vector3 move = transform.right * movement.x + transform.forward * movement.y;
        controller.Move(movementSpeed * speedMultiplier * Time.deltaTime * move);

        // Jumping
        if (grounded && inputActions.FindAction("Jump").triggered)
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
            jumpImpulseSource.GenerateImpulse();
        }

        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);

        velocityX = Mathf.Lerp(velocityX, realMovementSpeed > 1.2 ? movement.x : 0 , 10f * Time.deltaTime);
        velocityZ = Mathf.Lerp(velocityZ, realMovementSpeed > 1.2 ? (movement.y * speedMultiplier) : 0, 10f * Time.deltaTime);
        UpdateAnimator(velocityX, velocityZ);
    }

    private void UpdateAnimator(float xVelocity, float yVelocity)
    {
        foreach (Animator animator in animators)
        {
            animator.SetFloat("XVelocity", xVelocity);
            animator.SetFloat("YVelocity", yVelocity);
            animator.SetBool("IsGrounded", grounded);
            animator.SetBool("IsCrouched", isCrouched);
            animator.SetFloat("Turning", mouseXSmooth);
        }
        handAnim.SetFloat("XVelocity", xVelocity);
        handAnim.SetFloat("YVelocity", yVelocity);
        handAnim.SetBool("IsGrounded", grounded);
    }

    private void DoCrouch()
    {
        if (inputActions.FindAction("Crouch").ReadValue<float>() > 0)
        {
            controller.height = crouchHeight;
            isCrouched = true;
        }
        else
        {
            if (!Physics.Raycast(transform.position, Vector3.up, 2.0f))
            {
                controller.height = initHeight;
            }
            isCrouched = false;
        }
    }

    public Vector2 GetPlayerMovement()
    {
        return inputActions.FindAction("Move").ReadValue<Vector2>();
    }

    public Vector2 GetPlayerLook()
    {
        return inputActions.FindAction("Look").ReadValue<Vector2>();
    }

    public static void ChangeLayerRecursively(GameObject currentGameObject, int newLayer)
    {
        currentGameObject.layer = newLayer;

        foreach (Transform child in currentGameObject.transform)
        {
            ChangeLayerRecursively(child.gameObject, newLayer);
        }
    }
}
