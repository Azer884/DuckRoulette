using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace DuckRoulette.Tests.Functional
{
    // Enters play mode directly in Tutorial.unity (offline, no Steam, no NetworkManager session) and
    // fails on any error/exception/assert logged during the first seconds. Errors are captured with
    // Application.logMessageReceivedThreaded and reported together, rather than through LogAssert
    // which would stop at the first one.
    [TestFixture, Category("Functional")]
    public class TutorialPlayModeTests
    {
        private const string TutorialPath = "Assets/Scenes/Tutorial.unity";
        private const double ObserveSeconds = 5.0;

        private SceneSetup[] _setup;
        private readonly List<string> _captured = new();

        private void Capture(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                lock (_captured)
                {
                    _captured.Add($"[{type}] {condition}\n{FirstLines(stackTrace, 4)}");
                }
            }
        }

        private static string FirstLines(string text, int count) =>
            string.Join("\n", (text ?? string.Empty).Split('\n').Take(count));

        // Only noise that is unavoidable when booting the tutorial on its own belongs here.
        private static bool IsKnownNoise(string entry) => false;

        [UnityTest]
        public IEnumerator Tutorial_FirstSeconds_LogNoErrorsOrExceptions()
        {
            _setup = EditorSceneGuard.CaptureRestorableSetup();
            EditorSceneManager.OpenScene(TutorialPath, OpenSceneMode.Single);

            _captured.Clear();
            Application.logMessageReceivedThreaded += Capture;
            LogAssert.ignoreFailingMessages = true;

            yield return new EnterPlayMode();

            double start = EditorApplication.timeSinceStartup;
            int frames = 0;
            while (EditorApplication.timeSinceStartup - start < ObserveSeconds)
            {
                frames++;
                yield return null;
            }

            Assert.That(EditorApplication.isPlaying, Is.True, "play mode ended on its own");
            yield return new ExitPlayMode();

            Application.logMessageReceivedThreaded -= Capture;

            List<string> errors;
            lock (_captured)
            {
                errors = _captured.Where(e => !IsKnownNoise(e)).Distinct().ToList();
            }

            Assert.That(frames, Is.GreaterThan(10), "play mode barely ticked");
            Assert.That(errors, Is.Empty, $"{errors.Count} error(s) in the first {ObserveSeconds}s of {TutorialPath}:\n\n" + string.Join("\n\n", errors));
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Application.logMessageReceivedThreaded -= Capture;
            if (EditorApplication.isPlaying)
            {
                yield return new ExitPlayMode();
            }
            EditorSceneGuard.Restore(_setup);
            _setup = null;
        }
    }
}
