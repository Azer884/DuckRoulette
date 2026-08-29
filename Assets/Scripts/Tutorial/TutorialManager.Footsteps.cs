using UnityEngine;

public partial class TutorialManager
{
    public float stepRate = 0.5f;
    public float stepCoolDown;
    public AudioSource footstepSource;

    // FootStepScript's own walk/run shake never fires offline - its Update() is gated on IsOwner,
    // which Netcode only ever sets during a real spawn/ownership assignment that never happens in
    // Tutorial (no NetworkManager session). Movement's jump/parry shake avoids that trap by never
    // checking IsOwner in the first place; mirror that here instead of touching FootStepScript's
    // owner gate, which the networked game still needs.
    public ShakeProfile walkShakeProfile;
    public ShakeProfile runShakeProfile;
    private CameraShaker cameraShaker;

    private void PlayFootstep()
    {
        // Movement state now lives on the shared Movement component.
        float currentSpeedMultiplier = movementComp != null ? movementComp.speedMultiplier : 1f;
        bool isRunning = currentSpeedMultiplier > 1;
        stepRate = isRunning ? 0.35f : 0.5f;

        stepCoolDown -= Time.deltaTime;
        // Only the owning player can trigger their own footsteps
        bool hasMoveInput = movementComp != null && movementComp.GetPlayerMovement() != Vector2.zero;
        if (hasMoveInput && realMovementSpeed > 1.2f && controller.isGrounded && stepCoolDown < 0f)
        {
            AudioClip clip = SFXManager.Instance != null ? SFXManager.Instance.RandomFootstep() : null;
            if (clip != null)
            {
                footstepSource.pitch = 1f + Random.Range(-0.2f, 0.2f);
                footstepSource.PlayOneShot(clip, 0.9f);
            }

            if (cameraShaker == null)
            {
                cameraShaker = CameraShaker.GetOrAdd(gameObject);
            }
            cameraShaker.Shake(isRunning ? runShakeProfile : walkShakeProfile);

            stepCoolDown = stepRate;
        }
    }
}
