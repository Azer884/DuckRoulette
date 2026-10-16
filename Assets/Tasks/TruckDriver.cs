using System.Collections.Generic;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// The reward for fixing the truck: once the engine is back in (TruckEngine), any duck can press
// Drive on the truck and joyride it for a few seconds before the engine blows again and a fresh
// one appears where the map put it.
//
// Server-authoritative like the rest of the truck: the driver's client only sends its stick and
// handbrake, the server runs the driving model on the truck's Rigidbody (PushableTruck steps
// aside while IsDriven) and NetworkTransform/NetworkRigidbody replicate the result.
//
// While driving:
//   - the driver is hidden in the cab on every peer, and their own client looks through
//     driveCamera, a third-person rig parented to the truck;
//   - the drive timer runs on the hiding-spot bar (HidingSpotTimerUI) in its own colour;
//   - anyone the truck hits is thrown by the truck's speed and knocked out (GameManager);
//   - hitting a wall hard ends the drive early.
// When the timer runs out the engine explodes, the driver is dropped in a free spot beside the
// truck, and the engine respawns.
//
// Goes on Assets/Prefabs/Map/Truck.prefab beside PushableTruck. The Drive prompt comes from
// TruckDriveSeat on a trigger child on the Interactable layer.
[RequireComponent(typeof(PushableTruck))]
public class TruckDriver : NetworkBehaviour
{
    [Header("References")]
    [SerializeField, Tooltip("Third-person camera, a child of the truck, left inactive. Switched " +
        "on for the driver's own client only.")]
    private CinemachineCamera driveCamera;

    [SerializeField, Tooltip("Where the hidden driver sits. Defaults to the truck's pivot.")]
    private Transform seat;

    [Header("Timer")]
    [SerializeField] private float driveDuration = 4f;
    [SerializeField, Tooltip("Colour of the drive timer on the hiding-spot bar.")]
    private Color timerColor = new(1f, 0.35f, 0.1f, 1f);

    [Header("Engine and gears")]
    [SerializeField] private float maxForwardSpeed = 22f;
    [SerializeField] private float maxReverseSpeed = 8f;
    [SerializeField, Tooltip("m/s².")] private float acceleration = 16f;
    [SerializeField, Tooltip("m/s², when pressing against the direction of travel.")]
    private float brakeDeceleration = 30f;
    [SerializeField, Tooltip("m/s², with no throttle.")] private float coastDeceleration = 4f;

    [Header("Steering and grip")]
    [SerializeField, Tooltip("Turn rate at full lock, in degrees per second.")]
    private float maxSteerRate = 120f;
    [SerializeField, Tooltip("Speed (m/s) below which steering is weaker, like a real car that " +
        "can't turn on the spot.")]
    private float fullSteerSpeed = 7f;
    [SerializeField, Tooltip("Fraction of the turn rate kept at top speed.")]
    private float highSpeedSteerFactor = 0.6f;
    [SerializeField, Tooltip("How quickly the turn rate follows the stick, per second.")]
    private float steerResponse = 10f;
    [SerializeField, Tooltip("How quickly sideways slide is killed, per second.")]
    private float grip = 9f;

    [Header("Drifting (hold Jump while steering)")]
    [SerializeField, Tooltip("Grip at full drift - low enough for the tail to swing out.")]
    private float driftGrip = 0.5f;
    [SerializeField, Tooltip("How hard a sliding tail keeps swinging round on its own, in degrees " +
        "per second² per m/s of slide. Higher = harder to catch.")]
    private float driftOversteer = 9f;
    [SerializeField, Tooltip("Random yaw shove while drifting, in degrees per second² - the truck " +
        "twitches and fishtails instead of gliding.")]
    private float driftWobble = 200f;
    [SerializeField, Range(0.05f, 1f), Tooltip("How much of the normal steering precision is left " +
        "at full drift. Lower = the stick only loosely corrects the slide.")]
    private float driftSteerControl = 0.3f;
    [SerializeField, Tooltip("Turn rate multiplier at full drift.")]
    private float driftSteerMultiplier = 1.6f;
    [SerializeField, Tooltip("Below this speed (m/s) the handbrake just brakes.")]
    private float minDriftSpeed = 7f;
    [SerializeField, Tooltip("How fast a drift builds up while the handbrake is held, per second.")]
    private float driftEnterRate = 5f;
    [SerializeField, Tooltip("How fast grip comes back after letting go, per second - slow enough " +
        "that the truck straightens out instead of snapping straight.")]
    private float driftExitRate = 1.2f;
    [SerializeField, Tooltip("Share of the sideways speed the slide scrubs off that is turned back " +
        "into forward speed, so a drift keeps its momentum instead of stalling.")]
    private float driftMomentumKeep = 0.45f;
    [SerializeField, Tooltip("Widest angle (degrees) between where the truck points and where it " +
        "slides; beyond it the slide is pulled back in so the truck never fully spins out.")]
    private float maxDriftAngle = 85f;
    [SerializeField, Tooltip("m/s², slowing the truck while the handbrake is held.")]
    private float handbrakeDrag = 1.5f;
    [SerializeField, Tooltip("Sideways speed (m/s) that counts as drifting, for the tyre smoke.")]
    private float driftSlipThreshold = 2.5f;
    [SerializeField, Tooltip("Extra grip into the ground per (m/s)², so fast corners don't tip it.")]
    private float downforce = 0.04f;
    [SerializeField, Tooltip("How hard the truck rights itself, so a jump never ends on its roof.")]
    private float uprightStrength = 6f;

