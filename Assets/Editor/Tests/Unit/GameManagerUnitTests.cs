using System.Collections.Generic;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;

namespace DuckRoulette.Tests.Unit
{
    // GameManager logic that does not need a live session. Components are added in edit mode, so
    // Awake/OnNetworkSpawn never run and IsServer is false - which is exactly what the "ignored when
    // not server" guards are checked against. Turn passing with a real host is in HostTurnFlowTests.
    [TestFixture, Category("Unit")]
    public class GameManagerUnitTests
    {
        private GameObject _go;
        private GameManager _gm;
        private Random.State _randomState;

        [SetUp]
        public void SetUp()
        {
            Assume.That(NetworkManager.Singleton, Is.Null, "A NetworkManager is live in the editor; offline GameManager tests need none.");
            _randomState = Random.state;
            Random.InitState(1234);
            _go = new GameObject("GameManager (unit test)") { hideFlags = HideFlags.DontSave };
            _gm = _go.AddComponent<GameManager>();
        }

        [TearDown]
        public void TearDown()
        {
            Random.state = _randomState;
            if (_go != null)
            {
                Object.DestroyImmediate(_go);
            }
        }

        private Dictionary<ulong, bool> PlayerStates => Reflect.Get<Dictionary<ulong, bool>>(_gm, "_playerStates");

        [Test]
        public void GetRandomClientId_WithoutNetworkManager_ReturnsSentinel()
        {
            object result = Reflect.Call(_gm, "GetRandomClientId", 3UL);
            Assert.That((ulong)result, Is.EqualTo(ulong.MaxValue));
        }

        [Test]
        public void GetPlayerNickname_WithoutNetworkManager_ReturnsUnknownPlayer()
        {
            Assert.That(_gm.GetPlayerNickname(0), Is.EqualTo("Unknown Player"));
        }

        [TestCase(100f)]
        [TestCase(150f)]
        public void Percentage_AtOrAboveHundred_AlwaysTrue(float chance)
        {
            for (int i = 0; i < 200; i++)
            {
                Assert.That(_gm.Percentage(chance), Is.True);
            }
        }

        [TestCase(0f)]
        [TestCase(-25f)]
        public void Percentage_AtOrBelowZero_AlwaysFalse(float chance)
        {
            for (int i = 0; i < 200; i++)
            {
                Assert.That(_gm.Percentage(chance), Is.False);
            }
        }

        [Test]
        public void Percentage_Fifty_ProducesBothOutcomes()
        {
            int trues = 0;
            for (int i = 0; i < 1000; i++)
            {
                if (_gm.Percentage(50f)) trues++;
            }

            Assert.That(trues, Is.InRange(350, 650));
        }

        [Test]
        public void Reload_PicksEverySixChambers_AndMarksReloaded()
        {
            var seen = new HashSet<int>();
            for (int i = 0; i < 600; i++)
            {
                _gm.Reload();
                Assert.That(_gm.randomBulletPosition.Value, Is.InRange(0, 5));
                seen.Add(_gm.randomBulletPosition.Value);
            }

            Assert.That(seen, Is.EquivalentTo(new[] { 0, 1, 2, 3, 4, 5 }));
            Assert.That(_gm.isReloaded.Value, Is.True);
        }

        [Test]
        public void OnClientShotChanged_WhenNotServer_DoesNotAdvanceChamberOrPassGun()
        {
            _gm.bulletPosition.Value = 5;
            _gm.playerWithGun.Value = 2;

            _gm.OnClientShotChanged(2, true);

            Assert.That(_gm.bulletPosition.Value, Is.EqualTo(5));
            Assert.That(_gm.playerWithGun.Value, Is.EqualTo(2UL));
        }

        [Test]
        public void PassGunOnTimeout_WhenNotServer_IsIgnored()
        {
            _gm.playerWithGun.Value = 4;
            _gm.PassGunOnTimeout();
            Assert.That(_gm.playerWithGun.Value, Is.EqualTo(4UL));
        }

        [Test]
        public void Defaults_NoGunHolder_NoAlivePlayers()
        {
            Assert.That(_gm.playerWithGun.Value, Is.EqualTo(ulong.MaxValue));
            Assert.That(_gm.AlivePlayersCount(), Is.EqualTo(0));
            Assert.That(_gm.canShoot.Value, Is.True);
        }

        [Test]
        public void MarkPlayerInactive_UnknownPlayer_ReturnsFalse()
        {
            Assert.That((bool)Reflect.Call(_gm, "MarkPlayerInactive", 9UL, false), Is.False);
        }

        [Test]
        public void MarkPlayerInactive_AlivePlayer_TransitionsOnce()
        {
            Reflect.Get<NetworkVariable<int>>(_gm, "_alivePlayersCount").Value = 3;
            PlayerStates[1] = true;
            PlayerStates[5] = true;
            PlayerStates[7] = true;

            Assert.That((bool)Reflect.Call(_gm, "MarkPlayerInactive", 5UL, false), Is.True);
            Assert.That(_gm.AlivePlayersCount(), Is.EqualTo(2));
            Assert.That(PlayerStates[5], Is.False);

            // Second report of the same death must not decrement again or re-broadcast.
            Assert.That((bool)Reflect.Call(_gm, "MarkPlayerInactive", 5UL, false), Is.False);
            Assert.That(_gm.AlivePlayersCount(), Is.EqualTo(2));
        }

        [Test]
        public void GetAlivePlayerId_ReturnsSurvivor_OrSentinelWhenNone()
        {
            PlayerStates[1] = false;
            PlayerStates[4] = true;
            Assert.That((ulong)Reflect.Call(_gm, "GetAlivePlayerId"), Is.EqualTo(4UL));

            PlayerStates[4] = false;
            Assert.That((ulong)Reflect.Call(_gm, "GetAlivePlayerId"), Is.EqualTo(ulong.MaxValue));
        }
    }
}
