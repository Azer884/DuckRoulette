using UnityEngine;
using Unity.Netcode.Components;

public class OwnerNetworkAnimator : NetworkAnimator
    {
        protected override bool OnIsServerAuthoritative()
        {
            return false;
        }

        // NetworkAnimator.Awake sizes its per-layer arrays (m_LayerWeights, m_TransitionHash and the
        // preallocated AnimationMessage.AnimationStates list) off m_Animator.layerCount ONCE, and never
        // resizes them. An Animator that sits on an inactive GameObject is not initialized, so it reports
        // layerCount 0 - the arrays come out empty, and the moment the object is switched on
        // CheckForStateChange() indexes AnimationStates[0] and throws
        // ArgumentOutOfRangeException every frame from then on.
        //
        // Both of these components live on the player root while the Animators they drive live on
        // children, and the third-person Gun child ships inactive in Player.prefab (it is also switched
        // off during Awake by Ragdoll -> DisableRagdoll -> SetVisualsEnabled, since HasGun is false at
        // that point, and Awake order between the two components is not guaranteed). So the gun's
        // NetworkAnimator reliably initialized against a dead Animator.
        //
        // Switch the Animator on just for the duration of the base initialization and put it straight
        // back: this happens inside Awake, long before anything renders, so nothing becomes visible, and
        // the Gun object is plain FBX mesh + Animator with no scripts of its own to disturb.
        protected override void Awake()
        {
            Animator animator = this.Animator;

            if (animator == null)
            {
                base.Awake();
                return;
            }

            GameObject animatorObject = animator.gameObject;
            bool wasActive = animatorObject.activeSelf;
            bool wasEnabled = animator.enabled;

            if (!wasActive)
            {
                animatorObject.SetActive(true);
            }

            if (!wasEnabled)
            {
                animator.enabled = true;
            }

            base.Awake();

            if (!wasEnabled)
            {
                animator.enabled = false;
            }

            if (!wasActive)
            {
                animatorObject.SetActive(false);
            }
        }
    }