    [Header("Running players over")]
    [SerializeField] private LayerMask playerLayers = 1 << 3;
    [SerializeField, Tooltip("Below this speed (m/s) the truck just nudges people.")]
    private float minBumpSpeed = 3f;
    [SerializeField, Tooltip("Launch speed per m/s of truck speed.")]
    private float launchPerSpeed = 1.1f;
    [SerializeField, Tooltip("Upward launch speed per m/s of truck speed.")]
    private float launchUpPerSpeed = 0.45f;
    [SerializeField] private float maxLaunchSpeed = 30f;
    [SerializeField, Tooltip("Knockout length at minBumpSpeed and at top speed, in seconds.")]
    private Vector2 knockoutSeconds = new(2f, 5f);
    [SerializeField, Tooltip("Seconds before the same player can be hit again.")]
    private float bumpCooldown = 1.5f;
    [SerializeField, Tooltip("Extra reach around the truck's body when looking for players, in metres.")]
    private float bumpMargin = 0.4f;

    [Header("Crashing")]
    [SerializeField, Tooltip("Impact speed into a wall (m/s) that blows the engine early.")]
    private float crashSpeed = 12f;
    [SerializeField, Tooltip("A loose rigidbody lighter than this (kg) is knocked aside instead of " +
        "counting as a crash.")]
    private float crashMinMass = 500f;
    [SerializeField, Tooltip("Impact speed that still gets a bump sound and effect.")]
    private float bumpFeedbackSpeed = 4f;

    [Header("SFX (placeholders - assign clips)")]
    [SerializeField] private AudioClip engineLoopClip;
    [SerializeField] private float engineIdlePitch = 0.8f;
    [SerializeField] private float engineTopPitch = 1.8f;
    [SerializeField] private AudioClip bumpClip;
    [SerializeField] private AudioClip crashClip;
    [SerializeField] private AudioClip explosionClip;

    [Header("VFX (placeholders)")]
    [SerializeField, Tooltip("Looping effect parented at each drift point, played while sliding.")]
    private GameObject driftVfxPrefab;
    [SerializeField, Tooltip("Rear wheels. Defaults to the truck's pivot.")]
    private Transform[] driftPoints;
    [SerializeField] private GameObject bumpVfxPrefab;
    [SerializeField] private float bumpVfxLifetime = 1.5f;
    [SerializeField] private GameObject explosionVfxPrefab;
    [SerializeField] private float explosionVfxLifetime = 3f;

    private const float MaxDriveRequestDistance = 10f;
    private const float InputTimeout = 0.5f;

    private readonly NetworkVariable<ulong> driverId = new(ulong.MaxValue);
    private readonly NetworkVariable<double> driveEndsAt = new(0d);
    private readonly NetworkVariable<bool> drifting = new(false);
    // The driver's steering, -1..1, so every peer turns the front wheels.
    private readonly NetworkVariable<float> wheelSteer = new(0f);
    private float driftAmount;

    private PushableTruck truck;
    private Rigidbody body;
    private TruckEngine engine;
    private AudioSource engineAudio;
    private readonly List<ParticleSystem> driftEffects = new();
    private Bounds localBodyBounds;

    // Server: the driver's latest controls.
    private Vector2 serverInput;
    private bool serverHandbrake;
    private float serverInputTime;
    private readonly Dictionary<ulong, float> nextBumpAllowed = new();
    private readonly Collider[] overlapHits = new Collider[32];
    private readonly HashSet<ulong> bumpedThisStep = new();

    // Driver's own client.
    private bool isLocalDriver;
    private InputAction moveAction;
    private InputAction handbrakeAction;
    private Vector2 lastSentInput;
    private bool lastSentHandbrake;
    private float inputSendTimer;

    // Every peer, for the engine pitch.
    private Vector3 lastPosition;
    private float smoothedSpeed;

