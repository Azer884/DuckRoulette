using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine.TestTools;

namespace DuckRoulette.Tests.Functional
{
    // GameManager/RoundManager turn flow against a live host (see HostSessionFixture).
    [TestFixture, Category("Functional")]
    public class HostTurnFlowTests : HostSessionFixture
    {
        [UnityTest]
        public IEnumerator Spawn_SoleHostGetsGun_AndRoundStarts()
        {
            Assert.That(Gm.IsServer, Is.True);
            Assert.That(Gm.playerWithGun.Value, Is.EqualTo(Host));
            Assert.That(Gm.AlivePlayersCount(), Is.EqualTo(1));
            Assert.That(Gm.canShoot.Value, Is.True);
            Assert.That(Rm.IsRoundActive, Is.True);
            Assert.That(Rm.RemainingTime, Is.GreaterThan(0f).And.LessThanOrEqualTo(RoundSeconds));
            yield break;
        }

        [UnityTest]
        public IEnumerator Shot_AdvancesChamberAndWrapsAfterSix()
        {
            Gm.bulletPosition.Value = 0;
            var chambers = new List<int>();
            for (int i = 0; i < 12; i++)
            {
                Gm.OnClientShotChanged(Host, true);
                chambers.Add(Gm.bulletPosition.Value);
            }

            Assert.That(chambers, Is.EqualTo(new[] { 1, 2, 3, 4, 5, 0, 1, 2, 3, 4, 5, 0 }));
            Assert.That(Rm.IsRoundActive, Is.True, "each shot ends the round and the hand-off starts the next");
            Assert.That(Gm.playerWithGun.Value, Is.EqualTo(Host), "a lone player keeps the gun");
            yield return null;
        }

        [UnityTest]
        public IEnumerator ShotChangedToFalse_ChangesNothing()
        {
            Gm.bulletPosition.Value = 3;
            Gm.OnClientShotChanged(Host, false);
            Assert.That(Gm.bulletPosition.Value, Is.EqualTo(3));
            yield return null;
        }

        [UnityTest]
        public IEnumerator RoundTimeout_PassesGunWithoutAdvancingChamber()
        {
            Gm.bulletPosition.Value = 4;
            var roundActive = Reflect.Get<NetworkVariable<bool>>(Rm, "_isRoundActiveNetworked");
            int roundsEnded = 0;
            NetworkVariable<bool>.OnValueChangedDelegate onChanged = (was, now) => { if (was && !now) roundsEnded++; };
            roundActive.OnValueChanged += onChanged;

            try
            {
                double start = EditorApplication.timeSinceStartup;
                while (!(roundsEnded > 0 && Rm.IsRoundActive) && EditorApplication.timeSinceStartup - start < 30.0)
                {
                    yield return null;
                }
            }
            finally
            {
                roundActive.OnValueChanged -= onChanged;
            }

            Assert.That(roundsEnded, Is.GreaterThanOrEqualTo(1), "the shot clock never ran out");
            Assert.That(Rm.IsRoundActive, Is.True, "timeout hand-off must start a new round");
            Assert.That(Gm.bulletPosition.Value, Is.EqualTo(4), "no trigger pull, so the chamber must not advance");
            Assert.That(Gm.playerWithGun.Value, Is.EqualTo(Host));
        }

        [UnityTest]
        public IEnumerator GetRandomClientId_NeverPicksShooterOrDeadPlayers()
        {
            Assume.That(TaskManager.Instance, Is.SameAs(Tm));
            HashSet<ulong> picked = SampleWithFakeClients(new Dictionary<ulong, bool> { [1] = true, [2] = false, [3] = true }, excluded: Host);

            Assert.That(picked, Is.EquivalentTo(new ulong[] { 1, 3 }));
            yield return null;
        }

        [UnityTest]
        public IEnumerator GetRandomClientId_ShooterAlone_KeepsGun()
        {
            Assert.That(RandomClientExcluding(Host), Is.EqualTo(Host));
            yield return null;
        }

        [UnityTest]
        public IEnumerator GetRandomClientId_PrefersPlayersWithNoOpenTasks_FallsBackToAllAlive()
        {
            var assigned = Reflect.Get<NetworkList<TaskManager.TaskEntry>>(Tm, "assignedTasks");
            HashSet<ulong> preferred, fallback;
            try
            {
                assigned.Add(new TaskManager.TaskEntry { ClientId = 1, TaskIndex = 0, Completed = false });
                assigned.Add(new TaskManager.TaskEntry { ClientId = 2, TaskIndex = 0, Completed = true });
                // client 3 was dealt nothing -> counts as done
                preferred = SampleWithFakeClients(new Dictionary<ulong, bool> { [1] = true, [2] = true, [3] = true }, excluded: Host);

                assigned.Add(new TaskManager.TaskEntry { ClientId = 2, TaskIndex = 1, Completed = false });
                assigned.Add(new TaskManager.TaskEntry { ClientId = 3, TaskIndex = 0, Completed = false });
                fallback = SampleWithFakeClients(new Dictionary<ulong, bool> { [1] = true, [2] = true, [3] = true }, excluded: Host);
            }
            finally
            {
                assigned.Clear();
            }

            Assert.That(preferred, Is.EquivalentTo(new ulong[] { 2, 3 }));
            Assert.That(fallback, Is.EquivalentTo(new ulong[] { 1, 2, 3 }), "nobody finished their tasks, so the gun must still move");
            yield return null;
        }

        [UnityTest]
        public IEnumerator StartRound_WithNoGunHolder_DoesNotStart()
        {
            Rm.EndRound();
            Assert.That(Rm.IsRoundActive, Is.False);

            Gm.playerWithGun.Value = ulong.MaxValue;
            Rm.StartRound();

            Assert.That(Rm.IsRoundActive, Is.False);
            yield return null;
        }
    }
}
