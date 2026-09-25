using NUnit.Framework;
using Steamworks;
using UnityEngine;
using UnityEngine.TestTools;

namespace DuckRoulette.Tests.Unit
{
    // Coin/CoinData/SaveSystem without Steam. SteamClient.IsValid is only read (a managed flag);
    // no Steamworks native call is made, and every test that would reach Steam Cloud is skipped
    // when a Steam session happens to be live in the editor so a test never overwrites a real save.
    [TestFixture, Category("Unit")]
    public class CoinAndSaveTests
    {
        private GameObject _go;
        private Coin _coin;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("Coin (unit test)") { hideFlags = HideFlags.DontSave };
            _coin = _go.AddComponent<Coin>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
        }

        [Test]
        public void DefaultAmount_Is100()
        {
            Assert.That(_coin.amount, Is.EqualTo(100));
        }

        [Test]
        public void UpdateCoinAmount_AddsAndSubtracts_RaisingChangedEachTime()
        {
            int raised = 0;
            Coin.OnCoinChanged handler = () => raised++;
            Coin.CoinChanged += handler;
            try
            {
                _coin.UpdateCoinAmount(25);
                Assert.That(_coin.amount, Is.EqualTo(125));
                _coin.UpdateCoinAmount(-30);
                Assert.That(_coin.amount, Is.EqualTo(95));
                Assert.That(raised, Is.EqualTo(2));
            }
            finally
            {
                Coin.CoinChanged -= handler;
            }
        }

        [Test]
        public void CoinData_CopiesAmount()
        {
            _coin.amount = 731;
            Assert.That(new CoinData(_coin).coinAmount, Is.EqualTo(731));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(int.MaxValue)]
        [TestCase(-5)]
        public void CoinData_JsonRoundTrip_PreservesAmount(int amount)
        {
            _coin.amount = amount;
            string json = JsonUtility.ToJson(new CoinData(_coin));
            CoinData back = JsonUtility.FromJson<CoinData>(json);
            Assert.That(back.coinAmount, Is.EqualTo(amount));
        }

        [Test]
        public void CoinData_SaveFormatIsStable()
        {
            // Coin.Value files already in players' Steam Cloud use exactly this shape; renaming the
            // field would silently reset everyone to the default.
            _coin.amount = 42;
            StringAssert.StartsWith("{\"coinAmount\":42", JsonUtility.ToJson(new CoinData(_coin)));
            Assert.That(JsonUtility.FromJson<CoinData>("{\"coinAmount\":1337}").coinAmount, Is.EqualTo(1337));
        }

        [Test]
        public void Verify_AcceptsCorrectlySignedSave()
        {
            var data = new CoinData(_coin) { coinAmount = 250, steamId = 76561198000000001UL };
            data.signature = SaveSystem.Sign(250, data.steamId);
            Assert.That(SaveSystem.Verify(data, data.steamId), Is.True);
            Assert.That(data.coinAmount, Is.EqualTo(250));
        }

        [Test]
        public void Verify_RejectsEditedAmount()
        {
            var data = new CoinData(_coin) { coinAmount = 250, steamId = 76561198000000001UL };
            data.signature = SaveSystem.Sign(250, data.steamId);
            data.coinAmount = 999999;
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("integrity check"));
            Assert.That(SaveSystem.Verify(data, data.steamId), Is.False);
            Assert.That(data.coinAmount, Is.Zero);
        }

        [Test]
        public void Verify_RejectsSaveFromAnotherAccount()
        {
            var data = new CoinData(_coin) { coinAmount = 250, steamId = 76561198000000001UL };
            data.signature = SaveSystem.Sign(250, data.steamId);
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("integrity check"));
            Assert.That(SaveSystem.Verify(data, 76561198000000002UL), Is.False);
            Assert.That(data.coinAmount, Is.Zero);
        }

        [Test]
        public void Verify_CapsOldUnsignedSave()
        {
            var data = JsonUtility.FromJson<CoinData>("{\"coinAmount\":50000}");
            Assert.That(SaveSystem.Verify(data, 76561198000000001UL), Is.False);
            Assert.That(data.coinAmount, Is.EqualTo(SaveSystem.LegacyMaxCoins));

            var small = JsonUtility.FromJson<CoinData>("{\"coinAmount\":80}");
            Assert.That(SaveSystem.Verify(small, 76561198000000001UL), Is.True);
            Assert.That(small.coinAmount, Is.EqualTo(80));
        }

        [Test]
        public void UpdateCoinAmount_RejectsOversizedChangeAndNeverGoesNegative()
        {
            _coin.amount = 10;
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Rejected coin change"));
            _coin.UpdateCoinAmount(Coin.MaxSingleChange + 1);
            Assert.That(_coin.amount, Is.EqualTo(10));

            _coin.UpdateCoinAmount(-50);
            Assert.That(_coin.amount, Is.Zero);
        }

        [Test]
        public void SaveSystem_WithoutSteam_LoadReturnsNullAndSaveIsNoOp()
        {
            Assume.That(SteamClient.IsValid, Is.False, "Steam is initialised in this editor; skipping to avoid touching the real cloud save.");
            Assert.That(SaveSystem.LoadCoin(), Is.Null);
            Assert.DoesNotThrow(() => SaveSystem.Save(_coin));
        }

        [Test]
        public void LoadCoin_WithoutSteam_KeepsAmountAndDoesNotRaiseChanged()
        {
            Assume.That(SteamClient.IsValid, Is.False, "Steam is initialised in this editor; skipping to avoid touching the real cloud save.");
            _coin.amount = 64;
            int raised = 0;
            Coin.OnCoinChanged handler = () => raised++;
            Coin.CoinChanged += handler;
            try
            {
                LogAssert.Expect(LogType.Warning, "Open Steam!");
                _coin.LoadCoin();
                Assert.That(_coin.amount, Is.EqualTo(64));
                Assert.That(raised, Is.Zero);
            }
            finally
            {
                Coin.CoinChanged -= handler;
            }
        }
    }
}
