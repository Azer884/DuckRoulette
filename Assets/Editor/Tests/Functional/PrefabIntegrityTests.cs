using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace DuckRoulette.Tests.Functional
{
    [TestFixture, Category("Functional")]
    public class PrefabIntegrityTests
    {
        private const string NetworkPrefabsListPath = "Assets/DefaultNetworkPrefabs.asset";

        private static IEnumerable<string> ProjectPrefabs() =>
            AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Distinct()
                .OrderBy(p => p);

        private static GameObject Load(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, $"could not load {path}");
            return prefab;
        }

        // Every prefab: no missing scripts, and any NetworkBehaviour sits under a NetworkObject
        // (a UI or cosmetic prefab with no networking at all is fine without one).
        [TestCaseSource(nameof(ProjectPrefabs))]
        public void Prefab_NoMissingScripts_NetworkBehavioursHaveNetworkObject(string path)
        {
            GameObject prefab = Load(path);
            var issues = HierarchyChecks.MissingScripts(prefab);

            // Offline copies (OfflineCharacter for the tutorial/menus) deliberately reuse networked
            // components with no NetworkObject anywhere; they are never spawned. Only prefabs that
            // are actually networked must keep every NetworkBehaviour under a NetworkObject.
            if (prefab.GetComponentInChildren<NetworkObject>(true) != null)
            {
                issues.AddRange(HierarchyChecks.NetworkBehavioursWithoutNetworkObject(prefab));
            }
            Assert.That(issues, Is.Empty, $"{path}:\n  " + string.Join("\n  ", issues));
        }

        [Test]
        public void DefaultNetworkPrefabs_EveryEntryValid_AndHashesUnique()
        {
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsListPath);
            Assert.That(list, Is.Not.Null, $"missing {NetworkPrefabsListPath}");
            Assert.That(list.PrefabList, Is.Not.Empty);

            var issues = new List<string>();
            var hashes = new Dictionary<uint, string>();

            for (int i = 0; i < list.PrefabList.Count; i++)
            {
                GameObject prefab = list.PrefabList[i].Prefab;
                if (prefab == null)
                {
                    issues.Add($"entry {i}: prefab reference is missing");
                    continue;
                }

                string path = AssetDatabase.GetAssetPath(prefab);
                if (!prefab.TryGetComponent(out NetworkObject networkObject))
                {
                    issues.Add($"entry {i} {path}: no NetworkObject on the prefab root");
                }
                else
                {
                    uint hash = new SerializedObject(networkObject).FindProperty("GlobalObjectIdHash").uintValue;
                    if (hash == 0)
                    {
                        issues.Add($"entry {i} {path}: GlobalObjectIdHash is 0");
                    }
                    else if (hashes.TryGetValue(hash, out string other) && other != path)
                    {
                        issues.Add($"entry {i} {path}: GlobalObjectIdHash {hash} collides with {other}");
                    }
                    else
                    {
                        hashes[hash] = path;
                    }
                }

                issues.AddRange(HierarchyChecks.MissingScripts(prefab).Select(m => $"entry {i} {path}: {m}"));
            }

            var duplicates = list.PrefabList.Where(p => p.Prefab != null).GroupBy(p => p.Prefab).Where(g => g.Count() > 1);
            issues.AddRange(duplicates.Select(g => $"{AssetDatabase.GetAssetPath(g.Key)} is registered {g.Count()} times"));

            Assert.That(issues, Is.Empty, string.Join("\n", issues));
        }

        [TestCase("Assets/Prefabs/Player.prefab")]
        [TestCase("Assets/Prefabs/Bullet.prefab")]
        public void GameplayNetworkPrefab_IsRegisteredInDefaultNetworkPrefabs(string path)
        {
            AssertRegistered(Load(path));
        }

        [Test]
        public void BlackjackCardPrefab_SpawnedByServer_IsRegisteredNetworkPrefab()
        {
            CardDeck deck = Load("Assets/Prefabs/Map/BlackjackTable.prefab").GetComponentInChildren<CardDeck>(true);
            Assert.That(deck, Is.Not.Null);
            Assert.That(deck.cardPrefab, Is.Not.Null, "CardDeck.cardPrefab is unassigned; SpawnCard silently deals no card objects");
            AssertRegistered(deck.cardPrefab);
        }

        private static void AssertRegistered(GameObject prefab)
        {
            string path = AssetDatabase.GetAssetPath(prefab);
            Assert.That(prefab.GetComponent<NetworkObject>(), Is.Not.Null, $"{path} is spawned over the network but has no root NetworkObject");
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsListPath);
            Assert.That(list.PrefabList.Any(p => p.Prefab == prefab), Is.True, $"{path} is not in {NetworkPrefabsListPath}; clients cannot instantiate it");
        }

        [Test]
        public void PlayerPrefab_HasCoreMatchComponents()
        {
            GameObject player = Load("Assets/Prefabs/Player.prefab");
            Assert.That(player.GetComponent<NetworkObject>(), Is.Not.Null);
            foreach (System.Type required in new[] { typeof(Shooting), typeof(Death), typeof(Movement) })
            {
                Assert.That(player.GetComponentInChildren(required, true), Is.Not.Null, $"Player.prefab lacks {required.Name}");
            }
            Assert.That(player.GetComponentsInChildren<DeathTrigger>(true), Is.Not.Empty, "Player.prefab has no DeathTrigger hitboxes");
        }

        [TestCase("Assets/Prefabs/Player.prefab")]
        [TestCase("Assets/Prefabs/Character.prefab")]
        public void NetworkCosmetics_ItemArraysHaveNoNullSlots(string path)
        {
            NetworkCosmetics nc = Load(path).GetComponentInChildren<NetworkCosmetics>(true);
            Assume.That(nc, Is.Not.Null, $"{path} has no NetworkCosmetics");
            var so = new SerializedObject(nc);
            foreach (string field in new[] { "hats", "accessories", "shirts" })
            {
                SerializedProperty array = so.FindProperty(field);
                for (int i = 0; i < array.arraySize; i++)
                {
                    Assert.That(array.GetArrayElementAtIndex(i).objectReferenceValue, Is.Not.Null, $"{path} NetworkCosmetics.{field}[{i}] is empty; equipping index {i + 1} would throw");
                }
            }
        }

        [Test]
        public void OfflineCharacterSelector_IndexSpaceMatchesNetworkedPlayer()
        {
            Cosmetics selector = Load("Assets/Prefabs/OfflineCharacter.prefab").GetComponentInChildren<Cosmetics>(true);
            Assume.That(selector, Is.Not.Null);
            AssertCosmeticIndexSpaceMatches(selector, "Assets/Prefabs/Player.prefab");
        }

        // Cosmetics (menu) and NetworkCosmetics (in match) share one cosmeticData.txt of 1-based
        // indices. If the menu offers more items than the player prefab has, a saved index walks off
        // the end of the player's array.
        internal static void AssertCosmeticIndexSpaceMatches(Cosmetics selector, string playerPrefabPath)
        {
            var player = AssetDatabase.LoadAssetAtPath<GameObject>(playerPrefabPath);
            NetworkCosmetics networked = player.GetComponentInChildren<NetworkCosmetics>(true);
            Assert.That(networked, Is.Not.Null, $"{playerPrefabPath} has no NetworkCosmetics");

            var menu = new SerializedObject(selector);
            var match = new SerializedObject(networked);
            var mismatches = new List<string>();
            foreach (string field in new[] { "hats", "accessories", "shirts" })
            {
                int offered = menu.FindProperty(field).arraySize;
                int available = match.FindProperty(field).arraySize;
                if (offered > available)
                {
                    mismatches.Add($"{field}: selector offers {offered}, {playerPrefabPath} has {available}");
                }
            }

            Assert.That(mismatches, Is.Empty, $"{HierarchyChecks.PathOf(selector.transform)}:\n  " + string.Join("\n  ", mismatches));
        }
    }
}