    // True only between an engine being installed in THIS truck and the drive blowing it up.
    // Set by TruckEngine.Install itself, rather than read off whichever engine a scene search
    // happens to find, so the truck can never be driven without its engine actually going in.
    private readonly NetworkVariable<bool> engineInstalled = new(false);

    public bool IsBeingDriven => driverId.Value != ulong.MaxValue;

    /// <summary>Whether the Drive prompt should show for the local player.</summary>
    public bool CanLocalPlayerDrive => IsSpawned && !IsBeingDriven && engineInstalled.Value;

    /// <summary>Server only. TruckEngine calls this when its engine goes into this truck.</summary>
    public void ServerEngineInstalled()
    {
        if (IsServer && IsSpawned)
        {
            engineInstalled.Value = true;
        }
    }

    /// <summary>Server only. The engine was taken back out (respawned for a new task holder)
    /// before anyone drove: the truck is broken again.</summary>
    public void ServerEngineRemoved()
    {
        if (IsServer && IsSpawned && !IsBeingDriven)
        {
            engineInstalled.Value = false;
        }
    }

    private void Awake()
    {
        truck = GetComponent<PushableTruck>();
        body = GetComponent<Rigidbody>();
        localBodyBounds = ComputeLocalBodyBounds();
        lastPosition = transform.position;

        if (driveCamera != null)
        {
            driveCamera.gameObject.SetActive(false);
        }

        engineAudio = gameObject.AddComponent<AudioSource>();
        engineAudio.clip = engineLoopClip;
        engineAudio.loop = true;
        engineAudio.playOnAwake = false;
        engineAudio.spatialBlend = 1f;
        engineAudio.minDistance = 5f;
        engineAudio.maxDistance = 60f;

        BuildDriftEffects();
    }

    public override void OnNetworkSpawn()
    {
        driverId.OnValueChanged += OnDriverChanged;
        drifting.OnValueChanged += OnDriftingChanged;
        OnDriftingChanged(false, drifting.Value);
        UpdateEngineAudio(true);
    }

    public override void OnNetworkDespawn()
    {
        driverId.OnValueChanged -= OnDriverChanged;
        drifting.OnValueChanged -= OnDriftingChanged;
        if (isLocalDriver)
        {
            ExitLocalDriving(transform.position, false);
        }
    }

    #region Drive request (client)

    /// <summary>Called by TruckDriveSeat when the local player presses Drive.</summary>
    public void RequestDrive()
    {
        if (CanLocalPlayerDrive)
        {
            DriveServerRpc();
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void DriveServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        if (IsBeingDriven || !engineInstalled.Value || !TryGetPlayer(sender, out NetworkObject player))
        {
            return;
        }

        GameManager game = GameManager.Instance;
        bool dead = player.TryGetComponent(out Death death) && death.isDead.Value;
        // The gun holder has a turn to take - except for a lone tester, who always holds it.
        bool busyWithGun = game != null && game.playerWithGun.Value == sender && !TaskManager.SoloTestMode;
        if (dead || busyWithGun || (game != null && !game.IsPlayerAlive(sender)) ||
            HidingSpot.IsClientHiding(sender) || DistanceToHull(player.transform.position) > MaxDriveRequestDistance)
        {
            return;
        }

        serverInput = Vector2.zero;
        serverHandbrake = false;
        serverInputTime = Time.time;
        driftAmount = 0f;
        nextBumpAllowed.Clear();

        truck.IsDriven = true;
        driveEndsAt.Value = NetworkManager.ServerTime.Time + driveDuration;
        driverId.Value = sender;

        DriveStartedClientRpc(sender, player.NetworkObjectId);
    }

    [ClientRpc]
    private void DriveStartedClientRpc(ulong driver, ulong playerObjectId)
    {
        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(playerObjectId, out NetworkObject player))
        {
            return;
        }

        SetDriverVisible(player.gameObject, false);

        if (driver == NetworkManager.LocalClientId)
        {
            EnterLocalDriving(player.gameObject);
        }
    }

    #endregion

    #region Driver's own client

    private void EnterLocalDriving(GameObject player)
    {
        isLocalDriver = true;
        lastSentInput = Vector2.zero;
        lastSentHandbrake = false;

        if (player.TryGetComponent(out InputSystem input))
        {
            moveAction = input.inputActions.FindAction("Move");
            handbrakeAction = input.inputActions.FindAction("Jump");
        }

        SetLocalScriptsEnabled(player, false);

        if (driveCamera != null)
        {
            // The follow offset is in world units while the map scatter scales each truck, so
            // it is sized from this truck's real body every time: up and behind, clear of the tail.
            if (driveCamera.TryGetComponent(out CinemachineFollow follow))
            {
                Vector3 half = Vector3.Scale(localBodyBounds.extents, transform.lossyScale);
                follow.FollowOffset = new Vector3(0f, half.y * 2f + 2.5f, -(half.z + 8f));
            }

            driveCamera.Priority = new PrioritySettings { Enabled = true, Value = 20 };
            driveCamera.gameObject.SetActive(true);
        }
    }

