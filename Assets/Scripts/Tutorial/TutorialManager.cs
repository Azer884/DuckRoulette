using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.Audio;
using UnityEngine.InputSystem;

public class TutorialManager : MonoBehaviour
{
    public event Action OnLook, OnMove, OnSprint, OnJump, OnPickUp, OnThrow, OnShutDown, OnCrouch, OnSlide, OnSwitchToGun, OnReload, OnTrigger, OnGunShot, OnTeamUp, OnTalk, OnEndTeamUp, OnSlap;
    [HideInInspector] public bool looked, moved, sprinted, jumped, pickedUp, thrown, shutDown, crouched, slid, switchedToGun, reloaded, triggered, gunShot, teamedUp, talked, endedTeamUp, slapped;

    private InputActionAsset inputActions; // Use InputActionAsset from RebindSaveLoad
    public static TutorialManager Instance { get; private set; }
    private CharacterController controller;

    [SerializeField] private Transform camHolder;
    [SerializeField] private float movementSpeed = 2.0f;
    [SerializeField] private float lookSensitivity = 1.0f;
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

    private Vector3 lastPosition; // To store the last frame's position
    [HideInInspector] public float realMovementSpeed;  // To store the calculated speed

    public CinemachineImpulseSource jumpImpulseSource;


    private bool isOnIce = false; // Check if the player is on ice
    private bool isSliding = false;
    [SerializeField] private float iceFriction = 0.98f; // Ice friction (less than 1 for sliding)
    [SerializeField] private float slidingSpeedMultiplier = 7f; // Speed boost during tobogganing
    [SerializeField] private float slidingFriction = 0.95f; // Friction for sliding deceleration
    [SerializeField] private float slidingStopThreshold = 0.1f; // Minimum velocity to stop sliding
    [SerializeField] private float slidingHeight = 0.5f;
    [SerializeField] private GameObject slidingCam;
    [SerializeField] private Rig rig;
    [SerializeField] private GameObject pauseMenu, crosshair;
    private bool isPaused = false;

    float mouseXSmooth = 0f;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
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
    public void Pause(bool state)
    {
        isPaused = state;
        pauseMenu.SetActive(state);
        crosshair.SetActive(!state);
        if (state)
        {
            Cursor.lockState = CursorLockMode.None; // Unlock the cursor
            Time.timeScale = 0; // Pause the game
        }
        else
        {
            Cursor.lockState = CursorLockMode.Locked; // Lock the cursor again
            Time.timeScale = 1; // Resume the game
        }
    }

    private void Update()
    {
        DoLooking();
        if (looked) DoMovement();
        if (jumped) PickUpThrowShut();
        if (shutDown) DoCrouch();
        if (slid && canSwitch)
        {
            if (inputActions.FindAction("Change Weapon").triggered && !onlySlap)
            {
                haveGun = !haveGun;
                SwitchParent(haveGun);
            }
        }
        if (haveGun && !switchedToGun)
        {
            switchedToGun = true;
            OnSwitchToGun?.Invoke();
        }
        if (switchedToGun && haveGun)
        {
            Reload();
            Trigger();
            Shoot();
        }
        if (gunShot) TeamUp();
        if (teamedUp && Input.GetKeyDown(KeyCode.V))
        {
            if (!talked)
            {
                talked = true;
                OnTalk?.Invoke();
            }
        }
        if (!haveGun && endedTeamUp)
        {
            Slap();
        }

        if (inputActions.FindAction("Pause").triggered)
        {
            Pause(isPaused);
            isPaused = !isPaused;
        }

        Vector3 currentPos = transform.position;
        Vector3 deltaPosition = currentPos - lastPosition;
        realMovementSpeed = deltaPosition.magnitude / Time.deltaTime;
        lastPosition = currentPos;
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


    // Update the OnControllerColliderHit to detect ice
    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (hit.gameObject.CompareTag("Ice"))
        {
            isOnIce = true;
        }
        else
        {
            isOnIce = false;
        }
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
        if (inputActions.FindAction("Run").ReadValue<float>() > 0 && movement.y > 0 && !isCrouched)
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
        if (grounded && inputActions.FindAction("Jump").triggered && !isCrouched && !isSliding)
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
        handAnim.SetFloat("XVelocity", xVelocity);
        handAnim.SetFloat("YVelocity", yVelocity);
        handAnim.SetBool("IsGrounded", grounded);
        handAnim.SetBool("IsSliding", isSliding);
    }

