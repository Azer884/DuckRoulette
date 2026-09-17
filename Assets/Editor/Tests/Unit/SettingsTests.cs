using System.Globalization;
using System.IO;
using System.Threading;
using IniParser;
using IniParser.Model;
using NUnit.Framework;
using UnityEngine;

namespace DuckRoulette.Tests.Unit
{
    // SettingsManager is driven with a private temp Settings.ini; the real one under
    // persistentDataPath is never read or written. Nothing that pushes to Screen/QualitySettings/the
    // AudioMixer (LoadSettings, Apply*) is called, so the editor's own state is left alone.
    [TestFixture, Category("Unit")]
    public class SettingsManagerTests
    {
        private GameObject _go;
        private SettingsManager _settings;
        private string _tempDir;
        private string _iniPath;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "DuckRouletteTests_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _iniPath = Path.Combine(_tempDir, "Settings.ini");

            _go = new GameObject("SettingsManager (unit test)") { hideFlags = HideFlags.DontSave };
            _settings = _go.AddComponent<SettingsManager>();
            Reflect.Set(_settings, "_settingsFilePath", _iniPath);
            Reflect.Set(_settings, "_parser", new FileIniDataParser());
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
        }

        private float GetFloat(string section, string key, float fallback) => (float)Reflect.Call(_settings, "GetFloatSetting", section, key, fallback);
        private int GetInt(string section, string key, int fallback) => (int)Reflect.Call(_settings, "GetIntSetting", section, key, fallback);
        private bool IsTrue(string value) => (bool)Reflect.Call(_settings, "IsTrueSetting", value);

        [Test]
        public void GetSetting_NoDataLoaded_ReturnsDefault()
        {
            Assert.That(_settings.GetSetting("Audio", "MasterVolume", "fallback"), Is.EqualTo("fallback"));
        }

        [Test]
        public void SetSetting_CreatesSectionAndRoundTrips()
        {
            _settings.SetSetting("Brand New", "Key", "Value");
            Assert.That(_settings.GetSetting("Brand New", "Key"), Is.EqualTo("Value"));
            Assert.That(_settings.GetSetting("Brand New", "Missing", "d"), Is.EqualTo("d"));
            Assert.That(_settings.GetSetting("Other", "Key", "d"), Is.EqualTo("d"));
        }

        [TestCase("0.5", 0.5f)]
        [TestCase("1e-2", 0.01f)]
        [TestCase("abc", 0.8f)]
        [TestCase("", 0.8f)]
        [TestCase("1,5", 0.8f)]
        public void GetFloatSetting_ParsesInvariantCulture_FallsBackOnGarbage(string raw, float expected)
        {
            _settings.SetSetting("Audio", "MusicVolume", raw);
            Assert.That(GetFloat("Audio", "MusicVolume", 0.8f), Is.EqualTo(expected).Within(1e-6f));
        }

