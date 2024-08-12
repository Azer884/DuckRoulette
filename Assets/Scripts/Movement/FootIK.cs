using UnityEngine;

[RequireComponent(typeof(Animator))]
public class FootIK : MonoBehaviour {

	protected Animator animator;
	public Transform LeftFoot = null;
	public Transform RightFoot = null;
	public float footOffset;

	//length of the linecast
	float legDistance;
	//
	int layerMask = 1 << 8;
	CharacterController controller;

	float LeftFootY, RightFootY;
	private float colliderHeight;
	public float deltaAmplifier = 1f;

	public float leftFootWeight, rightFootWeight;

    void Start ()
	{
		animator = GetComponent<Animator> ();
		controller = transform.parent.GetComponent<CharacterController> ();

		//hit all layers but the players layer
		layerMask = ~layerMask;
		colliderHeight = controller.height;
		//controllerBoundsBottom = controller.bounds.extents.y;
	}

	void Update()
	{
		handleColliderOffset();
	}

	void OnAnimatorIK(int layerIndex = 2)
	{
		if(animator) {

            if(LeftFoot != null) {
                SolveIK (ref LeftFoot);
            }

            if(RightFoot != null) {
                SolveIK (ref RightFoot);
            }
		}
	}

	private void SolveIK(ref Transform foot)
	{
		string footName = foot.name;
        _ = new RaycastHit();
        _ = new
        Vector3();
        _ = Quaternion.identity;
        if (Physics.Linecast(CheckOrigin(foot.position), CheckTarget(foot.position), out RaycastHit floorHit, layerMask))
        {
            Vector3 newPosition = FootPosition(floorHit);
            Quaternion newRotation = FootRotation(floorHit);

            if (Equals(footName, LeftFoot.name))
            {
                animator.SetIKPositionWeight(AvatarIKGoal.LeftFoot, leftFootWeight);
                animator.SetIKPosition(AvatarIKGoal.LeftFoot, newPosition);

                LeftFootY = newPosition.y;

                animator.SetIKRotationWeight(AvatarIKGoal.LeftFoot, leftFootWeight);
                animator.SetIKRotation(AvatarIKGoal.LeftFoot, newRotation);
            }

            if (Equals(footName, RightFoot.name))
            {
                animator.SetIKPositionWeight(AvatarIKGoal.RightFoot, rightFootWeight);
                animator.SetIKPosition(AvatarIKGoal.RightFoot, newPosition);

                RightFootY = newPosition.y;

                animator.SetIKRotationWeight(AvatarIKGoal.RightFoot, rightFootWeight);
                animator.SetIKRotation(AvatarIKGoal.RightFoot, newRotation);
            }
        }
    }

	private void handleColliderOffset()
	{
		//this will change the length of the linecast based on the agents speed
		StateBasedLegDistance ();

		if (PlaneSpeed (ref controller) < 0.1f) {
			float delta = Mathf.Abs (LeftFootY - RightFootY);
			controller.height = colliderHeight - delta * deltaAmplifier;
			//controller.center = new Vector3(0, Mathf.Lerp(controller.center.y, colliderCenterY + delta, Time.deltaTime * smooth), 0);//new Vector3 (0, colliderCenterY + delta, 0);
		} else {
			controller.height = colliderHeight;
			//controller.center = new Vector3 (0, colliderCenterY, 0);
		}
	}

	private void StateBasedLegDistance()
	{
		if (controller) {
			legDistance = 1 / (PlaneSpeed (ref controller) + 0.8f);
		}
	}


	private float PlaneSpeed(ref CharacterController characterController)
	{
		Vector3 planeSpeed = new(characterController.velocity.x, 0, characterController.velocity.z);
		return planeSpeed.magnitude;
	}

	private Quaternion FootRotation(RaycastHit hit)
	{
		Quaternion footRotation = Quaternion.LookRotation( Vector3.ProjectOnPlane(transform.forward, hit.normal), hit.normal );
		return footRotation;
	}

	private Vector3 FootPosition(RaycastHit hit)
	{
		Vector3 displacement = hit.point;
		displacement.y += footOffset;
		return displacement;
	}

	private Vector3 CheckOrigin(Vector3 footPosition)
	{
		Vector3 origin = footPosition + ((legDistance + 0.25f) * Vector3.up);
		return origin;
	}

	private Vector3 CheckTarget(Vector3 footPosition)
	{
		Vector3 target = footPosition - (legDistance / 2f * Vector3.up);
		return target;
	}
}