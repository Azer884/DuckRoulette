using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class Movement : MonoBehaviour
{
    private PlayerInput inputActions;
    private CharacterController controller;
    
    [SerializeField] private Camera cam;
    [SerializeField] private float movementSpeed = 2.0f;
    [SerializeField] public float lookSensitivity = 1.0f;
    private float xRotation = 0f;

    // Movement Vars
    private Vector3 velocity;
    public float gravity = -9.81f;
    private bool grounded;
    private float speedMultiplier = 1.0f;
    [SerializeField] private float jumpHeight = 1.5f;

    // Animation Vars
    [SerializeField] private Animator[] animators;
    [SerializeField] private float velocityX = 0f;
    [SerializeField] private float velocityZ = 0f;

    // Crouch Vars
    private float initHeight;
    [SerializeField] private float crouchHeight;

    private void Awake()
    {
        inputActions = new PlayerInput();
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
        DoLooking();
        DoCrouch();
    }

    private void DoLooking()
    {
        Vector2 looking = GetPlayerLook();
        float lookX = looking.x * lookSensitivity * Time.deltaTime;
        float lookY = looking.y * lookSensitivity * Time.deltaTime;

        xRotation -= lookY;
        xRotation = Mathf.Clamp(xRotation, -85f, 75f);

        cam.transform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
        transform.Rotate(Vector3.up * lookX);
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
            PlayJumpAnimation();
        }
        

        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);

        velocityX = Mathf.Lerp(velocityX, movement.x, 10f * Time.deltaTime);
        velocityZ = Mathf.Lerp(velocityZ, movement.y * speedMultiplier, 10f * Time.deltaTime);
        UpdateAnimator(velocityX, velocityZ);
    }

    private void PlayJumpAnimation()
    {
        foreach (Animator animator in animators)
        {
            animator.Play("Jump");
        }
    }

    private void UpdateAnimator(float xVelocity, float yVelocity)
    {
        foreach (Animator animator in animators)
        {
            animator.SetFloat("XVelocity", xVelocity);
            animator.SetFloat("YVelocity", yVelocity);
            animator.SetBool("IsGrounded", grounded);
        }
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
}
