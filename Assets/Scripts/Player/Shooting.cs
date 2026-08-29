using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class Shooting : NetworkBehaviour
{
    public event Action OnGunShot;
    public ShakeProfile shootShakeProfile;
    private CameraShaker cameraShaker;
    public GameObject bulletPrefab;
    private GameObject bullet;
    public Transform spawnPt;
    public Transform cam;
    private InputActionAsset inputActions;
    public Animator[] animators;
    public Animator bulletAnimator;
    public NetworkVariable<bool> hasShot = new(false,  NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public bool canTrigger, canShoot, isTriggered;
    [SerializeField] private Transform targetAim;
    public Hands fPHands;
    public GameObject gun;
    public NetworkVariable<bool> haveGun = new(false,  NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    [SerializeField] private Slap slapScript;
    public int shotCounter = 0, emptyShots;
    public GameObject vfxPrefab;
    private bool _shotExecuted;
    private InputAction reloadAction, triggerAction, shootAction;
    private Movement movement;
    // Frame Trigger() last consumed a triggerAction press to cock the gun - lets Shoot() tell a
    // fresh Trigger press (the gamepad RT click that should fire, since Input System only
    // re-triggers a Button action after release) apart from the very press that just cocked it.
    private int lastTriggerConsumedFrame = -1;

    // --- Offline (tutorial) support -------------------------------------------
    // When there is no active Netcode session the component runs fully locally: the
    // russian-roulette chamber state lives here instead of in GameManager's
    // NetworkVariables, bullets/VFX/sounds are spawned locally and no RPCs are sent.
    // All gameplay tuning below (animations, input, timings) stays shared with the
    // networked game - only the transport layer differs.
    public bool IsLocalMode => NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening;

    private const int LocalChamberSize = 6;
    private bool _localHaveGun;
    private bool _localIsReloaded;
    // Offline (tutorial) chamber state is fully deterministic: the live round always sits in
    // the pinned chamber and every reload restarts the cylinder at chamber 0. Each reload
    // therefore plays out identically - first pull = dry click, second pull = bang.
    private int _localBulletPosition;
    [SerializeField] private int _localPinnedBulletPosition = 1;

    public bool HasGun => IsLocalMode ? _localHaveGun : haveGun.Value;
    private bool IsReloadedNow => IsLocalMode ? _localIsReloaded : GameManager.Instance.isReloaded.Value;
    private bool ShootingAllowedNow => IsLocalMode || GameManager.Instance.canShoot.Value;

    /// <summary>Tutorial hook: pin which chamber holds the live round (0-based).</summary>
    public void ConfigureLocalChamber(int index)
    {
        _localPinnedBulletPosition = Mathf.Clamp(index, 0, LocalChamberSize - 1);
    }

    // One-shot gameplay events so external systems (e.g. TutorialManager step tracking)
    // can observe reloads/triggers without polling input themselves.
    public event Action OnReloaded;
    public event Action OnTriggered;

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            enabled = false;
        }
        else
        {
            // NetworkVariable writes must happen once ownership is established (OnNetworkSpawn),
            // not in OnEnable, which runs before spawn/ownership is assigned for non-owner instances.
            // haveGun stays false here: the prefab spawns with Shooting disabled (m_Enabled: 0) so
            // GameManager's PlayerShootingScriptClientRpc is the only thing that ever enables it for
            // the actual gun holder, which is what flips haveGun true via OnEnable below. Setting it
            // true here unconditionally previously made every player appear to have the gun for the
            // brief window before GameManager assigned one.
            hasShot.Value = false;
            _shotExecuted = false;

            // GameManager picks the first gun holder and broadcasts PlayerShootingScriptClientRpc
            // from its own OnNetworkSpawn, which runs before PlayerSpawner has actually spawned any
            // player objects - that broadcast's GetLocalPlayerObject() lookup is still null then, so
            // it silently no-ops. Nothing re-sent it afterward, so nobody visibly held the gun until
            // the first round timeout (30s later) force-reassigned it - looking like "the gun starts
            // on one player then switches to another". Self-correct here instead, since this runs
            // exactly when this player's own object actually finishes spawning.
            if (GameManager.Instance != null && GameManager.Instance.playerWithGun.Value == OwnerClientId)
            {
                enabled = true;
            }
        }

        base.OnNetworkSpawn();
    }

    private void Awake()
    {
        inputActions = GetComponent<InputSystem>().inputActions;
        reloadAction = inputActions.FindAction("Reload");
        triggerAction = inputActions.FindAction("Trigger");
        shootAction = inputActions.FindAction("Shoot");
        movement = GetComponent<Movement>();
        cameraShaker = CameraShaker.GetOrAdd(gameObject);

        // Shooting spawns disabled - only GameManager's PlayerShootingScriptClientRpc enables it,
        // for the one assigned gun holder - so OnEnable (which normally sets HaveAGun) never runs
        // for anyone else, leaving them stuck on the Animator Controller's own default value for
        // that parameter. Awake still runs while disabled, so force it false here for every
        // player up front: nobody looks armed until GameManager actually hands them the gun.
        if (animators != null)
        {
            foreach (Animator anim in animators)
            {
                if (anim != null)
                {
                    anim.SetBool("HaveAGun", false);
                }
            }
        }
    }

    private void OnEnable()
    {
        hasShot.OnValueChanged += HandleHasShotChanged;

        // Re-enabled every time GameManager hands this player the gun again (see
        // PlayerShootingScriptClientRpc) - reset here so a player who already shot once can
        // shoot again on a later round. IsSpawned guards the initial pre-spawn OnEnable call,
        // which OnNetworkSpawn's own reset already covers.
        if (IsLocalMode)
        {
            _localHaveGun = true;
            _localIsReloaded = false;
            _localBulletPosition = 0;
            _shotExecuted = false;
        }
        else if (IsSpawned && IsOwner)
        {
            hasShot.Value = false;
            _shotExecuted = false;
            haveGun.Value = true;
        }

        // A disable mid-way through the Triggering() coroutine kills it before it can clear
        // this flag (e.g. the gun being lowered right after a shot) - without a reset here
        // the next gun phase would be permanently unable to trigger.
        isTriggered = false;

        // While sliding, defer the hand-pose switch instead of cutting the slide short - it gets
        // applied by Movement.EndSliding once the player actually gets back up.
        if (movement == null || !movement.IsSliding)
        {
            HandsState(true);
        }
    }

    // Called by Movement once a slide that started before/during a gun hand-off finishes, so the
    // deferred gun pose (see OnEnable above) gets applied at the right time.
    public void ApplyGunHandsPose()
    {
        if (HasGun)
        {
            HandsState(true);
        }
    }

    private void OnDisable()
    {
        hasShot.OnValueChanged -= HandleHasShotChanged;

        HandsState(false);

        if (IsLocalMode)
        {
            _localHaveGun = false;
        }
        else if (IsOwner)
        {
            haveGun.Value = false;
        }
    }

    [Obsolete("This is a necessary Update method for handling input. Do not remove.")]
    void Update()
    {
        Reload();
        Trigger();
        Shoot();
    }

    private void Reload()
    {
        if (reloadAction.triggered && !IsReloadedNow && ShootingAllowedNow)
        {
            PlayReloadSound(gun != null ? gun.transform.position : transform.position);

            foreach (Animator animator in animators)
            {
                animator.Play("Reload");
            }
            if (bulletAnimator != null)
            {
                bulletAnimator.Play("Reload");
            }

            if (IsLocalMode)
            {
                // Deterministic tutorial reload: cylinder restarts at chamber 0 with the
                // live round pinned in place, so the outcome never depends on luck.
                _localBulletPosition = 0;
                _localIsReloaded = true;
            }
            else
            {
                ReloadServerRpc();
            }

            OnReloaded?.Invoke();
        }
        if (animators.Length > 0 && animators[0].GetCurrentAnimatorStateInfo(0).IsName("Reload"))
        {
            canTrigger = false;
        }
        else
        {
            canTrigger = true;
        }
    }
    private void Trigger()
    {
        if (triggerAction.triggered && !isTriggered && IsReloadedNow && canTrigger && ShootingAllowedNow)
        {
            isTriggered = true;
            lastTriggerConsumedFrame = Time.frameCount;
            PlayTriggerSound(gun != null ? gun.transform.position : transform.position);

            foreach (Animator animator in animators)
            {
                animator.SetBool("Triggered", isTriggered);
            }

            OnTriggered?.Invoke();
        }
        if (animators.Length > 0 && animators[0].GetCurrentAnimatorStateInfo(0).IsName("Trigger"))
        {
            canShoot = false;
        }
        else
        {
            canShoot = true;
        }
    }

    private void Shoot()
    {
        // The Trigger action (RT on gamepad) drives both phases now: Trigger() above only cocks
        // on the frame it consumes a press. Input System doesn't re-trigger a Button action until
        // its control is released and pressed again, so any triggerAction.triggered on a LATER
        // frame is inherently "released and re-clicked" - including a digital, click-only RT that
        // can't be told apart from an analog one any other way. shootAction (left mouse) stays a
        // separate, independent path for keyboard/mouse.
        bool secondTriggerPress = triggerAction.triggered && lastTriggerConsumedFrame != Time.frameCount;

        if ((shootAction.triggered || secondTriggerPress) && canShoot && isTriggered)
        {
            ExecuteShot();
        }
    }

    private void ExecuteShot()
    {
        // One-shot-per-round protection is only a networked concept: the round ends via
        // hasShot and the component is re-enabled for the next holder. Offline (tutorial)
        // shooting repeats freely - reload/trigger state already rate-limits it.
        if (!IsLocalMode && _shotExecuted)
        {
            return;
        }

        bool isValidShot;
        if (IsLocalMode)
        {
            // Offline russian roulette: the pinned chamber holds the live round.
            isValidShot = _localBulletPosition == _localPinnedBulletPosition;
        }
        else
        {
            isValidShot = GameManager.Instance.bulletPosition.Value == GameManager.Instance.randomBulletPosition.Value;
        }

        if (!IsLocalMode)
        {
            _shotExecuted = true;
        }

        Vector3 spawnPosition = spawnPt != null ? spawnPt.position : transform.position;
        if (isValidShot)
        {
            foreach (Animator animator in animators)
            {
                animator.Play("Shooting");
            }
            OnGunShot?.Invoke();
            if (cameraShaker != null)
            {
                cameraShaker.Shake(shootShakeProfile);
            }
            shotCounter++;

            if (IsLocalMode)
            {
                SpawnLocalBullet(spawnPosition);
                PlayLocalOneShot(SFXManager.Instance.shootClip, spawnPosition);
                // The round was fired - require a reload before the next shot, mirroring
                // the server-side isReloaded reset in ShootServerRpc.
                _localIsReloaded = false;
            }
            else if (!CanUseNetcode())
            {
                PlayShootSound(spawnPosition);
                ShootServerRpc(spawnPt.position, Quaternion.identity, targetAim.position);
            }
            else
            {
                ShootServerRpc(spawnPt.position, Quaternion.identity, targetAim.position);
            }
        }
        else
        {
            int emptyShotAnimators = Mathf.Min(3, animators.Length);
            for (int i = 0; i < emptyShotAnimators; i++)
            {
                animators[i].Play("Shooting");
            }
            emptyShots++;
            PlayEmptyShotSound(spawnPosition);
        }

        if (IsLocalMode)
        {
            // Mirror GameManager.OnClientShotChanged: the cylinder advances after every
            // trigger pull, valid or not.
            _localBulletPosition = (_localBulletPosition + 1) % LocalChamberSize;
        }
        else
        {
            hasShot.Value = true;
        }
        StartCoroutine(Triggering());
    }

    // Offline bullet: raycast through the crosshair/cursor, spawn the prefab locally and
    // clean it up after a few seconds - no NetworkObject involved.
    private void SpawnLocalBullet(Vector3 spawnPosition)
    {
        if (bulletPrefab == null)
        {
            return;
        }

        bullet = Instantiate(bulletPrefab, spawnPosition, Quaternion.identity);

        Vector3 aimPoint = spawnPosition + transform.forward * 100f;
        if (Camera.main != null && Mouse.current != null)
        {
            Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
            aimPoint = Physics.Raycast(ray, out RaycastHit hit) ? hit.point : ray.GetPoint(100f);
        }
        else if (targetAim != null)
        {
            aimPoint = targetAim.position;
        }

        Vector3 direction = (aimPoint - spawnPosition).normalized;
        if (bullet.TryGetComponent(out Rigidbody rb))
        {
            rb.linearVelocity = direction * 15f;
        }

        Destroy(bullet, 5f);

        if (vfxPrefab != null)
        {
            GameObject vfx = Instantiate(vfxPrefab, spawnPosition, Quaternion.identity);
            Destroy(vfx, 1f);
        }
    }

    private System.Collections.IEnumerator Triggering()
    {
        // Wait until the "Shooting" animation has finished playing. The hands animator lives at
        // index 4 on the networked player prefab; fall back to the last available animator when
        // fewer are assigned (offline tutorial rig).
        if (animators.Length == 0)
        {
            isTriggered = false;
            yield break;
        }
        int shootingCheckIndex = Mathf.Min(4, animators.Length - 1);
        // Safety cutoff: if the "Shooting" state ever lingers (missing exit transition, etc.)
        // isTriggered must still clear or the gun could never be triggered again.
        float triggerResetCutoff = Time.time + 3f;
        while (animators[shootingCheckIndex].GetCurrentAnimatorStateInfo(0).IsName("Shooting") && Time.time < triggerResetCutoff)
        {
            yield return null;
        }
        isTriggered = false;
        foreach (Animator animator in animators)
        {
            animator.SetBool("Triggered", isTriggered);
        }
    }

    [ServerRpc]
    public void ShootServerRpc(Vector3 spawnPoint, Quaternion rot, Vector3 targetAim, bool haveToReload = true, ServerRpcParams serverRpcParams = default)
    {
        GameManager.Instance.isReloaded.Value = !haveToReload;

        bullet = Instantiate(bulletPrefab, spawnPoint, rot);
        var bulletNetworkObject = bullet.GetComponent<NetworkObject>();
        bulletNetworkObject.SpawnWithOwnership(serverRpcParams.Receive.SenderClientId);

        Vector3 direction = (targetAim - spawnPoint).normalized;

        if (bullet.TryGetComponent(out BulletBehavior bulletBehavior))
        {
            bulletBehavior.initialVelocity.Value = direction;
        }

        GameObject vfx = Instantiate(vfxPrefab, spawnPoint, rot);
        NetworkObject networkVfx = vfx.GetComponent<NetworkObject>();
        networkVfx.Spawn(); // Or SpawnWithOwnership if needed
        StartCoroutine(DestroyVfxAfterDelay(networkVfx, GetVfxLifetime(vfx)));

        PlayShootSoundClientRpc(spawnPoint);
    }

    private void HandleHasShotChanged(bool oldValue, bool newValue)
    {
        // hasShot.OnValueChanged fires on every peer that observes the sync (owner and
        // observers alike) - only the owning client should ever report its own shot outcome.
        if (!IsOwner)
        {
            return;
        }

        OnHasShotChangedServerRpc(oldValue, newValue);
    }

    // RequireOwnership (the default) means Netcode itself rejects this if a non-owner ever
    // calls it directly, closing the exploit where any client could report any other player's
    // shot outcome.
    [ServerRpc]
    private void OnHasShotChangedServerRpc(bool oldValue, bool newValue)
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnClientShotChanged(OwnerClientId, newValue);
        }
    }

    [ServerRpc]
    private void ReloadServerRpc()
    {
        if (GameManager.Instance)
        {
            GameManager.Instance.Reload();
        }
    }

    private void PlayReloadSound(Vector3 position)
    {
        if (CanUseNetcode())
        {
            PlayReloadSoundServerRpc(position);
            return;
        }

        PlayLocalOneShot(SFXManager.Instance.reloadClip, position);
    }

    private void PlayTriggerSound(Vector3 position)
    {
        if (CanUseNetcode())
        {
            PlayTriggerSoundServerRpc(position);
            return;
        }

        PlayLocalOneShot(SFXManager.Instance.triggerClip, position);
    }

    private void PlayShootSound(Vector3 position)
    {
        if (CanUseNetcode())
        {
            PlayShootSoundServerRpc(position);
            return;
        }

        PlayLocalOneShot(SFXManager.Instance.shootClip, position);
    }

    private void PlayEmptyShotSound(Vector3 position)
    {
        if (CanUseNetcode())
        {
            PlayEmptyShotSoundServerRpc(position);
            return;
        }

        PlayLocalOneShot(SFXManager.Instance.emptyShotClip, position);
    }

    [ServerRpc(RequireOwnership = false)]
    private void PlayReloadSoundServerRpc(Vector3 position)
    {
        PlayReloadSoundClientRpc(position);
    }

    [ClientRpc]
    private void PlayReloadSoundClientRpc(Vector3 position)
    {
        PlayLocalOneShot(SFXManager.Instance.reloadClip, position);
    }

    [ServerRpc(RequireOwnership = false)]
    private void PlayTriggerSoundServerRpc(Vector3 position)
    {
        PlayTriggerSoundClientRpc(position);
    }

    [ClientRpc]
    private void PlayTriggerSoundClientRpc(Vector3 position)
    {
        PlayLocalOneShot(SFXManager.Instance.triggerClip, position);
    }

    [ServerRpc(RequireOwnership = false)]
    private void PlayShootSoundServerRpc(Vector3 position)
    {
        PlayShootSoundClientRpc(position);
    }

    [ClientRpc]
    private void PlayShootSoundClientRpc(Vector3 position)
    {
        PlayLocalOneShot(SFXManager.Instance.shootClip, position);
    }

    [ServerRpc(RequireOwnership = false)]
    private void PlayEmptyShotSoundServerRpc(Vector3 position)
    {
        PlayEmptyShotSoundClientRpc(position);
    }

    [ClientRpc]
    private void PlayEmptyShotSoundClientRpc(Vector3 position)
    {
        PlayLocalOneShot(SFXManager.Instance.emptyShotClip, position);
    }

    private void PlayLocalOneShot(AudioClip clip, Vector3 position)
    {
        if (SFXManager.Instance != null)
        {
            SFXManager.Instance.PlayAt(clip, position);
        }
    }

    private bool CanUseNetcode()
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
    }

    private void HandsState(bool state)
    {
        // animators/fPHands may legitimately be unset briefly (offline injection happens
        // right after AddComponent, whose OnEnable lands here first).
        if (animators != null)
        {
            foreach (Animator anim in animators)
            {
                anim.SetBool("HaveAGun", state);

                if (!state)
                {
                    // The gun can be taken away mid Trigger/Reload/Shooting animation (round
                    // ends, timeout hand-off) - flipping HaveAGun alone waits on the animator's
                    // own exit transitions, so a stuck Triggered bool or an in-progress state
                    // could leave the hands frozen mid-pose. Hard-cut back to the unarmed
                    // slap-idle stance instead.
                    anim.SetBool("Triggered", false);
                    AnimatorStateInfo stateInfo = anim.GetCurrentAnimatorStateInfo(0);
                    if ((stateInfo.IsName("Trigger") || stateInfo.IsName("Reload") || stateInfo.IsName("Shooting"))
                        && anim.HasState(0, Animator.StringToHash("NoGunMovement")))
                    {
                        anim.Play("NoGunMovement", 0, 0f);
                    }
                }
            }
        }
        if (fPHands != null)
        {
            fPHands.SwitchParent(state);
        }
        if (slapScript != null)
        {
            slapScript.enabled = !state;
        }
    }

    private System.Collections.IEnumerator DestroyVfxAfterDelay(NetworkObject netObj, float delay)
    {
        yield return new WaitForSeconds(delay);
        netObj.Despawn();
    }

    private float GetVfxLifetime(GameObject vfx)
    {
        if (vfx != null)
        {
            ParticleSystem ps = vfx.GetComponentInChildren<ParticleSystem>();
            if (ps != null)
            {
                var main = ps.main;
                return Mathf.Max(main.duration, main.startLifetime.constantMax) + 0.25f;
            }
        }

        return 1f;
    }
}
