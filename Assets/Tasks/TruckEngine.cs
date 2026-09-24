using Unity.Netcode;
using UnityEngine;

// "Find the truck engine": the engine lies somewhere on the map, the player with the task picks
// it up (it rides on their head exactly where a carried boombox does), carries it to the truck
// and presses Interact next to the truck to put it back in.
//
// Server-authoritative. The holder is a NetworkVariable, so every peer hides the real engine and
// shows a copy on the carrier's head; the server alone decides pick-up, drop and install.
// When the task is handed out again the engine goes back to where the map put it.
//
// Goes on Assets/Prefabs/Map/Placeholders/TruckEngine.prefab next to a NetworkObject, a
// NetworkTransform and a Rigidbody, with its collider on the Interact layer.
[RequireComponent(typeof(Rigidbody))]
public class TruckEngine : NetworkBehaviour, IInteractable
{
    [SerializeField, Tooltip("The task this engine completes once it is back in the truck.")]
    private Challenge engineTask;

    [SerializeField, Tooltip("How close to the truck the carrier has to be for Interact to install " +
        "the engine instead of dropping it, in metres from the truck's collider.")]
    private float installDistance = 4.5f;

    [SerializeField, Tooltip("Size of the carried copy's longest side, in metres.")]
    private float headModelSize = 0.9f;

    [SerializeField, Tooltip("How far in front of the carrier a dropped engine lands.")]
    private float dropForwardOffset = 1.2f;

    private readonly NetworkVariable<ulong> holder = new(ulong.MaxValue);
    private readonly NetworkVariable<bool> installed = new(false);

    public bool IsHeld { get => holder.Value != ulong.MaxValue; set { } }
    public bool IsPickable { get; set; } = false;

    public string InteractionPrompt => "Pick Up Engine";

    // Only the player who has to find it can pick it up, so nobody can carry it off to troll them.
    public bool CanInteract =>
        !IsHeld && !installed.Value && engineTask != null && TaskManager.Instance != null &&
        TaskManager.Instance.IsTaskOpenForLocalPlayer(engineTask);

    // Latching keeps Interact's reference, so the next Interact press lands in Drop() - which
    // installs it when next to the truck.
    public bool LatchesPlayer => true;

    private Rigidbody body;
    private Collider[] colliders;
    private Renderer[] renderers;
    private GameObject headModel;
    private Vector3 homePosition;
    private Quaternion homeRotation;
    private PushableTruck cachedTruck;

