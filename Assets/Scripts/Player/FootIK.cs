using UnityEngine;

/// <summary>
/// Plants the feet of a humanoid on uneven ground. The ground straight under the character's
/// capsule is the reference the animation was authored against; each foot is raycast down and
/// moved up or down by how much higher or lower the ground under it is than that reference, and
/// tilted to its slope. The hips are lowered by however far the lower foot has to reach, so
/// neither leg over-stretches on a step or slope.
///
/// Relative on purpose: the three rigs on the player (first-person legs, shadow body, third-person
/// body) each sit at a different height inside the capsule, so an absolute "ankle above ground"
/// target would need tuning per rig. Moving by the ground difference keeps whatever the animation
/// does - a stepping foot stays lifted, a crouch stays low - and only adds the terrain.
///
/// Purely cosmetic and local. It never touches the CharacterController - the old version
/// shrank <c>controller.height</c> every idle frame (reading it back and subtracting again, so it
/// kept shrinking), which fought Movement's crouch and slide heights and broke the ground check
/// that is derived from the capsule height. Crouch, slide and jump now own the capsule alone.
///
/// Works on every peer, owner or not: grounding and speed are measured from the transform, not
/// from the CharacterController, whose velocity is zero on remote copies.
///
/// Needs a Humanoid avatar and "IK Pass" ticked on at least one layer of the animator
/// controller, or Unity never calls OnAnimatorIK.
/// </summary>
[RequireComponent(typeof(Animator))]
public class FootIK : MonoBehaviour
{
    [Tooltip("Optional. Only used to show which bones are driven; the humanoid avatar supplies the feet.")]
    public Transform LeftFoot = null;
    public Transform RightFoot = null;

    [SerializeField, Tooltip("Layers the feet stand on.")]
    private LayerMask layerMask = 1;

    [Header("Reach")]
    [SerializeField, Tooltip("Highest step up a foot will climb onto.")]
    private float rayAbove = 0.5f;

    [SerializeField, Tooltip("Deepest step down a foot will reach for.")]
    private float rayBelow = 0.45f;

    [SerializeField, Tooltip("Most the hips are lowered to let the lower foot reach the ground.")]
    private float maxPelvisDrop = 0.35f;

    [Header("Weights")]
    [SerializeField, Range(0f, 1f), Tooltip("IK weight while standing still.")]
    private float idleWeight = 1f;

    [SerializeField, Range(0f, 1f), Tooltip("IK weight at full run. Lower keeps the animation's own " +
        "stride readable; the feet are still lifted over steps.")]
    private float movingWeight = 0.6f;

    [SerializeField, Tooltip("Planar speed (m/s) at which the moving weight is fully used.")]
    private float fullMovingSpeed = 4f;

    [SerializeField, Range(0f, 1f), Tooltip("How much of the IK weight also drives the foot rotation.")]
    private float rotationWeight = 0.8f;

    [SerializeField, Tooltip("Farther than this between the capsule bottom and the ground, the " +
        "character counts as airborne and IK fades out.")]
    private float groundedDistance = 0.25f;

    [Header("Smoothing")]
    [SerializeField, Tooltip("How fast the foot goals follow the ground. Higher is snappier.")]
    private float footSpeed = 18f;

    [SerializeField, Tooltip("How fast the hips follow.")]
    private float pelvisSpeed = 10f;

    [SerializeField, Tooltip("How fast the overall IK weight fades in and out (jumping, sliding).")]
    private float weightSpeed = 6f;

    private Animator animator;
    private Movement movement;
    private CharacterController controller;
    private bool usable;

    private float weight;
    private float pelvisOffset;
    private float leftOffset, rightOffset;
    private Vector3 leftNormal = Vector3.up, rightNormal = Vector3.up;
    private Vector3 lastPosition;
    private float planarSpeed;
    private bool grounded;
    private float referenceY;

    private int computedFrame = -1;
    private bool leftHit, rightHit;
    private Vector3 bodyBase;
    private Vector3 leftAnimated, rightAnimated;
    private Quaternion leftAnimatedRotation, rightAnimatedRotation;

    private void Awake()
    {
        animator = GetComponent<Animator>();
        movement = GetComponentInParent<Movement>();
        controller = GetComponentInParent<CharacterController>();
        lastPosition = transform.position;
    }

    private void OnEnable()
    {
        usable = animator != null && animator.isHuman;
        if (!usable)
        {
            Debug.LogWarning($"FootIK on {name}: needs a Humanoid avatar, disabling.", this);
            enabled = false;
        }

        weight = 0f;
        pelvisOffset = 0f;
        leftOffset = rightOffset = 0f;
        lastPosition = transform.position;
    }

    private void Update()
    {
        float deltaTime = Mathf.Max(Time.deltaTime, 0.0001f);
        Vector3 moved = transform.position - lastPosition;
        moved.y = 0f;
        // Smoothed so one hitchy frame does not yank the weight around.
        planarSpeed = Mathf.Lerp(planarSpeed, moved.magnitude / deltaTime, 1f - Mathf.Exp(-10f * deltaTime));
        lastPosition = transform.position;

        grounded = ProbeReference();
        float target = grounded && !IsSliding()
            ? Mathf.Lerp(idleWeight, movingWeight, Mathf.InverseLerp(0.1f, Mathf.Max(0.2f, fullMovingSpeed), planarSpeed))
            : 0f;
        weight = Mathf.MoveTowards(weight, target, weightSpeed * deltaTime);
    }

