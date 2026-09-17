using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DuckRoulette.Tests.Unit
{
    // Gameplay scripts look actions up by magic string (FindAction("LeaveBlackjack")) against the
    // project-wide Inputs.inputactions. A renamed action silently returns null and the feature goes
    // dead with no error, so every literal used in Assets/Scripts is checked against the asset.
    [TestFixture, Category("Unit")]
    public class InputActionsTests
    {
        private const string ActionsPath = "Assets/Scripts/InputSys/Inputs.inputactions";
        private static readonly Regex FindActionLiteral = new Regex("FindAction\\(\"([^\"]+)\"\\)");

        private readonly List<Object> _clones = new();

        [TearDown]
        public void TearDown()
        {
            foreach (Object clone in _clones) Object.DestroyImmediate(clone);
            _clones.Clear();
        }

        private static InputActionAsset LoadAsset()
        {
            var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(ActionsPath);
            Assert.That(asset, Is.Not.Null, $"Missing {ActionsPath}");
            return asset;
        }

        private InputActionAsset Clone()
        {
            InputActionAsset clone = Object.Instantiate(LoadAsset());
            _clones.Add(clone);
            return clone;
        }

        [Test]
        public void EveryFindActionLiteralInScripts_ResolvesInProjectActions()
        {
            InputActionAsset asset = LoadAsset();
            var missing = new List<string>();
            int checkedCount = 0;

            foreach (string file in Directory.GetFiles("Assets/Scripts", "*.cs", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(file);
                foreach (Match match in FindActionLiteral.Matches(text))
                {
                    checkedCount++;
                    string name = match.Groups[1].Value;
                    if (asset.FindAction(name) == null)
                    {
                        missing.Add($"{file.Replace('\\', '/')}: \"{name}\"");
                    }
                }
            }

            Assert.That(checkedCount, Is.GreaterThan(20), "sanity: expected many FindAction lookups in Assets/Scripts");
            Assert.That(missing.Distinct(), Is.Empty, "FindAction names with no matching action in " + ActionsPath);
        }

        [Test]
        public void BindingOverrides_JsonRoundTrip_RestoresRebind()
        {
            InputActionAsset source = Clone();
            InputAction jump = source.FindAction("Jump");
            Assert.That(jump, Is.Not.Null);
            Assume.That(jump.bindings.Count, Is.GreaterThan(0));
            string original = jump.bindings[0].effectivePath;
            const string rebound = "<Keyboard>/k";
            Assume.That(original, Is.Not.EqualTo(rebound));

            jump.ApplyBindingOverride(0, rebound);
            string json = source.SaveBindingOverridesAsJson();

            InputActionAsset target = Clone();
            target.LoadBindingOverridesFromJson(json);

            Assert.That(target.FindAction("Jump").bindings[0].effectivePath, Is.EqualTo(rebound));
            Assert.That(LoadAsset().FindAction("Jump").bindings[0].effectivePath, Is.EqualTo(original), "test leaked an override into the asset");
        }

        [Test]
        public void BindingOverrides_NoOverrides_JsonLoadsAsNoOp()
        {
            InputActionAsset source = Clone();
            string json = source.SaveBindingOverridesAsJson();

            InputActionAsset target = Clone();
            Assert.DoesNotThrow(() => target.LoadBindingOverridesFromJson(json));
            Assert.That(target.FindAction("Jump").bindings[0].effectivePath, Is.EqualTo(source.FindAction("Jump").bindings[0].effectivePath));
        }

        [Test]
        public void RebindSaveLoad_RumbleWithoutGamepadScheme_IsNoOp()
        {
            var go = new GameObject("RebindSaveLoad (unit test)") { hideFlags = HideFlags.DontSave };
            _clones.Add(go);
            var rebind = go.AddComponent<RebindSaveLoad>();
            rebind.currentControlScheme = "Keyboard&Mouse";

            Assert.DoesNotThrow(() => rebind.RumbleGamepad(1f, 1f, 0f, 0.1f));
            Assert.That(Reflect.Get<Coroutine>(rebind, "_activeRumbleRoutine"), Is.Null);
        }
    }
}
