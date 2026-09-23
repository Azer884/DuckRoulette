using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace DuckRoulette.MapGen
{
    /// <summary>
    /// One place to instantiate generated content. In the editor it keeps the prefab link, so a
    /// generated map can be inspected, tweaked by hand and saved into the scene like normal level
    /// geometry; at runtime it is a plain Instantiate.
    /// </summary>
    public static class MapSpawner
    {
        public static GameObject Spawn(GameObject prefab, Transform parent)
        {
    #if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                var instance = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, parent);
                if (instance != null)
                {
                    return instance;
                }
            }
    #endif
            return Object.Instantiate(prefab, parent);
        }

        // Props this peer spawned as the server, so a regenerate can take them back.
        static readonly List<NetworkObject> networkProps = new List<NetworkObject>();

        /// <summary>True when the instance is a networked prop (it carries a NetworkObject).</summary>
        public static bool IsNetworked(GameObject instance)
        {
            return instance != null && instance.GetComponent<NetworkObject>() != null;
        }

        /// <summary>
        /// Finishes a networked prop once it has been placed and scaled.
        ///
        /// Every peer builds the map itself, but a prop that moves (the truck) has to be one shared
        /// object: the server unparents and spawns its copy, which Netcode then replicates, and a
        /// client throws its local copy away. Offline (no session running) the instance is kept as a
        /// plain local prop. In the editor the network components are stripped from the preview
        /// instance, so the saved scene never holds an in-scene placed NetworkObject that play mode
        /// would then wipe and regenerate.
        /// </summary>
        public static void HandOffToNetwork(GameObject instance)
        {
            if (!instance.TryGetComponent(out NetworkObject networkObject))
            {
                return;
            }

    #if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                StripNetworkComponents(instance);
                return;
            }
    #endif

            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsListening)
            {
                return;
            }

            if (!manager.IsServer)
            {
                // Immediately, not at the end of the frame: the server's copy can arrive in this
                // same frame, and props with singletons (the blackjack deck) would otherwise see
                // this doomed copy as the live one and discard the real one.
                Object.DestroyImmediate(instance);
                return;
            }

            // Netcode only parents a NetworkObject under another NetworkObject, and the generated
            // hierarchy is plain GameObjects. The world pose (and the map's scale) is kept.
            instance.transform.SetParent(null, true);
            networkObject.Spawn(true);
            networkProps.Add(networkObject);
        }

        /// <summary>Server: despawns the networked props from the previous generate.</summary>
        public static void DespawnNetworkProps()
        {
            foreach (NetworkObject networkObject in networkProps)
            {
                if (networkObject != null && networkObject.IsSpawned)
                {
                    networkObject.Despawn(true);
                }
            }

            networkProps.Clear();
        }

    #if UNITY_EDITOR
        static void StripNetworkComponents(GameObject instance)
        {
            // Behaviours first, newest first: a later component can require an earlier one
            // (NetworkRigidbody requires NetworkTransform), and Unity refuses to remove a
            // component something else still depends on.
            NetworkBehaviour[] behaviours = instance.GetComponentsInChildren<NetworkBehaviour>(true);
            for (int i = behaviours.Length - 1; i >= 0; i--)
            {
                Object.DestroyImmediate(behaviours[i]);
            }

            foreach (NetworkObject networkObject in instance.GetComponentsInChildren<NetworkObject>(true))
            {
                Object.DestroyImmediate(networkObject);
            }

            // The edit-time copy is a preview; keep it from falling while the scene is edited.
            foreach (Rigidbody body in instance.GetComponentsInChildren<Rigidbody>(true))
            {
                body.isKinematic = true;
            }
        }
    #endif

        /// <summary>Removes every child of a transform, in editor or at runtime.</summary>
        public static void ClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                GameObject child = parent.GetChild(i).gameObject;
    #if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    Object.DestroyImmediate(child);
                    continue;
                }
    #endif
                Object.Destroy(child);
            }
        }
    }
}
