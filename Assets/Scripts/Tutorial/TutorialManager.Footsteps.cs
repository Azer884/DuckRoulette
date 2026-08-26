using UnityEngine;

public partial class TutorialManager
{
    public float stepRate = 0.5f;
    public float stepCoolDown;
    public AudioSource footstepSource;

    private void PlayFootstep()
    {
        // Movement state now lives on the shared Movement component.
        float currentSpeedMultiplier = movementComp != null ? movementComp.speedMultiplier : 1f;
        stepRate = currentSpeedMultiplier > 1 ? 0.35f : 0.5f;

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
            stepCoolDown = stepRate;
        }
    }
}