    private void ExitLocalDriving(Vector3 dropPosition, bool restoreScripts)
    {
        isLocalDriver = false;
        moveAction = null;
        handbrakeAction = null;
        HidingSpotTimerUI.HideExternal(this);

        if (driveCamera != null)
        {
            driveCamera.gameObject.SetActive(false);
        }

        NetworkObject localPlayer = NetworkManager.Singleton != null
            ? NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject()
            : null;
        if (localPlayer == null)
        {
            return;
        }

        // Owner-authoritative transform: only this client can put the player down. The
        // CharacterController is still off here (SetDriverVisible turns it back on after).
        // dropPosition is where the feet go; the player's pivot sits above the bottom of its
        // controller, and starting the controller inside the ground drops it straight through.
        if (localPlayer.TryGetComponent(out CharacterController controller))
        {
            float bottom = (controller.center.y - controller.height * 0.5f) * localPlayer.transform.lossyScale.y;
            dropPosition.y += -bottom + controller.skinWidth + 0.05f;
        }

        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        Quaternion facing = forward.sqrMagnitude > 0.001f ? Quaternion.LookRotation(forward) : localPlayer.transform.rotation;
        localPlayer.transform.SetPositionAndRotation(dropPosition, facing);

        if (restoreScripts)
        {
            SetLocalScriptsEnabled(localPlayer.gameObject, true);
        }
    }

    // Same set HidingSpot hands over, plus Interact so the driver can't grab things from the cab.
    private static void SetLocalScriptsEnabled(GameObject player, bool enabledState)
    {
        if (player.TryGetComponent(out Movement movement)) movement.enabled = enabledState;
        if (player.TryGetComponent(out TeamUp teamUp)) teamUp.enabled = enabledState;
        if (player.TryGetComponent(out Interact interact)) interact.enabled = enabledState;

        if (player.TryGetComponent(out Shooting shooting) && player.TryGetComponent(out Slap slap))
        {
            bool hasGun = enabledState && shooting.HasGun;
            shooting.enabled = hasGun;
            slap.enabled = enabledState && !hasGun;
        }
    }

    private void Update()
    {
        UpdateEngineAudio(false);
        truck.SteerInput = IsSpawned && IsBeingDriven ? wheelSteer.Value : 0f;

        if (!isLocalDriver)
        {
            return;
        }

        float remaining = (float)(driveEndsAt.Value - NetworkManager.ServerTime.Time);
        HidingSpotTimerUI.ShowExternal(this, Mathf.Max(0f, remaining), driveDuration, timerColor);

        Vector2 move = moveAction != null ? Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f) : Vector2.zero;
        bool handbrake = handbrakeAction != null && handbrakeAction.IsPressed();

