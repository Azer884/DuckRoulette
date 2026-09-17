using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DuckRoulette.Tests.Functional
{
    // Build scenes are opened ADDITIVELY and closed again, so whatever the user has open is untouched.
    [TestFixture, Category("Functional")]
    public class SceneIntegrityTests
    {
        private static readonly string[] ExpectedBuildScenes =
        {
            "Assets/Scenes/Loading.unity",
            "Assets/Scenes/Lobby.unity",
            "Assets/Scenes/GameScene.unity",
            "Assets/Scenes/Error.unity",
            "Assets/Scenes/Tutorial.unity",
            "Assets/Scenes/LoadingScreen.unity",
        };

        private static IEnumerable<string> EnabledBuildScenes() =>
            EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path);

        [Test]
        public void BuildSettings_ContainExpectedScenes_LoadingFirst()
        {
            string[] enabled = EnabledBuildScenes().ToArray();
            Assert.That(enabled, Is.SupersetOf(ExpectedBuildScenes));
            Assert.That(enabled.First(), Is.EqualTo("Assets/Scenes/Loading.unity"), "scene 0 is the boot scene");
            foreach (string path in enabled)
            {
                Assert.That(File.Exists(path), Is.True, $"build scene missing on disk: {path}");
            }
        }

        [TestCaseSource(nameof(EnabledBuildScenes))]
        public void BuildScene_OpensWithNoMissingScriptsOrPrefabs(string scenePath)
        {
            Scene alreadyOpen = SceneManager.GetSceneByPath(scenePath);
            bool openedHere = !alreadyOpen.isLoaded;
            Scene scene = openedHere ? EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive) : alreadyOpen;

            try
            {
                Assert.That(scene.IsValid() && scene.isLoaded, Is.True, $"could not open {scenePath}");

                var issues = new List<string>();
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    issues.AddRange(HierarchyChecks.MissingScripts(root));

                    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    {
                        if (PrefabUtility.IsPrefabAssetMissing(t.gameObject) && PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject))
                        {
                            issues.Add($"{HierarchyChecks.PathOf(t)} (missing prefab asset)");
                        }
                    }
                }

                Assert.That(issues, Is.Empty, $"{scenePath}:\n  " + string.Join("\n  ", issues));
            }
            finally
            {
                if (openedHere && scene.IsValid())
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        [Test]
        public void LobbyCosmeticsSelector_IndexSpaceMatchesNetworkedPlayer()
        {
            const string lobbyPath = "Assets/Scenes/Lobby.unity";
            Scene alreadyOpen = SceneManager.GetSceneByPath(lobbyPath);
            bool openedHere = !alreadyOpen.isLoaded;
            Scene scene = openedHere ? EditorSceneManager.OpenScene(lobbyPath, OpenSceneMode.Additive) : alreadyOpen;

            try
            {
                Cosmetics[] selectors = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Cosmetics>(true)).ToArray();
                Assume.That(selectors, Is.Not.Empty, "Lobby has no Cosmetics selector");

                foreach (Cosmetics selector in selectors)
                {
                    PrefabIntegrityTests.AssertCosmeticIndexSpaceMatches(selector, "Assets/Prefabs/Player.prefab");
                }
            }
            finally
            {
                if (openedHere && scene.IsValid())
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }
    }
}
