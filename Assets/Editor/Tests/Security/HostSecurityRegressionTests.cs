using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace DuckRoulette.Tests.Security
{
    // Regression tests for the server-side gates added in GameManager (Docs/SecurityAudit.md), run
    // against a live host. The host is the only real sender, so "hostile" requests are ones the host
    // itself is not entitled to make (not the gun holder, not a teammate, targeting itself).
    [TestFixture, Category("Security")]
    public class HostSecurityRegressionTests : HostSessionFixture
    {
        private const ulong NotHolder = 5;

        [UnityTest]
        public IEnumerator ShotReport_FromNonHolder_DoesNotAdvanceChamberOrPassGun()
        {
            // Audit #6: a non-holder flipping its own hasShot used to skip the holder's turn.
            Gm.bulletPosition.Value = 2;
            Gm.OnClientShotChanged(NotHolder, true);

            Assert.That(Gm.bulletPosition.Value, Is.EqualTo(2));
            Assert.That(Gm.playerWithGun.Value, Is.EqualTo(Host));
            yield return null;
        }

        [UnityTest]
        public IEnumerator TryAuthorizeShot_HolderOnly_OncePerTurn_LiveChamberOnly()
        {
            // Audit #2: ShootServerRpc used to fire out of turn, repeatedly.
            Gm.isReloaded.Value = true;
            Gm.canShoot.Value = true;
            Gm.bulletPosition.Value = 2;
            Gm.randomBulletPosition.Value = 2;

            Assert.That(Gm.TryAuthorizeShot(NotHolder), Is.False, "non-holder");
            Assert.That(Gm.TryAuthorizeShot(Host), Is.True, "holder, loaded, live chamber");
            Assert.That(Gm.TryAuthorizeShot(Host), Is.False, "second live shot in the same turn");

            // Hand-off starts a new turn (chamber advances to 3).
            Gm.OnClientShotChanged(Host, true);
            Gm.isReloaded.Value = true;
            Gm.canShoot.Value = true;
            Gm.randomBulletPosition.Value = 4;
            Assert.That(Gm.TryAuthorizeShot(Host), Is.False, "chamber under the hammer is empty");

            Gm.randomBulletPosition.Value = Gm.bulletPosition.Value;
            Assert.That(Gm.TryAuthorizeShot(Host), Is.True, "new turn, live chamber");

            Gm.OnClientShotChanged(Host, true);
            Gm.randomBulletPosition.Value = Gm.bulletPosition.Value;
            Gm.isReloaded.Value = false;
            Assert.That(Gm.TryAuthorizeShot(Host), Is.False, "not reloaded");

            Gm.isReloaded.Value = true;
            Gm.canShoot.Value = false;
            Assert.That(Gm.TryAuthorizeShot(Host), Is.False, "canShoot false");
            yield return null;
        }

        [UnityTest]
        public IEnumerator IsReloadAllowed_HolderWithUnloadedGunOnly()
        {
            // Audit #5: anyone could re-roll the live chamber during someone else's turn.
            Gm.canShoot.Value = true;
            Gm.isReloaded.Value = false;
            Assert.That(Gm.IsReloadAllowed(Host), Is.True);
            Assert.That(Gm.IsReloadAllowed(NotHolder), Is.False);

            Gm.isReloaded.Value = true;
            Assert.That(Gm.IsReloadAllowed(Host), Is.False, "re-rolling an already loaded gun");
            yield return null;
        }

        [UnityTest]
        public IEnumerator UpdateKillsServerRpc_ClientReportedKillsAreIgnored()
        {
            // Audit #3: kill credit (and the coin reward) used to be whatever id a client named.
            var kills = Reflect.Get<Dictionary<ulong, int>>(Gm, "_playersKills");
            kills[Host] = 0;

            Gm.UpdateKillsServerRpc(Host, 1);
            Gm.UpdateKillsServerRpc(Host, 50);
            Gm.UpdateKillsServerRpc(7, 1);
            yield return Frames(5);

            Assert.That(kills[Host], Is.EqualTo(0));
            Assert.That(kills.ContainsKey(7), Is.False);
        }

        [UnityTest]
        public IEnumerator EndTeamUpServerRpc_ForSomeoneElsesTeam_IsIgnored()
        {
            // Audit #12: any client could break up any other pair.
            var teams = Reflect.Get<List<(ulong, ulong)>>(Gm, "_teams");
            teams.Add((3, 4));

            Gm.EndTeamUpServerRpc(4);
            yield return Frames(5);

            Assert.That(teams, Is.EquivalentTo(new[] { ((ulong)3, (ulong)4) }));
            teams.Clear();
        }

        [UnityTest]
        public IEnumerator TeamUpRequestServerRpc_SelfOrUnknownTarget_CreatesNoPendingRequest()
        {
            // Audit #13: requests to yourself / players who don't exist / from anywhere.
            var pending = Reflect.Get<Dictionary<ulong, ulong>>(Gm, "_pendingTeamUpRequests");

            Gm.TeamUpRequestServerRpc(Host);
            Gm.TeamUpRequestServerRpc(9);
            yield return Frames(5);

            Assert.That(pending, Is.Empty);
        }

        [UnityTest]
        public IEnumerator TryValidateShotKill_NoRegisteredShot_Denied()
        {
            // Audit #1: UpdatePlayerStateServerRpc(anyVictim, anyKiller) used to kill instantly.
            Assert.That(Gm.TryValidateShotKill(victimId: 3, killerId: Host, hitReferencePoint: UnityEngine.Vector3.zero, lateralTolerance: 100f), Is.False);
            Assert.That(Gm.TryValidateShotKill(victimId: Host, killerId: Host, hitReferencePoint: UnityEngine.Vector3.zero, lateralTolerance: 100f), Is.False);
            yield return null;
        }
    }
}
