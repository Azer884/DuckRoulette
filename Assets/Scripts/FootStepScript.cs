using UnityEngine;
using Unity.Netcode;

public class FootStepScript : NetworkBehaviour {
    public float stepRate = 0.5f;
    public float stepCoolDown;
    public Movement movement;
    public AudioSource footstepSource;
    public AudioClip[] footstepClips;
    
    #region Input Things
    private PlayerInput inputActions;
    private void Awake()
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
    #endregion

    // NetworkVariable to track the timestamp of the last footstep
    private NetworkVariable<bool> isWalking = new(
        false, 
        NetworkVariableReadPermission.Everyone, 
        NetworkVariableWritePermission.Owner
    );

    void Update () {
        // Adjust step rate based on movement speed
        if (IsOwner && movement.speedMultiplier > 1) {
            stepRate = 0.35f;
        } else {
            stepRate = 0.5f;
        }

        stepCoolDown -= Time.deltaTime;
        isWalking.Value = IsOwner && (inputActions.PlayerControls.Move.ReadValue<Vector2>().x > 0 || inputActions.PlayerControls.Move.ReadValue<Vector2>().y > 0);
        // Only the owning player can update their own footsteps
        if (isWalking.Value && stepCoolDown < 0f) 
        {
            PlayFootstep();
            stepCoolDown = stepRate;
        }
    }

    private void PlayFootstep() {
        footstepSource.pitch = 1f + Random.Range(-0.2f, 0.2f);
        int index = Random.Range(0, footstepClips.Length);
        footstepSource.PlayOneShot(footstepClips[index], 0.9f);
    }
}
