using UnityEngine;
using Unity.Netcode;
using System.Collections;
public class Ragdoll : NetworkBehaviour
{
    private class BoneTransform
    {
        public Vector3 Position { get; set; }
        public Quaternion Rotation { get; set; }
    }

    private enum PlayerState
    {
        Idle,
        Ragdoll,
        StandingUp,
        ResettingBones
    }

    [SerializeField]
    private Transform parent;

    // Arrays to hold multiple possible animation states for randomization
    [SerializeField]
    private string[] _faceUpStandUpStateNames;
    [SerializeField]
    private string[] _faceDownStandUpStateNames;

    [SerializeField]
    private string[] _faceUpStandUpClipNames;
    [SerializeField]
    private string[] _faceDownStandUpClipNames;

    [SerializeField]
    private float _timeToResetBones;

    private Rigidbody[] _ragdollRigidbodies;
    private PlayerState _currentState = PlayerState.Idle;
    [SerializeField]
    private Animator _animator;
    [SerializeField]
    private Animator[] otherAnimators;
    [SerializeField]
    private GameObject cam;
    [SerializeField]
    private GameObject foots;
    [SerializeField]
    private GameObject shadow;
    [SerializeField]
    private GameObject hands;
    private CharacterController _characterController;
    private Movement movement;
    private Slap slap;
    private Shooting shooting;
    private Username userName;
    public float _timeToWakeUp;
    private Transform _hipsBone;

    private BoneTransform[] _faceUpStandUpBoneTransforms;
    private BoneTransform[] _faceDownStandUpBoneTransforms;
    private BoneTransform[] _ragdollBoneTransforms;
    private Transform[] _bones;
    private float _elapsedResetBonesTime;
    private bool _isFacingUp;
    private bool shootingStates;

    [SerializeField] private GameObject dizzy;

    public override void OnNetworkSpawn()
    {
        if(!IsOwner) enabled = false;
    }

    void Awake()
    {
        _ragdollRigidbodies = parent.GetComponentsInChildren<Rigidbody>();
        _characterController = GetComponent<CharacterController>();
        movement = GetComponent<Movement>();
        slap = GetComponent<Slap>();
        shooting = GetComponent<Shooting>();
        userName = GetComponent<Username>();
        _hipsBone = _animator.GetBoneTransform(HumanBodyBones.Hips);

        _bones = _hipsBone.GetComponentsInChildren<Transform>();
        _faceUpStandUpBoneTransforms = new BoneTransform[_bones.Length];
        _faceDownStandUpBoneTransforms = new BoneTransform[_bones.Length];
        _ragdollBoneTransforms = new BoneTransform[_bones.Length];

        for (int boneIndex = 0; boneIndex < _bones.Length; boneIndex++)
        {
            _faceUpStandUpBoneTransforms[boneIndex] = new BoneTransform();
            _faceDownStandUpBoneTransforms[boneIndex] = new BoneTransform();
            _ragdollBoneTransforms[boneIndex] = new BoneTransform();
        }

        PopulateAnimationStartBoneTransforms(_faceUpStandUpClipNames[0], _faceUpStandUpBoneTransforms);
        PopulateAnimationStartBoneTransforms(_faceDownStandUpClipNames[0], _faceDownStandUpBoneTransforms);

        DisableRagdoll();
    }

    void Update()
    {
        switch (_currentState)
        {
            case PlayerState.Ragdoll:
                RagdollBehaviour();
                break;
            case PlayerState.StandingUp:
                StandingUpBehaviour();
                break;
            case PlayerState.ResettingBones:
                ResettingBonesBehaviour();
                break;
        }
    }

    public void TriggerRagdoll(bool isDead)
    {
        EnableRagdoll();
        if (!isDead)
        {
            _timeToWakeUp = Random.Range(3, 6);
            _currentState = PlayerState.Ragdoll;
            EnableDizzinessServerRpc(OwnerClientId, _timeToWakeUp + 2);
        }
        else
        {
            movement.enabled = false;
            slap.enabled = false;
            shooting.enabled = false;
            cam.SetActive(false);
            hands.SetActive(false);
            foots.SetActive(false);
            shadow.SetActive(false);

            DisableServerRpc(OwnerClientId);

        }
    }

    private void DisableRagdoll()
    {
        foreach (Rigidbody rigidbody in _ragdollRigidbodies)
        {
            rigidbody.isKinematic = true;
        }

        _animator.enabled = true;
        foreach (Animator animator in otherAnimators)
        {
            animator.enabled = true;
        }
        movement.enabled = true;
        cam.SetActive(true);
        foots.SetActive(true);
        hands.SetActive(true);
        shadow.SetActive(true);
        _characterController.enabled = true;
        if (shootingStates && !shooting.enabled)
        {
            shooting.enabled = true;
        }
        else if (!shootingStates)
        {
            shooting.enabled = false;
            slap.enabled = true;
        }

    }