        [Test]
        public void GetFloatSetting_UnaffectedByCommaDecimalLocale()
        {
            CultureInfo previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                _settings.SetSetting("Mouse", "SensitivityX", "2.25");
                Assert.That(GetFloat("Mouse", "SensitivityX", 1f), Is.EqualTo(2.25f));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        [TestCase("7", 7)]
        [TestCase("-3", -3)]
        [TestCase("7.5", 2)]
        [TestCase("seven", 2)]
        public void GetIntSetting_ParsesIntegersOnly(string raw, int expected)
        {
            _settings.SetSetting("Graphics", "QualityLevel", raw);
            Assert.That(GetInt("Graphics", "QualityLevel", 2), Is.EqualTo(expected));
        }

        [TestCase("true", true)]
        [TestCase("TRUE", true)]
        [TestCase("1", true)]
        [TestCase("false", false)]
        [TestCase("0", false)]
        [TestCase("yes", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void IsTrueSetting_AcceptsTrueOrOne(string raw, bool expected)
        {
            Assert.That(IsTrue(raw), Is.EqualTo(expected));
        }

        [Test]
        public void DefaultSettings_CoverEveryKeyGameplaySettingsReads()
        {
            Reflect.Call(_settings, "SetDefaultSettings");

            string[] gameKeys = { "FieldOfView", "CameraShake", "InvertLookY", "ToggleCrouch", "ToggleSprint", "ShowTaskList" };
            string[] accessibilityKeys = { "ReduceMotion", "FlashingEffects", "ScreenEffects", "UIScale" };
            foreach (string key in gameKeys)
                Assert.That(_settings.GetSetting(GameplaySettings.GameSection, key, null), Is.Not.Null, $"Game.{key} missing from defaults");
            foreach (string key in accessibilityKeys)
                Assert.That(_settings.GetSetting(GameplaySettings.AccessibilitySection, key, null), Is.Not.Null, $"Accessibility.{key} missing from defaults");

            Assert.That(GetFloat("Audio", "MasterVolume", -1f), Is.EqualTo(1f));
            Assert.That(GetFloat("Audio", "MusicVolume", -1f), Is.EqualTo(0.8f));
            Assert.That(GetFloat("Mouse", "SensitivityX", -1f), Is.EqualTo(1f));
            Assert.That(IsTrue(_settings.GetSetting("Graphics", "Fullscreen")), Is.True);
            Assert.That(float.Parse(_settings.GetSetting("Game", "FieldOfView"), CultureInfo.InvariantCulture),
                Is.InRange(GameplaySettings.MinFieldOfView, GameplaySettings.MaxFieldOfView));
        }

        [Test]
        public void WriteSettingsToDisk_IniRoundTrip()
        {
            _settings.SetSetting("Audio", "MasterVolume", "0.35");
            _settings.SetSetting("Game", "InvertLookY", "true");
            _settings.SetSetting("Key Bindings", "Weird = Key", "a;b#c");

            Reflect.Call(_settings, "WriteSettingsToDisk");

            Assert.That(File.Exists(_iniPath), Is.True);
            IniData reread = new FileIniDataParser().ReadFile(_iniPath);
            Assert.That(reread["Audio"]["MasterVolume"], Is.EqualTo("0.35"));
            Assert.That(reread["Game"]["InvertLookY"], Is.EqualTo("true"));
        }

        [Test]
        public void PersistSetting_OnInactiveObject_WritesImmediately()
        {
            _go.SetActive(false);
            _settings.PersistSetting("Accessibility", "UIScale", "1.25");

            Assert.That(File.Exists(_iniPath), Is.True, "inactive SettingsManager cannot run the deferred-save coroutine, so it must write synchronously");
            Assert.That(new FileIniDataParser().ReadFile(_iniPath)["Accessibility"]["UIScale"], Is.EqualTo("1.25"));
        }
    }

    [TestFixture, Category("Unit")]
    public class GameplaySettingsTests
    {
        private GameObject _go;
        private SettingsManager _settings;
        private SettingsManager _previousInstance;

        [SetUp]
        public void SetUp()
        {
            _previousInstance = SettingsManager.Instance;
            _go = new GameObject("SettingsManager (gameplay test)") { hideFlags = HideFlags.DontSave };
            _settings = _go.AddComponent<SettingsManager>();
            Reflect.Set(typeof(SettingsManager), "Instance", _settings);
            GameplaySettings.Invalidate();
        }

        [TearDown]
        public void TearDown()
        {
            Reflect.Set(typeof(SettingsManager), "Instance", _previousInstance);
            GameplaySettings.Invalidate();
            Object.DestroyImmediate(_go);
        }

        private void Write(string section, string key, string value)
        {
            _settings.SetSetting(section, key, value);
            GameplaySettings.Invalidate();
        }

        [TestCase("90", 90f)]
        [TestCase("30", 60f)]
        [TestCase("500", 110f)]
        [TestCase("not a number", 60f)]
        public void FieldOfView_ClampedToSupportedRange(string raw, float expected)
        {
            Write("Game", "FieldOfView", raw);
            Assert.That(GameplaySettings.FieldOfView, Is.EqualTo(expected));
        }

        [TestCase("1.2", 1.2f)]
        [TestCase("0.1", 0.75f)]
        [TestCase("9", 1.5f)]
        public void UIScale_ClampedBetweenThreeQuartersAndOneAndHalf(string raw, float expected)
        {
            Write("Accessibility", "UIScale", raw);
            Assert.That(GameplaySettings.UIScale, Is.EqualTo(expected).Within(1e-5f));
        }

        [TestCase("0.4", 0.4f)]
        [TestCase("2", 1f)]
        [TestCase("-1", 0f)]
        public void CameraShakeScale_Clamped01(string raw, float expected)
        {
            Write("Game", "CameraShake", raw);
            Assert.That(GameplaySettings.CameraShakeScale, Is.EqualTo(expected).Within(1e-5f));
        }

        [Test]
        public void CameraShakeScale_ReduceMotionOverridesSlider()
        {
            _settings.SetSetting("Game", "CameraShake", "1");
            Write("Accessibility", "ReduceMotion", "true");
            Assert.That(GameplaySettings.CameraShakeScale, Is.Zero);
            Assert.That(GameplaySettings.ReduceMotion, Is.True);
        }

        [TestCase("0.3", 0.3f)]
        [TestCase("7", 1f)]
        [TestCase("-2", 0f)]
        public void ScreenEffectScale_Clamped01(string raw, float expected)
        {
            Write("Accessibility", "ScreenEffects", raw);
            Assert.That(GameplaySettings.ScreenEffectScale, Is.EqualTo(expected).Within(1e-5f));
        }

        [Test]
        public void Booleans_DefaultsWhenUnsetOrUnparseable()
        {
            Assert.That(GameplaySettings.InvertLookY, Is.False);
            Assert.That(GameplaySettings.ShowTaskList, Is.True);
            Assert.That(GameplaySettings.FlashingEffects, Is.True);

            Write("Game", "ShowTaskList", "False");
            Assert.That(GameplaySettings.ShowTaskList, Is.False);

            Write("Game", "InvertLookY", "maybe");
            Assert.That(GameplaySettings.InvertLookY, Is.False);
        }

        [Test]
        public void Cache_IsServedUntilInvalidated()
        {
            Write("Game", "FieldOfView", "80");
            Assert.That(GameplaySettings.FieldOfView, Is.EqualTo(80f));

            _settings.SetSetting("Game", "FieldOfView", "100");
            Assert.That(GameplaySettings.FieldOfView, Is.EqualTo(80f), "value re-parsed without Invalidate");

            GameplaySettings.Invalidate();
            Assert.That(GameplaySettings.FieldOfView, Is.EqualTo(100f));
        }

        [Test]
        public void NoSettingsManager_UsesDefaults_AndDoesNotCacheThem()
        {
            Reflect.Set(typeof(SettingsManager), "Instance", null);
            GameplaySettings.Invalidate();
            Assert.That(GameplaySettings.FieldOfView, Is.EqualTo(GameplaySettings.DefaultFieldOfView));
            Assert.That(GameplaySettings.UIScale, Is.EqualTo(1f));

            // The real file must win as soon as a manager appears, without anyone invalidating.
            _settings.SetSetting("Game", "FieldOfView", "95");
            Reflect.Set(typeof(SettingsManager), "Instance", _settings);
            Assert.That(GameplaySettings.FieldOfView, Is.EqualTo(95f));
        }
    }
}