    private void DoCrouch()
    {
        if (!isSliding)
        {
            if (inputActions.FindAction("Crouch").ReadValue<float>() > 0)
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
        return inputActions.FindAction("Move").ReadValue<Vector2>();
    }

    public Vector2 GetPlayerLook()
    {
        return inputActions.FindAction("Look").ReadValue<Vector2>();
    }


    public GameObject bulletPrefab;
    private GameObject bullet;
    public Transform spawnPt;
    public Animator bulletAnimator;
    public GameObject gun, shadowGun;
    public Transform withGunParent, withoutGunParent;
    public bool canTrigger, canShoot, isTriggered, isReloaded;
    private bool haveGun = false, onlySlap = false;
    private bool canSwitch = true;
    private int bulletPos = 0;


    private void Reload()
    {
        if (inputActions.FindAction("Reload").triggered && canShoot && !isReloaded)
        {
            isReloaded = true;
            bulletPos = 0;
            foreach (Animator animator in animators)
            {
                animator.Play("Reload");
            }
            if (!reloaded)
            {
                reloaded = true;
                OnReload?.Invoke();
            }
            bulletAnimator.Play("Reload");
        }
        if (animators[2].GetCurrentAnimatorStateInfo(0).IsName("Reload"))
        {
            canTrigger = false;
            canSwitch = false;
        }
        else
        {
            canTrigger = true;
            canSwitch = true;
        }
    }
    private void Trigger()
    {
        if (inputActions.FindAction("Trigger").triggered && !isTriggered && canTrigger && canShoot && isReloaded)
        {
            isTriggered = true;
            foreach (Animator animator in animators)
            {
                animator.SetBool("Triggered", isTriggered);
            }
            if (!triggered)
            {
                triggered = true;
                OnTrigger?.Invoke();
            }
        }
        if (animators[3].GetCurrentAnimatorStateInfo(0).IsName("Trigger"))
        {
            canShoot = false;
        }
        else
        {
            canShoot = true;
        }
    }

    private void Shoot()
    {
        if (inputActions.FindAction("Shoot").triggered && canShoot && isTriggered && isReloaded)
        {
            if (bulletPos == 1)
            {
                foreach (Animator animator in animators)
                {
                    animator.Play("Shooting");
                }

                if (!gunShot)
                {
                    gunShot = true;
                    onlySlap = true;
                    haveGun = false;
                    SwitchParent(false);
                    OnGunShot?.Invoke();
                }
                Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
                Vector3 pos;
                if (Physics.Raycast(ray, out RaycastHit hit))
                {
                    pos = hit.point;
                }
                else
                {
                    pos = ray.GetPoint(100f);
                }

                ShootServerRpc(spawnPt.position, Quaternion.identity, pos);

            }
            bulletPos++;

            isTriggered = false;
            foreach (Animator animator in animators)
            {
                animator.SetBool("Triggered", isTriggered);
            }
        }
    }
    public void ShootServerRpc(Vector3 spawnPoint, Quaternion rot, Vector3 targetAim)
    {
        bullet = Instantiate(bulletPrefab, spawnPoint, rot);

        Vector3 direction = (targetAim - spawnPoint).normalized;

        if (bullet.TryGetComponent(out Rigidbody rb))
        {
            rb.linearVelocity = direction * 15f;
        }
    }
    public void SwitchParent(bool state)
    {
        gun.SetActive(state);
        shadowGun.SetActive(state);
        if (state)
        {
            Hands.transform.parent = withGunParent;
            Hands.transform.localPosition = Vector3.zero;
            Hands.transform.localRotation = Quaternion.identity;
        }
        else
        {
            Hands.transform.parent = withoutGunParent;
            Hands.transform.localPosition = Vector3.zero;
            Hands.transform.localRotation = Quaternion.identity;
        }
    }






    [SerializeField] private Transform slapArea;
    [SerializeField] private float slapRaduis;
    [SerializeField] private float slapCoolDown = 1f;
    [SerializeField] private LayerMask otherPlayers;
    private Collider[] slapResults = new Collider[10];
    private bool canSlap = true;

    // Stun related variables
    private int slapCount = 0;
    private int slapLimit = 3;
    public AudioSource slapAudio;

    private void Slap()
    {
        if (inputActions.FindAction("Slap").triggered && canSlap)
        {
            foreach (Animator anim in animators)
            {
                anim.SetTrigger("Slap");
            }
            TryToSlap();

            canSlap = false;
            StartCoroutine(Timer(slapCoolDown));
        }
    }

    private IEnumerator Timer(float waitTime)
    {
        yield return new WaitForSeconds(waitTime);
        canSlap = true;
    }

    private void TryToSlap()
    {
        int numColliders = Physics.OverlapSphereNonAlloc(slapArea.position, slapRaduis, slapResults, otherPlayers);

        if (numColliders > 0)
        {
            slapAudio.Play();
            slapCount++;

            if (slapCount >= slapLimit)
            {
                slapResults[0].GetComponent<TutorialRagdoll>().EnableRagdoll();
                if (!slapped)
                {
                    slapped = true;
                    OnSlap?.Invoke();
                }

                slapCount = 0;
            }
        }
    }



    private List<GameObject> validPlayers = new();
    public bool isTeamedUp = false;
    public GameObject teamMate;
    public float teamUpRaduis = 2f;
    public Transform teamUpArea;
    Collider[] teamUpResults = new Collider[10];
    public AudioClip dapSound;
    public AudioClip perfectDapSound;
    private int perfectDap = 0;
    public Transform dapPosition;
    public AudioMixerGroup audioMixerGroup;
    public Color teamColor = Color.green;

    void TeamUp()
    {
        if (isTeamedUp)
        {
            if (Input.GetKeyDown(KeyCode.X))
            {
                MessageBox.Informate("You have ended the team up with TutoBot", Color.red, MessagePriority.High);
                if (!endedTeamUp)
                {
                    endedTeamUp = true;
                    OnEndTeamUp?.Invoke();
                }

                EndTeamUp();
                if (teamMate != null)
                {
                    RemoveTeamMate();
                }
            }

            return;
        }

        TryToTeamUp();
    }

    private void TryToTeamUp()
    {
        int numColliders = Physics.OverlapSphereNonAlloc(teamUpArea.position, teamUpRaduis, teamUpResults, otherPlayers);
        validPlayers.Clear();

        for (int i = 0; i < numColliders; i++)
        {
            if (teamUpResults[i].GetComponent<TutoBot>())
            {
                validPlayers.Add(teamUpResults[i].gameObject);
            }
        }
        if (validPlayers?.Count > 0)
        {
            if (Input.GetKeyDown(KeyCode.E))
            {
                if (!isTeamedUp)
                {
                    isTeamedUp = true;
                    if (!teamedUp)
                    {
                        teamedUp = true;
                        OnTeamUp?.Invoke();
                    }
                    MessageBox.Informate("You have teamed up with TutoBot", Color.green);
                    AddTeamMate();
                    PlayDapSound(dapPosition.position, perfectDap == 1);
                }

            }
            MessageBox.Informate("Press E to team up with TutoBot ", Color.white, MessagePriority.Low, 0.5f);
        }
    }
    public void EndTeamUp()
    {
        isTeamedUp = false;
    }

    public void PlayDapSound(Vector3 dapPosition, bool perfectDap)
    {
        AudioClip clipToPlay = perfectDap ? perfectDapSound : dapSound;

        // Create a temporary GameObject with an AudioSource
        GameObject audioObject = new("TempAudio");
        audioObject.transform.position = dapPosition;
        AudioSource audioSource = audioObject.AddComponent<AudioSource>();
        audioSource.outputAudioMixerGroup = audioMixerGroup;

        // Set the clip and adjust the pitch for variety
        audioSource.clip = clipToPlay;
        audioSource.spatialBlend = 1.0f; // Make the sound 3D
        audioSource.pitch = UnityEngine.Random.Range(0.9f, 1.1f); // Randomize the pitch slightly

        // Play the sound and destroy the GameObject after the clip duration
        audioSource.Play();
        Destroy(audioObject, clipToPlay.length);
    }

    public void AddTeamMate()
    {
        teamMate = validPlayers[0];
        teamMate.GetComponent<TutoBot>().Accept(teamColor);
    }

    public void RemoveTeamMate()
    {
        teamMate.GetComponentInChildren<TutoBot>().Accept(Color.black);
        teamMate = null;
    }
    


    [SerializeField] private LayerMask pickUpLayerMask;
    [SerializeField] private float maxDistance = 5f;
    public Transform bumBoxPickUpPosition;
    public Transform pickedUpObject;

    // Update is called once per frame
    void PickUpThrowShut()
    {
        if (Physics.Raycast(Camera.main.transform.position, Camera.main.transform.forward, out RaycastHit hit, maxDistance, pickUpLayerMask))
        {
            if (Input.GetKeyDown(KeyCode.E))
            {
                if (pickedUpObject == null)
                {
                    if (haveGun) return;
                    
                    PickUpObject(hit.collider);
                }
            }

            if (Input.GetKeyDown(KeyCode.F))
            {
                TryToMute(hit.transform);
            }
        }

        else if (pickedUpObject != null)
        {
            pickedUpObject.transform.SetPositionAndRotation(bumBoxPickUpPosition.position, bumBoxPickUpPosition.rotation);
            if (Input.GetKeyDown(KeyCode.E))
            {
                DropObject();
            }
            if (Input.GetKeyDown(KeyCode.F))
            {
                TryToMute(pickedUpObject);
            }

        }
    }

    private void PickUpObject(Collider collider)
    {
        if(collider.TryGetComponent(out IInteractable interactable) && !interactable.IsHeld)
        {
            interactable.Interact(0);

            if (interactable.IsPickable)
            {
                if (!pickedUp)
                {
                    pickedUp = true;
                    OnPickUp?.Invoke();
                }
                pickedUpObject = collider.transform;
            }
        }
    }

    private void DropObject()
    {
        pickedUpObject.GetComponent<IInteractable>().Drop();

        if (pickedUpObject.GetComponent<IInteractable>().IsPickable)
        {
            pickedUpObject.gameObject.SetActive(true);
            pickedUpObject = null;
            if(!thrown)
            {
                thrown = true;
                OnThrow?.Invoke();
            }
        }
    }

    private void TryToMute(Transform obj)
    {
        if (obj.TryGetComponent(out OfflineBumBox bumBox))
        {
            bumBox.Mute();
            if(!shutDown)
            {
                shutDown = true;
                OnShutDown?.Invoke();
            }
        }
    }
}
