using UnityEngine;
using UnityEngine.InputSystem;

public partial class TutorialManager
{
    private InputAction moveAction, lookAction, runAction, jumpAction, crouchAction;

    private void CacheMovementInputActions()
    {
        moveAction = inputActions.FindAction("Move");
        lookAction = inputActions.FindAction("Look");
        runAction = inputActions.FindAction("Run");
        jumpAction = inputActions.FindAction("Jump");
        crouchAction = inputActions.FindAction("Crouch");
    }

    private void DoLooking()
    {
        Vector2 looking = GetPlayerLook();
        if (looking.magnitude > 0.1f)
        {
            if (!looked)
            {
                looked = true;
                OnLook?.Invoke();
            }
        }
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

        // Handle gravity and grounded state
        if (grounded && velocity.y < 0)
        {
            velocity.y = -2f;
        }

        Vector2 movement = GetPlayerMovement();
        if (moved && runAction.ReadValue<float>() > 0 && movement.y > 0 && !isCrouched)
        {
            speedMultiplier = 2.0f;
            if (!sprinted)
            {
                sprinted = true;
                OnSprint?.Invoke();
            }
        }
        else
        {
            speedMultiplier = 1.0f;
        }
        if (movement.magnitude > 0.1f)
        {
            if (!moved)
            {
                moved = true;
                OnMove?.Invoke();
            }
        }

        if (isSliding && isOnIce)
        {
            HandleSliding();
        }
        else
        {
            EndSliding();
            Vector3 move = transform.right * movement.x + transform.forward * movement.y;

            if (isOnIce)
            {
                // Ice sliding with movement control
                velocity.x = Mathf.Lerp(velocity.x, move.x * movementSpeed * speedMultiplier * 1.2f, Time.deltaTime * iceFriction);
                velocity.z = Mathf.Lerp(velocity.z, move.z * movementSpeed * speedMultiplier * 1.2f, Time.deltaTime * iceFriction);
            }
            else
            {
                // Regular movement
                velocity.x = movementSpeed * speedMultiplier * move.x;
                velocity.z = movementSpeed * speedMultiplier * move.z;
            }
        }

        // Handle jumping
        if (sprinted && grounded && jumpAction.triggered && !isCrouched && !isSliding)
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
            jumpImpulseSource.GenerateImpulse();
            if (!jumped)
            {
                jumped = true;
                OnJump?.Invoke();
            }

            if (isOnIce && !isSliding)
            {
                StartSliding();
            }
        }

        // Apply gravity
        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);

        // Update animator
        velocityX = Mathf.Lerp(velocityX, realMovementSpeed > 1.2 ? movement.x : 0, 10f * Time.deltaTime);
        velocityZ = Mathf.Lerp(velocityZ, realMovementSpeed > 1.2 ? (movement.y * speedMultiplier) : 0, 10f * Time.deltaTime);
        UpdateAnimator(velocityX, velocityZ);
    }

    private void HandleSliding()
    {
        if (velocity.y <= 0.5f)
        {
            if (velocity.x < slidingStopThreshold && velocity.z < slidingStopThreshold)
            {
                EndSliding();
                return;
            }
            // Decelerate sliding
            velocity.x *= slidingFriction * slidingSpeedMultiplier;
            velocity.z *= slidingFriction * slidingSpeedMultiplier;
            rig.weight = Mathf.Lerp(rig.weight, 0.1f, Time.deltaTime * 5f);

            slidingSpeedMultiplier = 1f;
        }
    }
    private void StartSliding()
    {
        isSliding = true;

        if (!slid)
        {
            slid = true;
            OnSlide?.Invoke();
        }

        controller.height = slidingHeight;
        legs.SetActive(false);
        FPShadow.SetActive(false);
        Hands.SetActive(false);
        slidingCam.SetActive(true);
    }
    private void EndSliding()
    {
        isSliding = false;
        slidingSpeedMultiplier = 2.5f;

        rig.weight = Mathf.Lerp(rig.weight, 1, Time.deltaTime * 5f);
        legs.SetActive(true);
        FPShadow.SetActive(true);
        Hands.SetActive(true);
        slidingCam.SetActive(false);
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
            animator.SetBool("IsSliding", isSliding);

            animator.SetBool("HaveAGun", haveGun);
        }
    }

    private void DoCrouch()
    {
        if (!isSliding)
        {
            if (crouchAction.ReadValue<float>() > 0)
            {
                controller.height = crouchHeight;
                isCrouched = true;

                if (!crouched)
                {
                    crouched = true;
                    OnCrouch?.Invoke();
                }
            }
            else
            {
                if (!Physics.Raycast(transform.position, Vector3.up, 2.0f))
                {
                    controller.height = initHeight;
                    isCrouched = false;
                }
            }
        }
    }

    public Vector2 GetPlayerMovement()
    {
        return moveAction.ReadValue<Vector2>();
    }

    public Vector2 GetPlayerLook()
    {
        return lookAction.ReadValue<Vector2>();
    }
}
