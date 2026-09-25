using System.Collections.Generic;
using UnityEngine;

namespace Weather
{
    /// <summary>
    /// Hooks a cosmetic cape's <see cref="Cloth"/> up to the duck wearing it. The cape is a prefab
    /// spawned into the shirt holder at runtime, so its Cloth can't reference the wearer's body
    /// colliders in the asset - every collider slot was empty and the cape swung straight through
    /// the duck. On enable this finds the wearer's body capsules (the normal, non-ragdoll ones)
    /// and head, and hands them to the cloth. Wind comes from the <see cref="ClothWindReceiver"/>
    /// on the same prefab.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Cloth))]
    public class CapeCloth : MonoBehaviour
    {
        [SerializeField, Tooltip("Body collider GameObject names the cape should drape over and " +
            "never pass through.")]
        private string[] bodyColliderNames =
        {
            "LowerSpin", "Spin", "Upper_Arm_L", "Upper_Arm_R", "UpperLeg_L", "UpperLeg_R",
        };

        [SerializeField] private string headColliderName = "Head";

        [SerializeField, Tooltip("Layers the wearer's regular (non-ragdoll) body colliders are on.")]
        private string[] bodyLayers = { "Player", "Shadow" };

        private Cloth cloth;

        private void Awake()
        {
            cloth = GetComponent<Cloth>();
        }

        private void OnEnable()
        {
            Bind();
        }

        private void Bind()
        {
            if (cloth == null)
            {
                return;
            }

            Transform wearer = FindWearer();
            if (wearer == null)
            {
                return;
            }

            int layerMask = LayerMask.GetMask(bodyLayers);
            var capsules = new List<CapsuleCollider>();
            SphereCollider head = null;

            foreach (Collider collider in wearer.GetComponentsInChildren<Collider>(true))
            {
                if (collider.isTrigger || (layerMask & (1 << collider.gameObject.layer)) == 0)
                {
                    continue;
                }

                if (collider is CapsuleCollider capsule && System.Array.IndexOf(bodyColliderNames, collider.name) >= 0)
                {
                    capsules.Add(capsule);
                }
                else if (head == null && collider is SphereCollider sphere && collider.name == headColliderName)
                {
                    head = sphere;
                }
            }

            cloth.capsuleColliders = capsules.ToArray();
            cloth.sphereColliders = head != null
                ? new[] { new ClothSphereColliderPair(head) }
                : new ClothSphereColliderPair[0];

            // Start from the rest pose on the new body instead of whatever it was last simulating.
            cloth.ClearTransformMotion();
        }

        // The duck root: the nearest parent with a CharacterController (in match) or, in the menu
        // preview, the topmost parent.
        private Transform FindWearer()
        {
            CharacterController controller = GetComponentInParent<CharacterController>(true);
            if (controller != null)
            {
                return controller.transform;
            }

            return transform.root != transform ? transform.root : null;
        }
    }
}
