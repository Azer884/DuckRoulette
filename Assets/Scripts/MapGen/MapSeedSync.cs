using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace DuckRoulette.MapGen
{
    /// <summary>
    /// Makes every peer build the same procedural map.
    ///
    /// MapGenerator used to generate in Start on every peer, and with Randomise Seed On Start each
    /// peer rolled its own seed, so every player walked a different map. Now the server picks the
    /// seed (rolling a new one if the generator asks for it), publishes it in a NetworkVariable and
    /// generates; each client generates from that seed as soon as it arrives, including late
    /// joiners, which get it in the spawn payload. Changing the seed later (between rounds, through
    /// <see cref="ServerRegenerate"/>) rebuilds the map everywhere.
    ///
    /// Generation is deterministic from the seed, so only the seed crosses the network, not the map.
    /// Networked props the map places (the truck) are spawned by the server only; see
    /// <see cref="MapSpawner.HandOffToNetwork"/>.
    ///
    /// Outside a session (offline tutorial, a scene opened without the lobby) this does nothing and
    /// MapGenerator generates in Start as before.
    /// </summary>
    [RequireComponent(typeof(MapGenerator))]
    [DisallowMultipleComponent]
    public class MapSeedSync : NetworkBehaviour
    {
        private readonly NetworkVariable<FixedString64Bytes> seed = new(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private MapGenerator generator;
        private string generatedSeed;

        private void Awake()
        {
            generator = GetComponent<MapGenerator>();
        }

        /// <summary>True when a session is running and this component will do the generating, so
        /// MapGenerator must not generate on its own.</summary>
        public static bool DrivesGeneration(MapGenerator generator)
        {
            NetworkManager manager = NetworkManager.Singleton;
            return manager != null && manager.IsListening &&
                   generator.TryGetComponent(out MapSeedSync sync) && sync.isActiveAndEnabled;
        }

        public override void OnNetworkSpawn()
        {
            seed.OnValueChanged += OnSeedChanged;

            if (IsServer)
            {
                if (generator.generateOnStart)
                {
                    StartCoroutine(RegenerateAfterSceneEvent());
                }

                return;
            }

            GenerateFrom(seed.Value.ToString());
        }

        // This is an in-scene placed object, so on the server OnNetworkSpawn runs while Netcode is
        // still inside the scene load: the Load scene event has not been sent to the clients yet.
        // Generating here spawned the map's networked props (truck, blackjack table) before that
        // event, so each client created them in the scene it was leaving (LoadingScreen) and the
        // Single-mode load then destroyed them - the props never showed up for anyone but the host.
        // One frame later the Load event is already queued ahead of the spawn messages, so clients
        // defer the props until the map scene has loaded, the same as any other spawn during a load.
        private System.Collections.IEnumerator RegenerateAfterSceneEvent()
        {
            yield return null;

            if (IsSpawned && IsServer)
            {
                ServerRegenerate(generator.randomiseSeedOnStart);
            }
        }

        public override void OnNetworkDespawn()
        {
            StopAllCoroutines();
            seed.OnValueChanged -= OnSeedChanged;
        }

        /// <summary>Server only. Publishes a seed (a new one when <paramref name="reroll"/>) and
        /// rebuilds the map on every peer.</summary>
        public void ServerRegenerate(bool reroll)
        {
            if (!IsServer)
            {
                return;
            }

            string next = reroll ? generator.RerollSeed() : generator.noiseSettings.seed;
            if (string.IsNullOrEmpty(next))
            {
                next = generator.RerollSeed();
            }

            seed.Value = new FixedString64Bytes(next);
            GenerateFrom(next);
        }

        private void OnSeedChanged(FixedString64Bytes previous, FixedString64Bytes current)
        {
            if (!IsServer)
            {
                GenerateFrom(current.ToString());
            }
        }

        private void GenerateFrom(string value)
        {
            // Empty until the server has published one; the change callback picks it up.
            if (string.IsNullOrEmpty(value) || value == generatedSeed)
            {
                return;
            }

            generatedSeed = value;
            generator.noiseSettings.seed = value;
            generator.Generate();
            Debug.Log($"MapSeedSync: built map from {(IsServer ? "own" : "server")} seed '{value}'.");
        }
    }
}