        // Only when it changes, plus a keep-alive so the server's input timeout never trips.
        inputSendTimer -= Time.deltaTime;
        if (move != lastSentInput || handbrake != lastSentHandbrake || inputSendTimer <= 0f)
        {
            inputSendTimer = 0.15f;
            lastSentInput = move;
            lastSentHandbrake = handbrake;
            DriveInputServerRpc(move, handbrake);
        }
    }

    private void LateUpdate()
    {
        if (!isLocalDriver || NetworkManager.Singleton == null)
        {
            return;
        }

        // The hidden driver rides along, so the server's view of them (range checks, voice
        // position) stays at the truck.
        NetworkObject localPlayer = NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject();
        if (localPlayer != null)
        {
            Transform at = seat != null ? seat : transform;
            Vector3 forward = Vector3.ProjectOnPlane(at.forward, Vector3.up);
            localPlayer.transform.SetPositionAndRotation(at.position,
                forward.sqrMagnitude > 0.001f ? Quaternion.LookRotation(forward) : localPlayer.transform.rotation);
        }
    }

    [ServerRpc(RequireOwnership = false, Delivery = RpcDelivery.Unreliable)]
    private void DriveInputServerRpc(Vector2 move, bool handbrake, ServerRpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != driverId.Value)
        {
            return;
        }

        serverInput = Vector2.ClampMagnitude(move, 1f);
        serverHandbrake = handbrake;
        serverInputTime = Time.time;
    }

    #endregion

    #region Server: driving model

    private void FixedUpdate()
    {
        if (!IsServer || !IsSpawned || !IsBeingDriven)
        {
            return;
        }

        ulong driver = driverId.Value;
        bool driverGone = !TryGetPlayer(driver, out NetworkObject player) ||
                          (player.TryGetComponent(out Death death) && death.isDead.Value) ||
                          (GameManager.Instance != null && !GameManager.Instance.IsPlayerAlive(driver));

        if (driverGone || NetworkManager.ServerTime.Time >= driveEndsAt.Value)
        {
            EndDrive();
            return;
        }

        if (Time.time - serverInputTime > InputTimeout)
        {
            serverInput = Vector2.zero;
            serverHandbrake = false;
        }

        Drive(Time.fixedDeltaTime);
        RunPlayersOver();

        if (Mathf.Abs(wheelSteer.Value - serverInput.x) > 0.05f)
        {
            wheelSteer.Value = serverInput.x;
        }
    }

    private void Drive(float dt)
    {
        Vector3 up = transform.up;
        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;

        // Keep the wheels down: a gentle pull back to upright, on the ground and in the air.
        Vector3 tilt = Vector3.Cross(up, Vector3.up);
        body.AddTorque(tilt * uprightStrength, ForceMode.Acceleration);

        if (!truck.IsGrounded())
        {
            SetDrifting(false);
            return;
        }

        Vector3 velocity = body.linearVelocity;
        float along = Vector3.Dot(velocity, forward);
        float sideways = Vector3.Dot(velocity, right);
        float throttle = serverInput.y;
        float steer = serverInput.x;
        bool handbrake = serverHandbrake;

        // Throttle, brake and reverse.
        float change;
        if (throttle > 0.05f)
        {
            change = along < -0.5f
                ? brakeDeceleration * throttle * dt
                : along < maxForwardSpeed ? acceleration * throttle * dt : 0f;
        }
        else if (throttle < -0.05f)
        {
            change = along > 0.5f
                ? -brakeDeceleration * -throttle * dt
                : along > -maxReverseSpeed ? -acceleration * 0.6f * -throttle * dt : 0f;
        }
        else
        {
            change = Mathf.MoveTowards(along, 0f, coastDeceleration * dt) - along;
        }

        if (handbrake)
        {
            change += Mathf.MoveTowards(along, 0f, handbrakeDrag * dt) - along;
        }

        body.AddForce(forward * change, ForceMode.VelocityChange);

        // Drift amount builds while the handbrake is held at speed and fades out after letting
        // go, so grip comes back gradually - the tail swings out and the truck then straightens
        // up, instead of grip flicking on and off with the button.
        bool wantsDrift = handbrake && along > minDriftSpeed;
        driftAmount = Mathf.MoveTowards(driftAmount, wantsDrift ? 1f : 0f,
            (wantsDrift ? driftEnterRate : driftExitRate) * dt);

        // Tyres: grip pulls the slide back in line. Part of what a drift scrubs off sideways is
        // handed back as forward speed, so sliding through a corner keeps the truck's momentum.
        float gripRate = Mathf.Lerp(grip, driftGrip, driftAmount);
        float scrubbed = sideways * Mathf.Clamp01(gripRate * dt);
        body.AddForce(-right * scrubbed, ForceMode.VelocityChange);
        if (driftAmount > 0f && along > 0f && along < maxForwardSpeed)
        {
            body.AddForce(forward * (Mathf.Abs(scrubbed) * driftMomentumKeep * driftAmount), ForceMode.VelocityChange);
        }

        // The momentum hand-back must not turn a drift into a speed boost.
        Vector3 flat = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
        if (driftAmount > 0f && flat.magnitude > maxForwardSpeed)
        {
            body.AddForce(flat.normalized * maxForwardSpeed - flat, ForceMode.VelocityChange);
            flat = flat.normalized * maxForwardSpeed;
        }

        // Never past maxDriftAngle: the slide is swung back towards the nose, so a long drift
        // stays a drift instead of becoming a spin.
        if (along > minDriftSpeed * 0.5f && flat.sqrMagnitude > 1f)
        {
            float slipAngle = Vector3.SignedAngle(forward, flat, Vector3.up);
            if (Mathf.Abs(slipAngle) > maxDriftAngle)
            {
                Vector3 limited = Quaternion.AngleAxis(Mathf.Sign(slipAngle) * maxDriftAngle, Vector3.up) * forward * flat.magnitude;
                body.AddForce(limited - flat, ForceMode.VelocityChange);
            }
        }

        // Steering is a turn rate, not a force: it scales with speed (no turning on the spot),
        // flips when reversing, and eases off at top speed.
        float speed01 = Mathf.Clamp01(Mathf.Abs(along) / maxForwardSpeed);
        float lowSpeed = Mathf.Clamp01(Mathf.Abs(along) / fullSteerSpeed);
        float rate = steer * maxSteerRate * Mathf.Deg2Rad * lowSpeed * Mathf.Sign(along) *
                     Mathf.Lerp(1f, highSpeedSteerFactor, speed01) * Mathf.Lerp(1f, driftSteerMultiplier, driftAmount);
        float yaw = Vector3.Dot(body.angularVelocity, Vector3.up);
        float response = steerResponse * Mathf.Lerp(1f, driftSteerControl, driftAmount);
        body.AddTorque(Vector3.up * ((rate - yaw) * Mathf.Clamp01(response * dt)), ForceMode.VelocityChange);

        // Out of control: a sliding tail keeps swinging the way it is already going (worse the
        // faster it slides), and the truck twitches as it fishtails.
        if (driftAmount > 0f)
        {
            float slide = Vector3.Dot(body.linearVelocity, right);
            float swing = -Mathf.Sign(slide) * Mathf.Abs(slide) * driftOversteer;
            float wobble = (Mathf.PerlinNoise(Time.time * 2.5f, 0.37f) - 0.5f) * 2f * driftWobble;
            body.AddTorque(Vector3.up * ((swing * dt + wobble * dt) * Mathf.Deg2Rad * driftAmount), ForceMode.VelocityChange);
        }

        body.AddForce(-up * (along * along * downforce), ForceMode.Acceleration);

        SetDrifting(Mathf.Abs(sideways) > driftSlipThreshold && Mathf.Abs(along) > 4f);
    }

    private void SetDrifting(bool value)
    {
        if (drifting.Value != value)
        {
            drifting.Value = value;
        }
    }

    private void RunPlayersOver()
    {
        float speed = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up).magnitude;
        if (speed < minBumpSpeed || GameManager.Instance == null)
        {
            return;
        }

        Vector3 scale = transform.lossyScale;
        Vector3 half = Vector3.Scale(localBodyBounds.extents, scale) + Vector3.one * bumpMargin;
        Vector3 centre = transform.TransformPoint(localBodyBounds.center);
        int count = Physics.OverlapBoxNonAlloc(centre, half, overlapHits, transform.rotation, playerLayers,
            QueryTriggerInteraction.Ignore);

        bumpedThisStep.Clear();
        for (int i = 0; i < count; i++)
        {
            NetworkObject victim = overlapHits[i].GetComponentInParent<NetworkObject>();
            if (victim == null || !victim.IsPlayerObject)
            {
                continue;
            }

            ulong id = victim.OwnerClientId;
            if (id == driverId.Value || !bumpedThisStep.Add(id) || HidingSpot.IsClientHiding(id) ||
                !GameManager.Instance.IsPlayerAlive(id) ||
                (nextBumpAllowed.TryGetValue(id, out float allowedAt) && Time.time < allowedAt))
            {
                continue;
            }

            nextBumpAllowed[id] = Time.time + bumpCooldown;

            // Mostly along the truck's travel, partly straight away from it, and up.
            Vector3 travel = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up).normalized;
            Vector3 away = Vector3.ProjectOnPlane(victim.transform.position - transform.position, Vector3.up).normalized;
            Vector3 direction = (travel * 0.7f + away * 0.3f).normalized;
            Vector3 launch = Vector3.ClampMagnitude(direction * (speed * launchPerSpeed) + Vector3.up * (speed * launchUpPerSpeed), maxLaunchSpeed);
            float knockout = Mathf.Lerp(knockoutSeconds.x, knockoutSeconds.y,
                Mathf.InverseLerp(minBumpSpeed, maxForwardSpeed, speed));

            GameManager.Instance.KnockOutPlayer(id, launch, knockout);
            BumpClientRpc(victim.transform.position + Vector3.up, false);
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!IsServer || !IsBeingDriven || collision.collider.isTrigger)
        {
            return;
        }

        // Players are handled by RunPlayersOver; the ground is not a wall.
        NetworkObject other = collision.collider.GetComponentInParent<NetworkObject>();
        if (other != null && other.IsPlayerObject)
        {
            return;
        }

        float impact = 0f;
        Vector3 point = transform.position;
        for (int i = 0; i < collision.contactCount; i++)
        {
            ContactPoint contact = collision.GetContact(i);
            if (contact.normal.y > 0.6f)
            {
                continue;
            }

            float hit = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, contact.normal));
            if (hit > impact)
            {
                impact = hit;
                point = contact.point;
            }
        }

        // Loose props (a boombox, the engine) get bowled over; only walls and heavy things count
        // as a crash.
        Rigidbody otherBody = collision.rigidbody;
        bool lightProp = otherBody != null && !otherBody.isKinematic && otherBody.mass < crashMinMass;

        if (impact >= crashSpeed && !lightProp)
        {
            BumpClientRpc(point, true);
            EndDrive();
        }
        else if (impact >= bumpFeedbackSpeed)
        {
            BumpClientRpc(point, false);
        }
    }

    // Engine blows: timer out, a hard crash, or the driver died or left.
    private void EndDrive()
    {
        ulong driver = driverId.Value;
        ulong playerObjectId = TryGetPlayer(driver, out NetworkObject player) ? player.NetworkObjectId : 0;
        Vector3 drop = FindDropSpot();

        driverId.Value = ulong.MaxValue;
        engineInstalled.Value = false;
        SetDrifting(false);
        driftAmount = 0f;
        wheelSteer.Value = 0f;
        truck.IsDriven = false;

        // Dead engine: the truck lurches and loses most of its speed.
        body.linearVelocity *= 0.25f;
        body.AddForce(Vector3.up * 4f, ForceMode.VelocityChange);

        FindEngine()?.ServerRespawn();

        DriveEndedClientRpc(driver, playerObjectId, drop);
    }

    // Beside the truck, wherever there is room for a duck: left, right, behind, in front.
    private Vector3 FindDropSpot()
    {
        Vector3 scale = transform.lossyScale;
        Vector3 half = Vector3.Scale(localBodyBounds.extents, scale);
        Vector3 centre = transform.TransformPoint(localBodyBounds.center);
        Vector3 right = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;
        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        const float clearance = 1.3f;

        Vector3[] candidates =
        {
            centre - right * (half.x + clearance),
            centre + right * (half.x + clearance),
            centre - forward * (half.z + clearance),
            centre + forward * (half.z + clearance),
            centre - right * (half.x + clearance * 2.5f),
            centre + right * (half.x + clearance * 2.5f),
        };

        int blocking = ~(playerLayers | (1 << 8) | (1 << 12) | (1 << 13));
        foreach (Vector3 candidate in candidates)
        {
            Vector3 origin = candidate + Vector3.up * 4f;
            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit ground, 12f, blocking, QueryTriggerInteraction.Ignore) ||
                ground.rigidbody == body)
            {
                continue;
            }

            Vector3 feet = ground.point + Vector3.up * 0.05f;
            if (!Physics.CheckCapsule(feet + Vector3.up * 0.5f, feet + Vector3.up * 1.6f, 0.45f, blocking, QueryTriggerInteraction.Ignore))
            {
                return feet;
            }
        }

        // Boxed in: on the roof.
        return centre + Vector3.up * (half.y + 0.5f);
    }

    [ClientRpc]
    private void DriveEndedClientRpc(ulong driver, ulong playerObjectId, Vector3 drop)
    {
        PlayOneShot(explosionClip, transform.position);
        if (explosionVfxPrefab != null)
        {
            VfxManager.SpawnOneShot(explosionVfxPrefab, transform.position + Vector3.up, explosionVfxLifetime);
        }

        NetworkObject player = null;
        if (playerObjectId != 0)
        {
            NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(playerObjectId, out player);
        }

        bool dead = player != null && player.TryGetComponent(out Death death) && death.isDead.Value;

        if (driver == NetworkManager.LocalClientId && isLocalDriver)
        {
            ExitLocalDriving(drop, !dead);
        }

        if (player != null && !dead)
        {
            SetDriverVisible(player.gameObject, true);
        }
    }

    [ClientRpc]
    private void BumpClientRpc(Vector3 point, bool crash)
    {
        PlayOneShot(crash ? (crashClip != null ? crashClip : bumpClip) : bumpClip, point);
        if (bumpVfxPrefab != null)
        {
            VfxManager.SpawnOneShot(bumpVfxPrefab, point, bumpVfxLifetime);
        }
    }

    #endregion

    #region Every peer: driver visibility, sound, smoke

    // Like a hiding spot: the driver is in the cab, so no model, nametag, gun or hitbox.
    private static void SetDriverVisible(GameObject player, bool visible)
    {
        if (player.TryGetComponent(out Movement movement))
        {
            movement.SetModelVisible(visible);
        }

        if (player.TryGetComponent(out Username username) && username.userName != null)
        {
            username.userName.gameObject.SetActive(visible && !username.IsOwner);
        }

        if (player.TryGetComponent(out Shooting shooting) && shooting.gun != null)
        {
            shooting.gun.SetActive(visible && shooting.HasGun);
        }

        if (player.TryGetComponent(out CharacterController controller))
        {
            controller.enabled = visible;
        }
    }

    private void OnDriverChanged(ulong previous, ulong current) => UpdateEngineAudio(true);

    private void UpdateEngineAudio(bool stateChanged)
    {
        Vector3 position = transform.position;
        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        float speed = (position - lastPosition).magnitude / dt;
        lastPosition = position;
        smoothedSpeed = Mathf.Lerp(smoothedSpeed, speed, Mathf.Clamp01(dt * 6f));

        if (engineAudio == null || engineAudio.clip == null)
        {
            return;
        }

        bool shouldPlay = IsSpawned && IsBeingDriven;
        if (stateChanged || shouldPlay != engineAudio.isPlaying)
        {
            if (shouldPlay && !engineAudio.isPlaying)
            {
                engineAudio.Play();
            }
            else if (!shouldPlay && engineAudio.isPlaying)
            {
                engineAudio.Stop();
            }
        }

        engineAudio.pitch = Mathf.Lerp(engineIdlePitch, engineTopPitch, Mathf.Clamp01(smoothedSpeed / maxForwardSpeed));
    }

    private void BuildDriftEffects()
    {
        if (driftVfxPrefab == null)
        {
            return;
        }

        Transform[] points = driftPoints != null && driftPoints.Length > 0 ? driftPoints : new[] { transform };
        foreach (Transform point in points)
        {
            if (point == null)
            {
                continue;
            }

            GameObject effect = Instantiate(driftVfxPrefab, point.position, point.rotation, point);

            // Authored at world size; undo the truck's own (map-scattered) scale.
            Vector3 parentScale = point.lossyScale;
            effect.transform.localScale = new Vector3(
                parentScale.x != 0f ? 1f / parentScale.x : 1f,
                parentScale.y != 0f ? 1f / parentScale.y : 1f,
                parentScale.z != 0f ? 1f / parentScale.z : 1f);
            foreach (ParticleSystem ps in effect.GetComponentsInChildren<ParticleSystem>())
            {
                ParticleSystem.MainModule main = ps.main;
                main.loop = true;
                main.playOnAwake = false;
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                driftEffects.Add(ps);
            }
        }
    }

    private void OnDriftingChanged(bool previous, bool current)
    {
        foreach (ParticleSystem ps in driftEffects)
        {
            if (ps == null)
            {
                continue;
            }

            if (current && !ps.isPlaying)
            {
                ps.Play(true);
            }
            else if (!current && ps.isPlaying)
            {
                ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }
    }

    private static void PlayOneShot(AudioClip clip, Vector3 position)
    {
        if (clip == null)
        {
            return;
        }

        if (SFXManager.Instance != null)
        {
            SFXManager.Instance.PlayAt(clip, position);
        }
        else
        {
            AudioSource.PlayClipAtPoint(clip, position);
        }
    }

    #endregion

    #region Helpers

    private TruckEngine FindEngine()
    {
        if (engine == null)
        {
            engine = FindAnyObjectByType<TruckEngine>();
        }

        return engine;
    }

    private bool TryGetPlayer(ulong clientId, out NetworkObject player)
    {
        player = null;
        return clientId != ulong.MaxValue && NetworkManager != null &&
               NetworkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client) &&
               (player = client.PlayerObject) != null;
    }

    private float DistanceToHull(Vector3 position)
    {
        float best = float.MaxValue;
        foreach (Collider c in GetComponentsInChildren<Collider>())
        {
            if (c.enabled && !c.isTrigger)
            {
                best = Mathf.Min(best, Vector3.Distance(c.ClosestPoint(position), position));
            }
        }

        return best;
    }

    // The solid body in the truck's own space, so the run-over box turns with it.
    private Bounds ComputeLocalBodyBounds()
    {
        Bounds bounds = new(Vector3.zero, Vector3.zero);
        bool any = false;

        foreach (Collider c in GetComponentsInChildren<Collider>(true))
        {
            if (c.isTrigger)
            {
                continue;
            }

            Vector3 centre;
            Vector3 extents;
            if (c is BoxCollider box)
            {
                centre = box.center;
                extents = box.size * 0.5f;
            }
            else if (c is SphereCollider sphere)
            {
                centre = sphere.center;
                extents = Vector3.one * sphere.radius;
            }
            else
            {
                continue;
            }

            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = centre + Vector3.Scale(extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                // Root space, before the root's scale - lossyScale is applied again when used.
                Vector3 local = transform.InverseTransformPoint(c.transform.TransformPoint(corner));

                if (any)
                {
                    bounds.Encapsulate(local);
                }
                else
                {
                    bounds = new Bounds(local, Vector3.zero);
                    any = true;
                }
            }
        }

        return any ? bounds : new Bounds(Vector3.up, new Vector3(3f, 3f, 9f));
    }

    #endregion
}