    private bool IsSliding() => movement != null && movement.IsSliding;

    // Ground straight under the capsule. Measured from the capsule's geometry rather than
    // CharacterController.isGrounded, which is only live on the owner.
    private bool ProbeReference()
    {
        Vector3 bottom;
        if (controller != null)
        {
            Transform body = controller.transform;
            bottom = body.TransformPoint(controller.center) - Vector3.up * (controller.height * 0.5f * body.lossyScale.y);
        }
        else
        {
            bottom = transform.position;
        }

        Vector3 origin = bottom + Vector3.up * rayAbove;
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, rayAbove + groundedDistance + rayBelow,
                layerMask, QueryTriggerInteraction.Ignore))
        {
            referenceY = hit.point.y;
            return hit.distance <= rayAbove + groundedDistance;
        }

        return false;
    }

    // Unity calls this once per layer that has IK Pass ticked. The ground probe runs once per
    // frame and every call just re-applies it, so extra IK layers cannot compound the offsets.
    private void OnAnimatorIK(int layerIndex)
    {
        if (!usable)
        {
            return;
        }

        if (computedFrame != Time.frameCount)
        {
            computedFrame = Time.frameCount;
            // Captured before anything is set, so a second IK layer pass starts from the same pose.
            bodyBase = animator.bodyPosition;
            leftAnimated = animator.GetIKPosition(AvatarIKGoal.LeftFoot);
            rightAnimated = animator.GetIKPosition(AvatarIKGoal.RightFoot);
            leftAnimatedRotation = animator.GetIKRotation(AvatarIKGoal.LeftFoot);
            rightAnimatedRotation = animator.GetIKRotation(AvatarIKGoal.RightFoot);
            Probe();
        }

        if (weight <= 0.001f)
        {
            animator.SetIKPositionWeight(AvatarIKGoal.LeftFoot, 0f);
            animator.SetIKPositionWeight(AvatarIKGoal.RightFoot, 0f);
            animator.SetIKRotationWeight(AvatarIKGoal.LeftFoot, 0f);
            animator.SetIKRotationWeight(AvatarIKGoal.RightFoot, 0f);
            return;
        }

        animator.bodyPosition = bodyBase + Vector3.up * (pelvisOffset * weight);

        ApplyFoot(AvatarIKGoal.LeftFoot, leftHit, leftAnimated + Vector3.up * leftOffset, leftAnimatedRotation, leftNormal);
        ApplyFoot(AvatarIKGoal.RightFoot, rightHit, rightAnimated + Vector3.up * rightOffset, rightAnimatedRotation, rightNormal);
    }

    private void Probe()
    {
        float deltaTime = Time.deltaTime;

        float leftTarget = ProbeFoot(leftAnimated, out leftHit, ref leftNormal, deltaTime);
        float rightTarget = ProbeFoot(rightAnimated, out rightHit, ref rightNormal, deltaTime);

        leftOffset = Mathf.Lerp(leftOffset, leftTarget, 1f - Mathf.Exp(-footSpeed * deltaTime));
        rightOffset = Mathf.Lerp(rightOffset, rightTarget, 1f - Mathf.Exp(-footSpeed * deltaTime));

        // Lower the hips only - by however far the lower foot has to reach down. Raising them
        // would lift the whole body off the capsule.
        float pelvisTarget = Mathf.Clamp(Mathf.Min(leftTarget, rightTarget, 0f), -maxPelvisDrop, 0f);
        pelvisOffset = Mathf.Lerp(pelvisOffset, pelvisTarget, 1f - Mathf.Exp(-pelvisSpeed * deltaTime));
    }

    // How far the ground under this foot sits above (+) or below (-) the ground under the
    // capsule - which is exactly how far the animated foot has to move to stand on it.
    private float ProbeFoot(Vector3 animated, out bool hit, ref Vector3 normal, float deltaTime)
    {
        float blend = 1f - Mathf.Exp(-footSpeed * deltaTime);

        if (grounded)
        {
            Vector3 origin = new(animated.x, referenceY + rayAbove, animated.z);
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit ground, rayAbove + rayBelow, layerMask,
                    QueryTriggerInteraction.Ignore))
            {
                hit = true;
                normal = Vector3.Slerp(normal, ground.normal, blend);
                return ground.point.y - referenceY;
            }
        }

        hit = false;
        normal = Vector3.Slerp(normal, Vector3.up, blend);
        return 0f;
    }

    private void ApplyFoot(AvatarIKGoal goal, bool hit, Vector3 position, Quaternion animatedRotation, Vector3 normal)
    {
        float footWeight = hit ? weight : 0f;

        animator.SetIKPositionWeight(goal, footWeight);
        animator.SetIKPosition(goal, position);

        animator.SetIKRotationWeight(goal, footWeight * rotationWeight);
        animator.SetIKRotation(goal, Quaternion.FromToRotation(Vector3.up, normal) * animatedRotation);
    }
}
