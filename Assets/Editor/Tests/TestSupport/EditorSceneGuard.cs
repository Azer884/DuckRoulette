using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DuckRoulette.Tests
{
    // Functional tests run inside the user's live editor. They never replace a scene that has unsaved
    // changes, and they put the original scene setup back afterwards.
    internal static class EditorSceneGuard
    {
        public static SceneSetup[] CaptureRestorableSetup()
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Assert.Inconclusive("The editor is already in (or entering) play mode.");
            }

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isDirty)
                {
                    Assert.Inconclusive($"Scene '{(string.IsNullOrEmpty(scene.path) ? scene.name : scene.path)}' has unsaved changes; refusing to replace it. Save it and re-run.");
                }
            }

            return EditorSceneManager.GetSceneManagerSetup();
        }

        public static void Restore(SceneSetup[] setup)
        {
            if (EditorApplication.isPlaying || setup == null || setup.Length == 0)
            {
                return;
            }

            if (setup.Any(s => string.IsNullOrEmpty(s.path)))
            {
                EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                return;
            }

            EditorSceneManager.RestoreSceneManagerSetup(setup);
        }
    }

    internal static class HierarchyChecks
    {
        public static string PathOf(Transform t)
        {
            var sb = new StringBuilder(t.name);
            for (Transform p = t.parent; p != null; p = p.parent)
            {
                sb.Insert(0, p.name + "/");
            }
            return sb.ToString();
        }

        public static List<string> MissingScripts(GameObject root)
        {
            var issues = new List<string>();
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                int missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                if (missing > 0)
                {
                    issues.Add($"{PathOf(t)} ({missing} missing script{(missing == 1 ? "" : "s")})");
                }
            }
            return issues;
        }

        public static List<string> NetworkBehavioursWithoutNetworkObject(GameObject root)
        {
            var issues = new List<string>();
            foreach (NetworkBehaviour nb in root.GetComponentsInChildren<NetworkBehaviour>(true))
            {
                if (nb.GetComponentInParent<NetworkObject>(true) == null)
                {
                    issues.Add($"{PathOf(nb.transform)}: {nb.GetType().Name} has no NetworkObject on itself or a parent");
                }
            }
            return issues;
        }
    }
}