    public void EnableRagdoll()
    {

        foreach (Rigidbody rigidbody in _ragdollRigidbodies)
        {
            rigidbody.isKinematic = false;
        }

        _animator.enabled = false;
        foreach (Animator animator in otherAnimators)
        {
            animator.enabled = false;
        }
        shootingStates = shooting.enabled;
        movement.enabled = false;
        slap.enabled = false;
        shooting.enabled = false;
        cam.SetActive(false);
        hands.SetActive(false);
        foots.SetActive(false);
        shadow.SetActive(false);
        _characterController.enabled = false;

        RebindSaveLoad.Instance.RumbleGamepad(1f, 1f, .3f, .2f);
    }

    private void RagdollBehaviour()
    {
        _timeToWakeUp -= Time.deltaTime;

        if (_timeToWakeUp <= 0)
        {
            _isFacingUp = _hipsBone.forward.y > 0;
            AlignPositionToHips();
            PopulateBoneTransforms(_ragdollBoneTransforms);
            _currentState = PlayerState.ResettingBones;
            _elapsedResetBonesTime = 0;
        }
    }

    private void StandingUpBehaviour()
    {
        if (_animator.GetCurrentAnimatorStateInfo(0).IsName(GetStandUpStateName()) == false)
        {
            _currentState = PlayerState.Idle;
        }
    }

    private void ResettingBonesBehaviour()
    {
        _elapsedResetBonesTime += Time.deltaTime;
        float elapsedPercentage = _elapsedResetBonesTime / _timeToResetBones;

        if (elapsedPercentage >= 1)
        {
            _currentState = PlayerState.StandingUp;
            DisableRagdoll();
            string animName = GetStandUpStateName();
            _animator.Play(animName, 0, 0);
            _animator.Play(animName, 1, 0);
            foreach (Animator anim in otherAnimators)
            {
                anim.Play(animName, 0, 0);
                anim.Play(animName, 1, 0);
            }
        }
    }

    private void AlignPositionToHips()
    {
        _hipsBone.GetPositionAndRotation(out Vector3 hipsPosition, out Quaternion hipsRotation);
        transform.SetPositionAndRotation(new Vector3(hipsPosition.x, transform.position.y, hipsPosition.z), Quaternion.Euler(0, hipsRotation.eulerAngles.y, 0));
        _hipsBone.position = hipsPosition;
        _hipsBone.rotation = hipsRotation;
    }

    private void PopulateBoneTransforms(BoneTransform[] boneTransforms)
    {
        for (int boneIndex = 0; boneIndex < _bones.Length; boneIndex++)
        {
            boneTransforms[boneIndex].Position = _bones[boneIndex].localPosition;
            boneTransforms[boneIndex].Rotation = _bones[boneIndex].localRotation;
        }
    }

    private void PopulateAnimationStartBoneTransforms(string clipName, BoneTransform[] boneTransforms)
    {
        transform.GetPositionAndRotation(out Vector3 positionBeforeSampling, out Quaternion rotationBeforeSampling);
        foreach (AnimationClip clip in _animator.runtimeAnimatorController.animationClips)
        {
            if (clip.name == clipName)
            {
                clip.SampleAnimation(gameObject, 0);
                PopulateBoneTransforms(boneTransforms);
                break;
            }
        }

        transform.SetPositionAndRotation(positionBeforeSampling, rotationBeforeSampling);
    }

    // Randomly select a stand-up animation based on the character's orientation
    private string GetStandUpStateName()
    {
        if (_isFacingUp)
        {
            int randomIndex = Random.Range(0, _faceUpStandUpStateNames.Length);
            return _faceUpStandUpStateNames[randomIndex];
        }
        else
        {
            int randomIndex = Random.Range(0, _faceDownStandUpStateNames.Length);
            return _faceDownStandUpStateNames[randomIndex];
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void DisableServerRpc(ulong clientId)
    {
        DisableClientRpc(clientId);
    }

    [ClientRpc]
    private void DisableClientRpc(ulong clientId)
    {
        if (OwnerClientId == clientId)
        {
            _characterController.enabled = false;
            userName.userName.gameObject.SetActive(false);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void EnableDizzinessServerRpc(ulong clientId , float waitTime)
    {
        EnableDizzinessClientRpc(clientId, waitTime);
    }

    [ClientRpc]
    private void EnableDizzinessClientRpc(ulong clientId, float waitTime)
    {
        if (OwnerClientId == clientId)
        {
            dizzy.SetActive(true);

            StartCoroutine(WaitBeforeDisactivate(waitTime));
        }
    }

    private IEnumerator WaitBeforeDisactivate(float waitTime)
    {
        yield return new WaitForSeconds(waitTime);

        dizzy.SetActive(false);
    }
}
