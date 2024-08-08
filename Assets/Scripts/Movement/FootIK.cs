using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FootIK : MonoBehaviour
{
    private Animator anim;
    public LayerMask layerMask; // Select all layers that foot placement applies to.
    [Range(0, 1f)]
    public float DistanceToGround;
    public Transform pelvis; // Reference to the pelvis bone
    public float pelvisOffset = 0.1f; // Amount to adjust pelvis

    private float lastPelvisPositionY;
    public float heightThreshold = 0.01f;

    private void Awake()
    {
        anim = GetComponent<Animator>();
        lastPelvisPositionY = pelvis.localPosition.y;
    }

    private void OnAnimatorIK(int layerIndex = 2)
    {
        AdjustPelvisHeight();

        SetFootPos(anim, AvatarIKGoal.LeftFoot);
        SetFootPos(anim, AvatarIKGoal.RightFoot);
    }

    private void SetFootPos(Animator anim, AvatarIKGoal avatarGoal)
    {
        anim.SetIKPositionWeight(avatarGoal, 1f);
        anim.SetIKRotationWeight(avatarGoal, 1f);

        Ray ray = new(anim.GetIKPosition(avatarGoal) + Vector3.up, Vector3.down);
        if (Physics.Raycast(ray, out RaycastHit hit, DistanceToGround + 1f, layerMask))
        {
            if (hit.transform.CompareTag("Walkable"))
            {
                Vector3 footPosition = hit.point;
                footPosition.y += DistanceToGround;
                anim.SetIKPosition(avatarGoal, footPosition);
                anim.SetIKRotation(avatarGoal, Quaternion.LookRotation(transform.forward, hit.normal));
            }
        }
    }

    private void AdjustPelvisHeight()
    {
        // Get the position of both feet
        Vector3 leftFootPos = anim.GetIKPosition(AvatarIKGoal.LeftFoot);
        Vector3 rightFootPos = anim.GetIKPosition(AvatarIKGoal.RightFoot);

        // Calculate the height difference
        float heightDifference = Mathf.Abs(leftFootPos.y - rightFootPos.y);

        // Adjust pelvis height if there is a significant height difference
        if (heightDifference > heightThreshold) // Adjust this threshold as needed
        {
            float newPelvisY = Mathf.Lerp(lastPelvisPositionY, pelvis.localPosition.y - heightDifference * pelvisOffset, Time.deltaTime * 10f);
            pelvis.localPosition = new Vector3(pelvis.localPosition.x, newPelvisY, pelvis.localPosition.z);
        }
        else
        {
            // Reset pelvis position if no significant difference
            pelvis.localPosition = new Vector3(pelvis.localPosition.x, lastPelvisPositionY, pelvis.localPosition.z);
        }

        lastPelvisPositionY = pelvis.localPosition.y;
    }
}
