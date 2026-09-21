using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Weather
{
    /// <summary>
    /// Server-authoritative hail. Runs only while the weather is <see cref="WeatherPhase.Storm"/>,
    /// which GameManager's small second roll decides.
    ///
    /// This is the piece the old WeatherHandler got expensive at: it fired a ServerRpc per spawn
    /// batch, Instantiated 10-20 fresh NetworkObjects a second with no ceiling, and left each of
    /// them to run its own despawn coroutine. Twenty seconds of that is several hundred live
    /// NetworkObjects, each one a spawn message to every client.
    ///
    /// Instead:
    ///   - stones come from a pool through <see cref="INetworkPrefabInstanceHandler"/>, so the
    ///     server and every client reuse the same instances instead of allocating and collecting
    ///     them (this is also why the handler is registered on clients, not just the server);
    ///   - a hard ceiling on concurrent stones, so a long storm costs the same as a short one;
    ///   - one lifetime loop here rather than a coroutine per stone.
    /// </summary>
    [DisallowMultipleComponent]
    public class HailSpawner : NetworkBehaviour, INetworkPrefabInstanceHandler
    {
        [SerializeField, Tooltip("The hailstone prefab. Must also be in the network prefab list.")]
        private GameObject hailPrefab;

        [SerializeField, Tooltip("Stones are dropped from the top face of this volume. Leave it " +
            "covering the playable area.")]
        private BoxCollider spawnArea;

        [Header("Placement")]
        [SerializeField, Tooltip("Drop stones around the players rather than evenly over the " +
            "whole spawn volume. Spread over the full map, a storm's worth of stones lands almost " +
            "entirely where nobody is looking, which read as the hail never happening.")]
        private bool dropAroundPlayers = true;

        [SerializeField, Tooltip("Radius in metres around a player that a stone can land in.")]
        private float dropRadius = 14f;

        [SerializeField, Tooltip("Metres above the player a stone starts from. High enough to be " +
            "seen falling, low enough to be in frame before it lands.")]
        private float dropHeight = 22f;

        [Header("Rate")]
        [SerializeField, Tooltip("Stones per second at the peak of a storm.")]
        private float stonesPerSecond = 14f;

        [SerializeField, Tooltip("Hard ceiling on stones in the air at once. The rate is throttled " +
            "to respect this, so a storm has a fixed worst case cost.")]
        private int maxConcurrentStones = 40;

        [SerializeField, Tooltip("Seconds before an unstruck stone is taken back, whether or not " +
            "it ever landed on anything.")]
        private float stoneLifetime = 6f;

        [Header("Fall")]
        [SerializeField, Tooltip("Downward speed a stone is launched with, before gravity.")]
        private float initialFallSpeed = 12f;

        [SerializeField, Tooltip("How much of the wind velocity is carried into the stone's launch " +
            "velocity, so hail comes in at an angle during a gale.")]
        private float windInfluence = 0.6f;

        [Header("Pool")]
        [SerializeField, Tooltip("Instances created up front so the first seconds of a storm do " +
            "not hitch. Should be at least the concurrent ceiling.")]
        private int prewarmCount = 40;

        private struct ActiveStone
        {
            public Hail Stone;
            public float ExpiresAt;
        }

        private readonly List<ActiveStone> active = new();
        private readonly Stack<NetworkObject> pool = new();
        private NetworkObject hailPrefabNetworkObject;
        private bool handlerRegistered;
        private float spawnAccumulator;

        public override void OnNetworkSpawn()
        {
            if (hailPrefab == null || !hailPrefab.TryGetComponent(out hailPrefabNetworkObject))
            {
                Debug.LogError("HailSpawner: hailPrefab is missing or has no NetworkObject - no hail will fall.");
                enabled = false;
                return;
            }

            RegisterHandler();
            WeatherSystem.PhaseChanged += OnPhaseChanged;

            // Clients too: they are the ones the handler instantiates on when the server spawns a
            // stone, so a client with an empty pool would allocate through the whole first squall.
            Prewarm();
        }

        public override void OnNetworkDespawn()
        {
            WeatherSystem.PhaseChanged -= OnPhaseChanged;

            if (IsServer)
            {
                RecycleAll();
            }

            UnregisterHandler();
        }

        private void OnPhaseChanged(WeatherPhase phase)
        {
            if (!IsServer)
            {
                return;
            }

            if (phase != WeatherPhase.Storm)
            {
                RecycleAll();
                spawnAccumulator = 0f;
                return;
            }

            if (spawnArea == null)
            {
                Debug.LogError("HailSpawner: the storm started but no spawn area is assigned, so " +
                    "no hail can fall. Re-run Tools > Weather > Set Up Game Scene.");
                return;
            }

            Debug.Log($"HailSpawner: storm started, dropping up to {maxConcurrentStones} stones " +
                $"from {spawnArea.bounds.max.y:0}m.");
        }

        private void Update()
        {
            if (!IsServer)
            {
                return;
            }

            float now = Time.time;
            ExpireStones(now);

            WeatherSystem weather = WeatherSystem.Instance;
            if (weather == null || weather.Phase != WeatherPhase.Storm || spawnArea == null)
            {
                return;
            }

            // Ramp with the wind, so the hail is thickest at the height of the gale rather than
            // arriving at a flat rate the moment the storm starts.
            float rate = stonesPerSecond * Mathf.Lerp(0.45f, 1f, weather.WindStrength);
            spawnAccumulator += rate * Time.deltaTime;

            while (spawnAccumulator >= 1f)
            {
                spawnAccumulator -= 1f;

                if (active.Count >= maxConcurrentStones)
                {
                    spawnAccumulator = 0f;
                    break;
                }

                SpawnStone(weather, now);
            }
        }

        private void SpawnStone(WeatherSystem weather, float now)
        {
            Vector3 position = PickDropPoint();

            NetworkObject instance = TakeFromPool(position, Random.rotation);
            if (instance == null)
            {
                return;
            }

            instance.Spawn(false);

            if (!instance.TryGetComponent(out Hail stone))
            {
                instance.Despawn(true);
                return;
            }

            Vector3 velocity = Vector3.down * initialFallSpeed + weather.WindVelocity * windInfluence;
            stone.Launch(this, velocity);

            active.Add(new ActiveStone { Stone = stone, ExpiresAt = now + stoneLifetime });
        }

        // Server-side, so it can only use what the server knows: player object positions. Each
        // stone picks a random connected player and lands somewhere around them - every player
        // sees hail near them, and nothing here trusts a client for where to put it.
        private Vector3 PickDropPoint()
        {
            if (dropAroundPlayers && NetworkManager != null)
            {
                var clients = NetworkManager.ConnectedClientsList;
                if (clients.Count > 0)
                {
                    // A few tries, in case the first client picked has no player object yet.
                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        NetworkObject player = clients[Random.Range(0, clients.Count)].PlayerObject;
                        if (player == null)
                        {
                            continue;
                        }

                        Vector2 offset = Random.insideUnitCircle * dropRadius;
                        Vector3 origin = player.transform.position;
                        return new Vector3(origin.x + offset.x, origin.y + dropHeight, origin.z + offset.y);
                    }
                }
            }

            Bounds bounds = spawnArea.bounds;
            return new Vector3(
                Random.Range(bounds.min.x, bounds.max.x),
                bounds.max.y,
                Random.Range(bounds.min.z, bounds.max.z));
        }

        private void ExpireStones(float now)
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                ActiveStone entry = active[i];
                if (entry.Stone == null)
                {
                    active.RemoveAt(i);
                    continue;
                }

                if (now >= entry.ExpiresAt)
                {
                    active.RemoveAt(i);
                    Despawn(entry.Stone);
                }
            }
        }

        /// <summary>Server only. Called by a stone that has struck a player and is done.</summary>
        public void Recycle(Hail stone)
        {
            if (!IsServer || stone == null)
            {
                return;
            }

            for (int i = active.Count - 1; i >= 0; i--)
            {
                if (active[i].Stone == stone)
                {
                    active.RemoveAt(i);
                    break;
                }
            }

            Despawn(stone);
        }

        private void RecycleAll()
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                if (active[i].Stone != null)
                {
                    Despawn(active[i].Stone);
                }
            }

            active.Clear();
        }

        private static void Despawn(Hail stone)
        {
            if (stone.TryGetComponent(out NetworkObject netObj) && netObj.IsSpawned)
            {
                // destroy: true routes through the prefab handler below, which parks the instance
                // back in the pool rather than actually destroying it.
                netObj.Despawn(true);
            }
        }

        #region Pooling

        private void RegisterHandler()
        {
            if (handlerRegistered || NetworkManager == null || hailPrefabNetworkObject == null)
            {
                return;
            }

            NetworkManager.PrefabHandler.AddHandler(hailPrefabNetworkObject, this);
            handlerRegistered = true;
        }

        private void UnregisterHandler()
        {
            if (!handlerRegistered || NetworkManager == null || hailPrefabNetworkObject == null)
            {
                return;
            }

            NetworkManager.PrefabHandler.RemoveHandler(hailPrefabNetworkObject);
            handlerRegistered = false;

            while (pool.Count > 0)
            {
                NetworkObject instance = pool.Pop();
                if (instance != null)
                {
                    Destroy(instance.gameObject);
                }
            }
        }

        private void Prewarm()
        {
            for (int i = 0; i < prewarmCount; i++)
            {
                NetworkObject instance = CreateInstance(Vector3.zero, Quaternion.identity);
                instance.gameObject.SetActive(false);
                pool.Push(instance);
            }
        }

        private NetworkObject TakeFromPool(Vector3 position, Quaternion rotation)
        {
            NetworkObject instance = null;
            while (pool.Count > 0 && instance == null)
            {
                instance = pool.Pop();
            }

            if (instance == null)
            {
                instance = CreateInstance(position, rotation);
            }
            else
            {
                instance.transform.SetPositionAndRotation(position, rotation);
                instance.gameObject.SetActive(true);
            }

            return instance;
        }

        private NetworkObject CreateInstance(Vector3 position, Quaternion rotation)
        {
            GameObject go = Instantiate(hailPrefab, position, rotation);
            go.name = "Hailstone";
            return go.GetComponent<NetworkObject>();
        }

        // --- INetworkPrefabInstanceHandler: Netcode routes every spawn/destroy of the hail prefab,
        // on the server and on every client, through these two. ---

        NetworkObject INetworkPrefabInstanceHandler.Instantiate(ulong ownerClientId, Vector3 position, Quaternion rotation)
        {
            return TakeFromPool(position, rotation);
        }

        void INetworkPrefabInstanceHandler.Destroy(NetworkObject networkObject)
        {
            if (networkObject == null)
            {
                return;
            }

            if (networkObject.TryGetComponent(out Hail stone))
            {
                stone.ResetForPool();
            }

            networkObject.gameObject.SetActive(false);
            pool.Push(networkObject);
        }

        #endregion
    }
}
