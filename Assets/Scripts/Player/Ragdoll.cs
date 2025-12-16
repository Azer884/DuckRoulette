using UnityEngine;
using Unity.Netcode;
using System.Collections;

public class Ragdoll : NetworkBehaviour
{
    private class BoneTransform
    {
        public Vector3 Position;
        public Quaternion Rotation;
    }

    private enum PlayerState
    {
        Idle,
        Ragdoll,
        ResettingBones,
        StandingUp,
        Dead
    }

    [SerializeField] private Transform parent;

    [SerializeField] private string[] _faceUpStandUpStateNames;
    [SerializeField] private string[] _faceDownStandUpStateNames;

    [SerializeField] private string[] _faceUpStandUpClipNames;
    [SerializeField] private string[] _faceDownStandUpClipNames;

    [SerializeField] private float _timeToResetBones = 0.5f;

    [SerializeField] private Animator _animator;
    [SerializeField] private Animator[] otherAnimators;

    [SerializeField] private GameObject cam, foots, shadow, hands, dizzy;

    private Rigidbody[] _ragdollRigidbodies;
    private CharacterController _characterController;
    private Movement movement;
    private Shooting shooting;
    private Slap slap;
    private TeamUp teamUp;
    private Username userName;

    private Transform _hipsBone;
    private Transform[] _bones;

    private BoneTransform[] _ragdollBoneTransforms;
    private BoneTransform[] _faceUpStandUpBoneTransforms;
    private BoneTransform[] _faceDownStandUpBoneTransforms;

    private PlayerState _currentState = PlayerState.Idle;
    private float _timeToWakeUp;
    private float _elapsedResetBonesTime;
    private bool _isFacingUp;
    private bool shootingStates;
    private string _currentStandUpAnim;

    public override void OnNetworkSpawn()
    {
        if (!IsOwner) enabled = false;
    }

