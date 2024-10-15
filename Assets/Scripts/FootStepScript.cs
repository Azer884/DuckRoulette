using UnityEngine;
using Unity.Netcode;
using UnityEngine.InputSystem;

public class FootStepScript : NetworkBehaviour {
    public float stepRate = 0.5f;
    public float stepCoolDown;
    public Movement movement;
    public AudioSource footstepSource;
    public AudioClip[] footstepClips;
    
    #region Input Things
    private InputActionAsset inputActions;
    private void Awake()
    {
        inputActions = RebindSaveLoad.Instance.actions;
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
        isWalking.Value = IsOwner && (Input.GetAxis("Horizontal") != 0f || Input.GetAxis("Vertical") != 0f);
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
