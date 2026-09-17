using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace DuckRoulette.Tests
{
    // A real Netcode host in an empty play-mode scene: UnityTransport on loopback instead of
    // FacepunchTransport (no Steam), and a runtime-built GameManager + RoundManager + TaskManager
    // object spawned as a server-owned NetworkObject. Only one real connection exists (the host,
    // client 0). Multi-player selection is exercised by temporarily adding fake ids to the server's
    // connected-client list inside one synchronous block (no frame passes, so nothing is ever sent
    // to them).
    public abstract class HostSessionFixture
    {
        protected const ushort Port = 47311;
        protected const float RoundSeconds = 1.5f;
        protected const ulong Host = NetworkManager.ServerClientId;

        private SceneSetup[] _setup;
        private GameObject _nmGo, _gmGo;
        protected NetworkManager Nm;
        protected GameManager Gm;
        protected RoundManager Rm;
        protected TaskManager Tm;

        [UnitySetUp]
        public IEnumerator HostSetUp()
        {
            _setup = EditorSceneGuard.CaptureRestorableSetup();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            yield return new EnterPlayMode();

            _nmGo = new GameObject("NetworkManager (host test)");
            var transport = _nmGo.AddComponent<UnityTransport>();
            transport.SetConnectionData("127.0.0.1", Port);
            Nm = _nmGo.AddComponent<NetworkManager>();
            Nm.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                EnableSceneManagement = false,
            };

            Assert.That(Nm.StartHost(), Is.True, "StartHost failed");
            Assert.That(NetworkManager.Singleton, Is.SameAs(Nm));

            _gmGo = new GameObject("GameManager (host test)");
            _gmGo.SetActive(false);
            var networkObject = _gmGo.AddComponent<NetworkObject>();
            Rm = _gmGo.AddComponent<RoundManager>();
            Reflect.Set(Rm, "roundDuration", RoundSeconds);
            Tm = _gmGo.AddComponent<TaskManager>();
            Gm = _gmGo.AddComponent<GameManager>();
            _gmGo.SetActive(true);

            Assert.That(GameManager.Instance, Is.SameAs(Gm), "a stale GameManager.Instance blocked Awake");
            networkObject.Spawn();

            yield return null;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator HostTearDown()
        {
            if (Gm != null)
            {
                // Otherwise GameManager.OnDisable -> LeaveGame loads the Lobby scene and touches
                // PlayerSpawner.Instance, which this minimal scene doesn't have.
                Reflect.Set(Gm, "_isLeavingGame", true);
            }

            if (_gmGo != null)
            {
                var networkObject = _gmGo.GetComponent<NetworkObject>();
                if (networkObject != null && networkObject.IsSpawned)
                {
                    networkObject.Despawn(true);
                }
                else
                {
                    Object.Destroy(_gmGo);
                }
                yield return null;
            }

            if (Nm != null)
            {
                Nm.Shutdown();
                yield return null;
                Object.Destroy(_nmGo);
                yield return null;
            }

            if (EditorApplication.isPlaying)
            {
                yield return new ExitPlayMode();
            }

            EditorSceneGuard.Restore(_setup);
            _setup = null;
        }

        protected List<ulong> ServerConnectedIds => Reflect.Get<List<ulong>>(Reflect.Get<object>(Nm, "ConnectionManager"), "ConnectedClientIds");
        protected Dictionary<ulong, bool> PlayerStates => Reflect.Get<Dictionary<ulong, bool>>(Gm, "_playerStates");
        protected ulong RandomClientExcluding(ulong excluded) => (ulong)Reflect.Call(Gm, "GetRandomClientId", excluded);

        protected HashSet<ulong> SampleWithFakeClients(IDictionary<ulong, bool> fakeAliveStates, ulong excluded, int samples = 300)
        {
            List<ulong> ids = ServerConnectedIds;
            var seen = new HashSet<ulong>();
            try
            {
                foreach (var kv in fakeAliveStates)
                {
                    ids.Add(kv.Key);
                    PlayerStates[kv.Key] = kv.Value;
                }

                for (int i = 0; i < samples; i++)
                {
                    seen.Add(RandomClientExcluding(excluded));
                }
            }
            finally
            {
                foreach (ulong id in fakeAliveStates.Keys)
                {
                    ids.Remove(id);
                    PlayerStates.Remove(id);
                }
            }
            return seen;
        }

        protected static IEnumerator Frames(int count)
        {
            for (int i = 0; i < count; i++) yield return null;
        }
    }
}