    void Awake()
    {
        _ragdollRigidbodies = parent.GetComponentsInChildren<Rigidbody>();
        _characterController = GetComponent<CharacterController>();

        movement = GetComponent<Movement>();
        shooting = GetComponent<Shooting>();
        slap = GetComponent<Slap>();
        teamUp = GetComponent<TeamUp>();
        userName = GetComponent<Username>();

        _hipsBone = _animator.GetBoneTransform(HumanBodyBones.Hips);
        _bones = _hipsBone.GetComponentsInChildren<Transform>();

        _ragdollBoneTransforms = CreateBoneArray();
        _faceUpStandUpBoneTransforms = CreateBoneArray();
        _faceDownStandUpBoneTransforms = CreateBoneArray();

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
            case PlayerState.ResettingBones:
                ResettingBonesBehaviour();
                break;
            case PlayerState.StandingUp:
                StandingUpBehaviour();
                break;
        }
    }

    /* ===================== PUBLIC ===================== */

    public void TriggerRagdoll(bool isDead = false)
    {
        EnableRagdoll();

        if (isDead)
        {
            _currentState = PlayerState.Dead;
            return;
        }

        _timeToWakeUp = Random.Range(3f, 6f);
        _currentState = PlayerState.Ragdoll;

        EnableDizzinessServerRpc(OwnerClientId, _timeToWakeUp + 2f);
    }

    /* ===================== STATES ===================== */

    private void RagdollBehaviour()
    {
        _timeToWakeUp -= Time.deltaTime;

        if (_timeToWakeUp <= 0f)
        {
            _isFacingUp = _hipsBone.forward.y > 0f;
            AlignPositionToHips();
            PopulateBoneTransforms(_ragdollBoneTransforms);

            _elapsedResetBonesTime = 0f;
            _currentState = PlayerState.ResettingBones;
        }
    }

    private void ResettingBonesBehaviour()
    {
        _elapsedResetBonesTime += Time.deltaTime;
        float t = Mathf.Clamp01(_elapsedResetBonesTime / _timeToResetBones);

        var target = _isFacingUp ? _faceUpStandUpBoneTransforms : _faceDownStandUpBoneTransforms;

        for (int i = 0; i < _bones.Length; i++)
        {
            _bones[i].localPosition = Vector3.Lerp(
                _ragdollBoneTransforms[i].Position,
                target[i].Position,
                t
            );

            _bones[i].localRotation = Quaternion.Slerp(
                _ragdollBoneTransforms[i].Rotation,
                target[i].Rotation,
                t
            );
        }

        if (t >= 1f)
        {
            _currentStandUpAnim = GetStandUpStateName();
            DisableRagdoll();

            _animator.Play(_currentStandUpAnim, 0, 0);
            foreach (var anim in otherAnimators)
                anim.Play(_currentStandUpAnim, 0, 0);

            _currentState = PlayerState.StandingUp;
        }
    }

    private void StandingUpBehaviour()
    {
        if (!_animator.GetCurrentAnimatorStateInfo(0).IsName(_currentStandUpAnim))
            _currentState = PlayerState.Idle;
    }

    /* ===================== RAGDOLL ===================== */

    private void EnableRagdoll()
    {
        foreach (var rb in _ragdollRigidbodies)
            rb.isKinematic = false;

        _animator.enabled = false;
        foreach (var anim in otherAnimators)
            anim.enabled = false;

        GetComponent<SFXHandler>().PainSound();
        shootingStates = shooting.enabled;

        SetScriptsEnabled(false);
        SetVisualsEnabled(false);

        _characterController.enabled = false;
    }

    private void DisableRagdoll()
    {
        foreach (var rb in _ragdollRigidbodies)
            rb.isKinematic = true;

        _animator.enabled = true;
        foreach (var anim in otherAnimators)
            anim.enabled = true;

        SetScriptsEnabled(true);
        SetVisualsEnabled(true);

        _characterController.enabled = true;
    }

    /* ===================== ORIGINAL METHODS KEPT ===================== */

    public void SetScriptsEnabled(bool state)
    {
        movement.enabled = state;
        teamUp.enabled = state;

        if (state)
        {
            if (shootingStates)
                shooting.enabled = true;
            else
            {
                shooting.enabled = false;
                slap.enabled = true;
            }
        }
        else
        {
            slap.enabled = false;
            shooting.enabled = false;
        }

        EnableServerRpc(OwnerClientId, state);
    }

    public void SetVisualsEnabled(bool state)
    {
        cam.SetActive(state);
        hands.SetActive(state);
        foots.SetActive(state);
        shadow.SetActive(state);
        userName.userName.gameObject.SetActive(state);
    }

    /* ===================== HELPERS ===================== */

    private BoneTransform[] CreateBoneArray()
    {
        var arr = new BoneTransform[_bones.Length];
        for (int i = 0; i < arr.Length; i++)
            arr[i] = new BoneTransform();
        return arr;
    }

    private void PopulateBoneTransforms(BoneTransform[] target)
    {
        for (int i = 0; i < _bones.Length; i++)
        {
            target[i].Position = _bones[i].localPosition;
            target[i].Rotation = _bones[i].localRotation;
        }
    }

    private void PopulateAnimationStartBoneTransforms(string clipName, BoneTransform[] target)
    {
        transform.GetPositionAndRotation(out var p, out var r);

        foreach (var clip in _animator.runtimeAnimatorController.animationClips)
        {
            if (clip.name == clipName)
            {
                clip.SampleAnimation(gameObject, 0f);
                PopulateBoneTransforms(target);
                break;
            }
        }

        transform.SetPositionAndRotation(p, r);
    }

    private void AlignPositionToHips()
    {
        _hipsBone.GetPositionAndRotation(out var p, out var r);
        transform.SetPositionAndRotation(
            new Vector3(p.x, transform.position.y, p.z),
            Quaternion.Euler(0, r.eulerAngles.y, 0)
        );
    }

    private string GetStandUpStateName()
    {
        return _isFacingUp
            ? _faceUpStandUpStateNames[Random.Range(0, _faceUpStandUpStateNames.Length)]
            : _faceDownStandUpStateNames[Random.Range(0, _faceDownStandUpStateNames.Length)];
    }

    /* ===================== RPCs ===================== */

    [ServerRpc(RequireOwnership = false)]
    private void EnableServerRpc(ulong clientId, bool state)
    {
        EnableClientRpc(clientId, state);
    }

    [ClientRpc]
    private void EnableClientRpc(ulong clientId, bool state)
    {
        if (OwnerClientId == clientId)
            _characterController.enabled = state;
    }

    [ServerRpc(RequireOwnership = false)]
    private void EnableDizzinessServerRpc(ulong clientId, float waitTime)
    {
        EnableDizzinessClientRpc(clientId, waitTime);
    }

    [ClientRpc]
    private void EnableDizzinessClientRpc(ulong clientId, float waitTime)
    {
        if (OwnerClientId != clientId) return;

        dizzy.SetActive(true);
        StartCoroutine(DisableDizzyAfter(waitTime));
    }

    private IEnumerator DisableDizzyAfter(float t)
    {
        yield return new WaitForSeconds(t);
        dizzy.SetActive(false);
    }
}
