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

    // How long the full-screen daze holds on AFTER the player is back on their feet, before it
    // starts fading. Rolled locally rather than server-picked, unlike _timeToWakeUp: this is a
    // first-person-only effect that no other peer can see, so there is no shared timeline for it
    // to disagree with.
    [SerializeField] private float minScreenDizzyLingerSeconds = 2f;
    [SerializeField] private float maxScreenDizzyLingerSeconds = 3f;

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
    private string _currentStandUpAnim;
    private Coroutine _dizzyRoutine;
    private Coroutine _screenDizzyLingerRoutine;

    // True on the copy of this player that this client actually controls: the owner in a networked
    // session, and the never-spawned offline tutorial player. Everything gated on it is either
    // owner-authoritative (the transform, NetworkVariables, the animator parameters
    // OwnerNetworkAnimator replicates) or first-person-only visuals that must never be switched on
    // for somebody else's player.
    private bool IsLocalPlayer => !IsSpawned || IsOwner;

    // Deliberately no OnNetworkSpawn override disabling this component on non-owners any more.
    // TriggerRagdoll is broadcast to EVERY peer (GameManager.StunPlayerClientRpc and
    // SetPlayerDeadClientRpc both call it on every client's copy of the victim), and Update() is
    // the only thing that ever advances Ragdoll -> ResettingBones -> StandingUp -> Idle. With the
    // component disabled off-owner, a remote copy ran EnableRagdoll (Animators off, first-person
    // visuals off, CharacterController off) and then had nothing left running to undo any of it:
    // every other player saw that player as a limp, frozen, non-animating ragdoll for the rest of
    // the match, gun included, no matter what they did on their own screen.

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

    /// <param name="wakeUpTime">How long to stay down, in seconds. The server picks this once and
    /// passes the same value to every peer (see GameManager.StunPlayerClientRpc) - now that this
    /// whole recovery runs on every copy of the player, each peer rolling its own Random.Range
    /// would have every screen standing the same player up at a different moment. Non-positive
    /// falls back to a local roll, for the offline tutorial and for a plain stun with no server
    /// value behind it.</param>
    public void TriggerRagdoll(bool isDead = false, float wakeUpTime = -1f)
    {
        EnableRagdoll();

        if (isDead)
        {
            _currentState = PlayerState.Dead;
            // Dying out of an existing knockout hands the screen over to DeathVignette, which owns
            // the "you're dead" treatment - drop the daze rather than layering the two.
            SetScreenDizziness(false);
            return;
        }

        _timeToWakeUp = wakeUpTime > 0f ? wakeUpTime : Random.Range(3f, 6f);
        _currentState = PlayerState.Ragdoll;

        // Shown directly instead of through a ServerRpc -> ClientRpc round trip: this method is
        // already running on every peer, so the round trip only added latency plus one duplicate
        // broadcast per connected client for a purely local visual.
        ShowDizziness(_timeToWakeUp + 2f);

        // The world-space stars above the head (ShowDizziness) tell everyone ELSE that this player
        // is out; this tells the player themselves, on their own screen only.
        SetScreenDizziness(true);
    }

    /* ===================== STATES ===================== */

    private void RagdollBehaviour()
    {
        _timeToWakeUp -= Time.deltaTime;

        if (_timeToWakeUp <= 0f)
        {
            // The daze does not end with the knockout: it holds through the stand-up and for a
            // couple of seconds of regained control before it starts fading, so getting slapped
            // down still costs the victim something after they are back on their feet.
            LingerScreenDizziness();

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

        // Safely get and call SFXHandler
        if (TryGetComponent<SFXHandler>(out var sfxHandler))
        {
            sfxHandler.PainSound();
        }

        // Visuals hidden BEFORE scripts disable, mirroring DisableRagdoll's own ordering note in
        // reverse: SetScriptsEnabled(false) disables Shooting, whose OnDisable reparents/toggles
        // the held-gun hierarchy (HandsState/fPHands.SwitchParent) - doing that while `hands` is
        // still visible made the gun visibly pop/snap out of the held pose for a frame right as
        // the ragdoll took over. Hiding first means that pop happens on already-invisible objects.
        SetVisualsEnabled(false);
        SetScriptsEnabled(false);

        _characterController.enabled = false;
    }

    private void DisableRagdoll()
    {
        foreach (var rb in _ragdollRigidbodies)
            rb.isKinematic = true;

        _animator.enabled = true;
        foreach (var anim in otherAnimators)
            anim.enabled = true;

        // Visuals must come back BEFORE scripts: re-enabling Shooting here fires its OnEnable,
        // which reapplies the gun hand pose (HandsState/fPHands.SwitchParent) - doing that while
        // `hands` is still SetActive(false) from EnableRagdoll left the pose applied to a hidden,
        // not-yet-reset hierarchy, which is why the gun looked held wrong after standing back up.
        SetVisualsEnabled(true);
        SetScriptsEnabled(true);

        _characterController.enabled = true;

        ReapplyGunAnimatorState();
    }

    // Every Animator on this rig has "Keep Animator State On Disable" off, so the enable/disable
    // pair above resets its parameters to the controller defaults. HaveAGun is replicated by
    // OwnerNetworkAnimator, which only pushes a parameter when it CHANGES on the owner - a reset
    // that lands on every peer at the same time therefore never gets corrected by replication, and
    // a player who was knocked down holding the gun kept playing the unarmed animation with a
    // visible gun in hand (GunStateChanger keeps that gun on screen off haveGun.Value alone).
    // Re-assert the pose from that same authoritative value once the animators are live again.
    private void ReapplyGunAnimatorState()
    {
        bool hasGun = shooting != null && shooting.HasGun;

        SetHaveAGun(_animator, hasGun);
        foreach (var anim in otherAnimators)
        {
            SetHaveAGun(anim, hasGun);
        }
    }

    private static void SetHaveAGun(Animator animator, bool hasGun)
    {
        // Not every animator on the rig declares HaveAGun (Ragdoll drives a different, wider set
        // than Shooting does) and writing a parameter a controller doesn't have logs a warning
        // every time.
        if (animator == null || !animator.isActiveAndEnabled)
        {
            return;
        }

        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.type == AnimatorControllerParameterType.Bool && parameter.name == "HaveAGun")
            {
                animator.SetBool("HaveAGun", hasGun);
                return;
            }
        }
    }

    /* ===================== ORIGINAL METHODS KEPT ===================== */

    public void SetScriptsEnabled(bool state)
    {
        // Owner-only. Movement/TeamUp/Shooting/Slap are all disabled on every remote copy by their
        // own OnNetworkSpawn, and this method runs on EVERY peer (TriggerRagdoll is a broadcast) -
        // so a stand-up handed a remote copy of somebody else's player back its Movement (which
        // then reads THIS machine's input in Update and drives a player it has no authority over)
        // and its Slap, and re-ran their OnEnable side effects on a copy that must never run them.
        // HidingSpot.SetLocalScriptsEnabled already documents the same invariant for the same four
        // components; this was the one place breaking it.
        if (IsLocalPlayer)
        {
            movement.enabled = state;
            teamUp.enabled = state;

            if (state)
            {
                // Getting knocked down always lowers the gun, same as HidingSpot's exit - the
                // player has to switch back to it explicitly instead of it reappearing on its own.
                shooting.enabled = false;
                slap.enabled = true;
            }
            else
            {
                slap.enabled = false;
                shooting.enabled = false;
            }
        }

        // Owner-gated too: every peer used to fire this same ServerRpc for the same knockout, so
        // one stun produced N identical broadcasts.
        if (IsOwner && CanSendNetworkRpc())
        {
            EnableServerRpc(OwnerClientId, state);
        }
    }

    public void SetVisualsEnabled(bool state)
    {
        // cam is this player's first-person camera, which Movement.ApplyRemoteVisualState turns off
        // on every remote copy and which has to stay off there. Now that recovery runs on every
        // peer, an ungated SetActive(true) here would switch the local view to whichever other
        // player just stood up from a knockout.
        if (IsLocalPlayer)
        {
            cam.SetActive(state);
        }

        hands.SetActive(state);
        foots.SetActive(state);
        shadow.SetActive(state);
        userName.userName.gameObject.SetActive(state);

        // The gun's own visibility is otherwise driven every frame by GunStateChanger purely off
        // haveGun.Value, which ragdoll never touches - so it used to keep floating in its last
        // hand pose while the player ragdolled instead of hiding like everything else, then
        // reappear correctly here only if this player still actually holds the gun turn.
        if (shooting != null && shooting.gun != null)
        {
            shooting.gun.SetActive(state && shooting.HasGun);
        }
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
        // The root transform is owner-authoritative (ClientNetworkTransform), so only the owner
        // may move it. A remote copy writing its own locally-simulated hip position here - ragdoll
        // physics diverges per peer - just fought the next incoming transform update and showed as
        // a snap. The owner's own align replicates to everyone anyway.
        if (!IsLocalPlayer)
        {
            return;
        }

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

    // Replaces the old EnableDizziness ServerRpc/ClientRpc pair. TriggerRagdoll already runs on
    // every peer, so that round trip bought nothing but latency and one duplicate broadcast per
    // connected client for a purely cosmetic effect.
    private void ShowDizziness(float waitTime)
    {
        if (dizzy == null)
        {
            return;
        }

        dizzy.SetActive(true);
        if (_dizzyRoutine != null)
        {
            StopCoroutine(_dizzyRoutine);
        }
        _dizzyRoutine = StartCoroutine(DisableDizzyAfter(waitTime));
    }

    private IEnumerator DisableDizzyAfter(float t)
    {
        yield return new WaitForSeconds(t);
        dizzy.SetActive(false);
        _dizzyRoutine = null;
    }

    // The full-screen post-processing daze (DizzinessEffect), as opposed to the world-space stars
    // ShowDizziness puts above the victim's head, which every peer is supposed to see.
    //
    // IsLocalPlayer is load-bearing here, not defensive: TriggerRagdoll is a broadcast
    // (GameManager.StunPlayerClientRpc reaches every client) and this whole state machine runs on
    // every peer's copy of every player, so without the gate one player getting slapped down would
    // swim the screen of everybody in the match at once.
    private void SetScreenDizziness(bool isDizzy)
    {
        if (!IsLocalPlayer)
        {
            return;
        }

        // Any explicit call wins over a linger still counting down - a fresh knockout mid-linger
        // must not be cleared by the previous one's timer, and dying mid-linger hands the screen
        // straight to DeathVignette.
        if (_screenDizzyLingerRoutine != null)
        {
            StopCoroutine(_screenDizzyLingerRoutine);
            _screenDizzyLingerRoutine = null;
        }

        if (!isDizzy)
        {
            // Only clear an effect that actually exists - a wake-up or a death that never went
            // through a knockout has nothing to ramp out, and GetOrAdd would install the component
            // (and its runtime Volume) purely to tell it to do nothing.
            if (TryGetComponent(out DizzinessEffect existing))
            {
                existing.SetDizzy(false);
            }
            return;
        }

        DizzinessEffect.GetOrAdd(gameObject).SetDizzy(true);
    }

    // Keeps the daze at full strength for a beat after the player wakes up, then ramps it out
    // through DizzinessEffect's normal fade.
    private void LingerScreenDizziness()
    {
        if (!IsLocalPlayer)
        {
            return;
        }

        if (_screenDizzyLingerRoutine != null)
        {
            StopCoroutine(_screenDizzyLingerRoutine);
        }
        _screenDizzyLingerRoutine = StartCoroutine(ClearScreenDizzinessAfterLinger());
    }

    private IEnumerator ClearScreenDizzinessAfterLinger()
    {
        yield return new WaitForSeconds(Random.Range(minScreenDizzyLingerSeconds, maxScreenDizzyLingerSeconds));

        // Cleared before the call so SetScreenDizziness doesn't try to stop the coroutine it is
        // currently running inside.
        _screenDizzyLingerRoutine = null;
        SetScreenDizziness(false);
    }

    private bool CanSendNetworkRpc()
    {
        return IsSpawned && NetworkManager != null && NetworkManager.IsListening;
    }
}