    private const float MaxInteractDistance = 8f;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        colliders = GetComponentsInChildren<Collider>(true);
        renderers = GetComponentsInChildren<Renderer>(true);
    }

    private void OnEnable()
    {
        TaskManager.RegisterObjective(engineTask);
        TaskManager.ServerTaskAssigned += OnServerTaskAssigned;
    }

    private void OnDisable()
    {
        TaskManager.UnregisterObjective(engineTask);
        TaskManager.ServerTaskAssigned -= OnServerTaskAssigned;
    }

    public override void OnNetworkSpawn()
    {
        homePosition = transform.position;
        homeRotation = transform.rotation;

        holder.OnValueChanged += OnHolderChanged;
        installed.OnValueChanged += OnInstalledChanged;
        ApplyState();
    }

    public override void OnNetworkDespawn()
    {
        holder.OnValueChanged -= OnHolderChanged;
        installed.OnValueChanged -= OnInstalledChanged;
        DestroyHeadModel();
    }

    private void OnHolderChanged(ulong previous, ulong current) => ApplyState();

    private void OnInstalledChanged(bool previous, bool current) => ApplyState();

    // Every peer: the real engine disappears while carried or installed, and the carrier wears a copy.
    private void ApplyState()
    {
        bool visible = !IsHeld && !installed.Value;

        foreach (Renderer r in renderers)
        {
            if (r != null)
            {
                r.enabled = visible;
            }
        }

        foreach (Collider c in colliders)
        {
            if (c != null)
            {
                c.enabled = visible;
            }
        }

        if (IsServer)
        {
            body.isKinematic = !visible;
        }

        DestroyHeadModel();
        if (IsHeld)
        {
            BuildHeadModel(holder.Value);
        }
    }

    #region Head model

    // Parented where the carried boombox sits (Interact.fakeBox), built from this engine's own
    // meshes so it always matches whatever model the prefab has.
    private void BuildHeadModel(ulong clientId)
    {
        NetworkObject player = NetworkManager.Singleton != null
            ? NetworkManager.Singleton.SpawnManager.GetPlayerNetworkObject(clientId)
            : null;
        if (player == null || !player.TryGetComponent(out Interact interact) || interact.fakeBox == null)
        {
            return;
        }

        Transform anchor = interact.fakeBox;
        headModel = new GameObject("CarriedEngine");
        headModel.layer = anchor.gameObject.layer;
        headModel.transform.SetParent(anchor.parent, false);
        headModel.transform.localPosition = anchor.localPosition;
        headModel.transform.localRotation = anchor.localRotation;

        Transform content = new GameObject("Content").transform;
        content.gameObject.layer = anchor.gameObject.layer;
        content.SetParent(headModel.transform, false);

        Bounds bounds = new Bounds();
        bool hasBounds = false;
        Matrix4x4 toRoot = transform.worldToLocalMatrix;

        foreach (MeshFilter filter in GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null || !filter.TryGetComponent(out MeshRenderer source))
            {
                continue;
            }

            Matrix4x4 local = toRoot * filter.transform.localToWorldMatrix;
            GameObject part = new GameObject(filter.name);
            part.layer = anchor.gameObject.layer;
            part.transform.SetParent(content, false);
            part.transform.localPosition = local.GetColumn(3);
            part.transform.localRotation = local.rotation;
            part.transform.localScale = local.lossyScale;
            part.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            MeshRenderer renderer = part.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = source.sharedMaterials;
            renderer.shadowCastingMode = source.shadowCastingMode;

            Bounds partBounds = TransformBounds(local, filter.sharedMesh.bounds);
            if (hasBounds)
            {
                bounds.Encapsulate(partBounds);
            }
            else
            {
                bounds = partBounds;
                hasBounds = true;
            }
        }

        if (!hasBounds)
        {
            return;
        }

        float longest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
        float scale = longest > 0.0001f ? headModelSize / longest : 1f;

        // Sit on the anchor rather than around it: the bottom-centre of the engine goes where the
        // boombox's pivot would be.
        content.localScale = Vector3.one * scale;
        content.localPosition = -new Vector3(bounds.center.x, bounds.min.y, bounds.center.z) * scale;
    }

    private static Bounds TransformBounds(Matrix4x4 matrix, Bounds local)
    {
        Vector3 centre = matrix.MultiplyPoint3x4(local.center);
        Vector3 extents = local.extents;
        Vector3 axisX = matrix.MultiplyVector(new Vector3(extents.x, 0f, 0f));
        Vector3 axisY = matrix.MultiplyVector(new Vector3(0f, extents.y, 0f));
        Vector3 axisZ = matrix.MultiplyVector(new Vector3(0f, 0f, extents.z));
        Vector3 size = new(
            Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x),
            Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y),
            Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z));
        return new Bounds(centre, size * 2f);
    }

    private void DestroyHeadModel()
    {
        if (headModel != null)
        {
            Destroy(headModel);
            headModel = null;
        }
    }

    #endregion

    #region Interaction (client)

    public void Interact(ulong clientId)
    {
        if (!CanInteract)
        {
            return;
        }

        PickUpServerRpc();
    }

    // Called by Interact on the next Interact press while this is latched.
    public void Drop()
    {
        DropServerRpc();
    }

    // The carrier's hint: Interact hides its own prompt while something is held, so this is the
    // only place that tells them the truck is close enough.
    private void Update()
    {
        if (IsServer && IsHeld)
        {
            FollowHolder();
        }

        if (!IsHeld || NetworkManager.Singleton == null || holder.Value != NetworkManager.Singleton.LocalClientId)
        {
            return;
        }

        NetworkObject player = NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject();
        if (player != null && player.TryGetComponent(out Interact interact) && !TeamUp.BlocksInteract &&
            IsNearTruck(player.transform.position))
        {
            InteractionPromptHUD.Show("Install Engine", GetInteractAction(interact));
        }
    }

    private static UnityEngine.InputSystem.InputAction GetInteractAction(Interact interact)
    {
        return interact.GetComponent<InputSystem>().inputActions.FindAction("Interact");
    }

    #endregion

    #region Server

    [ServerRpc(RequireOwnership = false)]
    private void PickUpServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        if (IsHeld || installed.Value || !IsSenderNear(sender, transform.position) ||
            TaskManager.Instance == null || !TaskManager.Instance.IsTaskOpenFor(sender, engineTask))
        {
            return;
        }

        holder.Value = sender;
    }

    [ServerRpc(RequireOwnership = false)]
    private void DropServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        if (holder.Value != sender || !TryGetPlayer(sender, out NetworkObject player))
        {
            return;
        }

        if (IsNearTruck(player.transform.position))
        {
            Install(sender);
            return;
        }

        ReleaseAt(player.transform.position + player.transform.forward * dropForwardOffset + Vector3.up, player.transform.rotation);
    }

    // The real engine rides along with its carrier on the server, so a drop, a death or a
    // disconnect leaves it where the carrier actually was.
    private void FollowHolder()
    {
        if (!TryGetPlayer(holder.Value, out NetworkObject player) ||
            (GameManager.Instance != null && !GameManager.Instance.IsPlayerAlive(holder.Value)))
        {
            Vector3 at = player != null ? player.transform.position + Vector3.up : transform.position;
            ReleaseAt(at, transform.rotation);
            return;
        }

        transform.position = player.transform.position + Vector3.up * 1.5f;
    }

    private void ReleaseAt(Vector3 position, Quaternion rotation)
    {
        holder.Value = ulong.MaxValue;
        Teleport(position, rotation);
        body.isKinematic = false;
        body.linearVelocity = Vector3.zero;
    }

    private void Install(ulong clientId)
    {
        holder.Value = ulong.MaxValue;
        installed.Value = true;

        PushableTruck truck = FindTruck();
        if (truck != null)
        {
            Teleport(truck.transform.position, truck.transform.rotation);
        }

        TaskManager.Instance?.CompleteTaskForPlayer(clientId, engineTask);
    }

    // Handing the task out again puts the engine back where the map placed it, ready to be found.
    private void OnServerTaskAssigned(Challenge task)
    {
        if (!IsServer || !IsSpawned || task != engineTask)
        {
            return;
        }

        installed.Value = false;
        holder.Value = ulong.MaxValue;
        Teleport(homePosition, homeRotation);
        body.isKinematic = false;
        body.linearVelocity = Vector3.zero;
    }

    private void Teleport(Vector3 position, Quaternion rotation)
    {
        if (TryGetComponent(out Unity.Netcode.Components.NetworkTransform networkTransform) && networkTransform.IsSpawned)
        {
            networkTransform.Teleport(position, rotation, transform.localScale);
        }
        else
        {
            transform.SetPositionAndRotation(position, rotation);
        }
    }

    #endregion

    #region Helpers

    private PushableTruck FindTruck()
    {
        if (cachedTruck == null)
        {
            cachedTruck = FindAnyObjectByType<PushableTruck>();
        }

        return cachedTruck;
    }

    // Distance to the truck's hull, not its pivot - the truck is nine metres long.
    private bool IsNearTruck(Vector3 position)
    {
        PushableTruck truck = FindTruck();
        if (truck == null)
        {
            return false;
        }

        float best = float.MaxValue;
        foreach (Collider c in truck.GetComponentsInChildren<Collider>())
        {
            if (c.enabled && !c.isTrigger)
            {
                best = Mathf.Min(best, Vector3.Distance(c.ClosestPoint(position), position));
            }
        }

        return best <= installDistance;
    }

    private bool IsSenderNear(ulong senderId, Vector3 point)
    {
        return TryGetPlayer(senderId, out NetworkObject player) &&
               RpcValidation.IsWithinDistance(player.transform.position, point, MaxInteractDistance);
    }

    private bool TryGetPlayer(ulong clientId, out NetworkObject player)
    {
        player = null;
        return NetworkManager != null && NetworkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client) &&
               (player = client.PlayerObject) != null;
    }

    #endregion
}
