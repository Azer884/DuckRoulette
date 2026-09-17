using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;

namespace DuckRoulette.Tests.Security
{
    // Allow/deny edge cases for Assets/Scripts/Security/RpcValidation.cs, the pure checks ServerRpc
    // bodies use to validate client-originated requests.
    [TestFixture, Category("Security")]
    public class RpcValidationTests
    {
        private const ulong None = RpcValidation.NoClient;
        private static readonly Vector3 NaNVec = new Vector3(float.NaN, 0f, 0f);
        private static readonly Vector3 InfVec = new Vector3(0f, float.PositiveInfinity, 0f);

        // ---------- Distance / finiteness ----------

        [Test]
        public void IsFinite_RejectsNaNAndInfinityOnAnyAxis()
        {
            Assert.That(RpcValidation.IsFinite(new Vector3(1, -2, 3)), Is.True);
            Assert.That(RpcValidation.IsFinite(new Vector3(float.MaxValue, float.MinValue, 0)), Is.True);
            Assert.That(RpcValidation.IsFinite(new Vector3(float.NaN, 0, 0)), Is.False);
            Assert.That(RpcValidation.IsFinite(new Vector3(0, float.NaN, 0)), Is.False);
            Assert.That(RpcValidation.IsFinite(new Vector3(0, 0, float.NaN)), Is.False);
            Assert.That(RpcValidation.IsFinite(new Vector3(float.NegativeInfinity, 0, 0)), Is.False);
            Assert.That(RpcValidation.IsFinite(new Vector3(0, 0, float.PositiveInfinity)), Is.False);
        }

        [Test]
        public void IsWithinDistance_InclusiveBoundary()
        {
            Vector3 a = Vector3.zero, b = new Vector3(3, 4, 0); // distance 5
            Assert.That(RpcValidation.IsWithinDistance(a, b, 5f), Is.True);
            Assert.That(RpcValidation.IsWithinDistance(a, b, 5.01f), Is.True);
            Assert.That(RpcValidation.IsWithinDistance(a, b, 4.99f), Is.False);
            Assert.That(RpcValidation.IsWithinDistance(a, a, 0f), Is.True);
        }

        [Test]
        public void IsWithinDistance_DeniesInvalidInput()
        {
            Assert.That(RpcValidation.IsWithinDistance(Vector3.zero, Vector3.zero, -0.001f), Is.False);
            Assert.That(RpcValidation.IsWithinDistance(Vector3.zero, Vector3.zero, float.NaN), Is.False);
            Assert.That(RpcValidation.IsWithinDistance(NaNVec, Vector3.zero, 1000f), Is.False, "NaN compares false everywhere; must not slip through as 'in range'");
            Assert.That(RpcValidation.IsWithinDistance(Vector3.zero, InfVec, 1000f), Is.False);
            Assert.That(RpcValidation.IsWithinDistance(InfVec, InfVec, 1000f), Is.False);
        }

        [Test]
        public void IsWithinDistance_HugeFiniteSeparation_Denied()
        {
            // sqrMagnitude overflows to +Inf; must read as out of range, not wrap.
            var far = new Vector3(float.MaxValue, 0, 0);
            Assert.That(RpcValidation.IsWithinDistance(far, -far, 10f), Is.False);
        }

        [Test]
        public void DistancePointToSegment_ProjectsAndClamps()
        {
            Vector3 s = Vector3.zero, e = new Vector3(10, 0, 0);
            Assert.That(RpcValidation.DistancePointToSegment(new Vector3(5, 3, 0), s, e), Is.EqualTo(3f).Within(1e-5f));
            Assert.That(RpcValidation.DistancePointToSegment(new Vector3(-4, 3, 0), s, e), Is.EqualTo(5f).Within(1e-5f), "before start clamps to start");
            Assert.That(RpcValidation.DistancePointToSegment(new Vector3(13, 4, 0), s, e), Is.EqualTo(5f).Within(1e-5f), "past end clamps to end");
            Assert.That(RpcValidation.DistancePointToSegment(new Vector3(7, 0, 0), s, e), Is.EqualTo(0f).Within(1e-5f));
        }

        [Test]
        public void DistancePointToSegment_ZeroLengthSegment_IsPointDistance()
        {
            Vector3 p = new Vector3(1, 2, 2);
            Assert.That(RpcValidation.DistancePointToSegment(p, Vector3.zero, Vector3.zero), Is.EqualTo(3f).Within(1e-5f));
        }

        // ---------- Shot path ----------

        private static bool NearPath(Vector3 point, Vector3 dir, float speed = 10f, float elapsed = 1f, float tolerance = 0.5f, float slack = 0f) =>
            RpcValidation.IsPointNearShotPath(point, Vector3.zero, dir, speed, elapsed, tolerance, slack);

        [Test]
        public void ShotPath_AllowsPointsAlongTravelledSegment()
        {
            Assert.That(NearPath(new Vector3(0, 0, 5), Vector3.forward), Is.True);
            Assert.That(NearPath(new Vector3(0.4f, 0, 9.9f), Vector3.forward), Is.True);
            Assert.That(NearPath(Vector3.zero, Vector3.forward, elapsed: 0f), Is.True, "hit at the muzzle at t=0");
        }

        [Test]
        public void ShotPath_DeniesPointsTheBulletCouldNotHaveReached()
        {
            Assert.That(NearPath(new Vector3(0, 0, 12), Vector3.forward), Is.False, "beyond speed*elapsed");
            Assert.That(NearPath(new Vector3(0, 0, -2), Vector3.forward), Is.False, "behind the muzzle");
            Assert.That(NearPath(new Vector3(1f, 0, 5), Vector3.forward), Is.False, "outside lateral tolerance");
        }

        [Test]
        public void ShotPath_SlackExtendsReach()
        {
            Assert.That(NearPath(new Vector3(0, 0, 12), Vector3.forward, slack: 0.25f), Is.True);
            // reach = 10 * (1 + 0.25) = 12.5; tolerance 0.5 also applies past the end cap.
            Assert.That(NearPath(new Vector3(0, 0, 13f), Vector3.forward, slack: 0.25f), Is.True, "end cap is inclusive of the tolerance");
            Assert.That(NearPath(new Vector3(0, 0, 13.1f), Vector3.forward, slack: 0.25f), Is.False);
        }

        [Test]
        public void ShotPath_DirectionMagnitudeDoesNotChangeReach()
        {
            Assert.That(NearPath(new Vector3(0, 0, 12), Vector3.forward * 100f), Is.False, "an unnormalised direction must not extend range");
            Assert.That(NearPath(new Vector3(0, 0, 9), Vector3.forward * 0.01f), Is.True);
        }

        [Test]
        public void ShotPath_DeniesDegenerateOrHostileParameters()
        {
            Vector3 p = new Vector3(0, 0, 1);
            Assert.That(NearPath(p, Vector3.zero), Is.False, "zero direction");
            Assert.That(NearPath(p, Vector3.forward, speed: 0f), Is.False);
            Assert.That(NearPath(p, Vector3.forward, speed: -10f), Is.False);
            Assert.That(NearPath(p, Vector3.forward, elapsed: -0.1f), Is.False);
            Assert.That(NearPath(p, Vector3.forward, tolerance: -1f), Is.False);
            Assert.That(NearPath(p, Vector3.forward, slack: -1f), Is.False);
            Assert.That(NearPath(NaNVec, Vector3.forward), Is.False);
            Assert.That(NearPath(p, NaNVec), Is.False);
            Assert.That(RpcValidation.IsPointNearShotPath(p, InfVec, Vector3.forward, 10f, 1f, 0.5f, 0f), Is.False);
        }

        // ---------- Kill credit / teams ----------

        [Test]
        public void KillCredit_AllowedForValidKill()
        {
            Assert.That(RpcValidation.IsKillCreditEligible(victimId: 2, killerId: 1, victimAlreadyDead: false, areTeammates: false), Is.True);
            Assert.That(RpcValidation.IsKillCreditEligible(0, 5, false, false), Is.True, "host (client 0) can be a victim");
        }

        [TestCase(1UL, 1UL, false, false, TestName = "KillCredit_Denied_SelfKill")]
        [TestCase(2UL, None, false, false, TestName = "KillCredit_Denied_NoKiller")]
        [TestCase(None, 1UL, false, false, TestName = "KillCredit_Denied_NoVictim")]
        [TestCase(None, None, false, false, TestName = "KillCredit_Denied_BothSentinel")]
        [TestCase(2UL, 1UL, true, false, TestName = "KillCredit_Denied_VictimAlreadyDead")]
        [TestCase(2UL, 1UL, false, true, TestName = "KillCredit_Denied_Teammates")]
        public void KillCredit_Denied(ulong victim, ulong killer, bool dead, bool teammates)
        {
            Assert.That(RpcValidation.IsKillCreditEligible(victim, killer, dead, teammates), Is.False);
        }

        [Test]
        public void AreTeammates_EitherOrder_NullSafe()
        {
            var teams = new List<(ulong, ulong)> { (1, 2), (5, 0) };
            Assert.That(RpcValidation.AreTeammates(teams, 1, 2), Is.True);
            Assert.That(RpcValidation.AreTeammates(teams, 2, 1), Is.True);
            Assert.That(RpcValidation.AreTeammates(teams, 0, 5), Is.True);
            Assert.That(RpcValidation.AreTeammates(teams, 1, 5), Is.False);
            Assert.That(RpcValidation.AreTeammates(teams, 1, 1), Is.False);
            Assert.That(RpcValidation.AreTeammates(new List<(ulong, ulong)>(), 1, 2), Is.False);
            Assert.That(RpcValidation.AreTeammates(null, 1, 2), Is.False);
        }

        [Test]
        public void IsInAnyTeam_EitherSlot_NullSafe()
        {
            var teams = new List<(ulong, ulong)> { (1, 2) };
            Assert.That(RpcValidation.IsInAnyTeam(teams, 1), Is.True);
            Assert.That(RpcValidation.IsInAnyTeam(teams, 2), Is.True);
            Assert.That(RpcValidation.IsInAnyTeam(teams, 3), Is.False);
            Assert.That(RpcValidation.IsInAnyTeam(null, 1), Is.False);
        }

        // ---------- Gun ----------

        [Test]
        public void IsGunHolder_RequiresAssignedMatchingHolder()
        {
            Assert.That(RpcValidation.IsGunHolder(3, 3), Is.True);
            Assert.That(RpcValidation.IsGunHolder(0, 0), Is.True);
            Assert.That(RpcValidation.IsGunHolder(3, 4), Is.False);
            Assert.That(RpcValidation.IsGunHolder(None, None), Is.False, "sentinel must never match itself");
            Assert.That(RpcValidation.IsGunHolder(3, None), Is.False);
        }

        [Test]
        public void IsShotAllowed_AllConditionsMet()
        {
            Assert.That(RpcValidation.IsShotAllowed(senderId: 1, gunHolderId: 1, canShoot: true, isReloaded: true,
                shotAlreadyFiredThisTurn: false, bulletPosition: 2, liveBulletPosition: 2), Is.True);
        }

        [TestCase(2UL, 1UL, true, true, false, 2, 2, TestName = "Shot_Denied_NotGunHolder")]
        [TestCase(None, None, true, true, false, 2, 2, TestName = "Shot_Denied_NoGunHolderAssigned")]
        [TestCase(1UL, 1UL, false, true, false, 2, 2, TestName = "Shot_Denied_CannotShoot")]
        [TestCase(1UL, 1UL, true, false, false, 2, 2, TestName = "Shot_Denied_NotReloaded")]
        [TestCase(1UL, 1UL, true, true, true, 2, 2, TestName = "Shot_Denied_AlreadyFiredThisTurn")]
        [TestCase(1UL, 1UL, true, true, false, 3, 2, TestName = "Shot_Denied_EmptyChamber")]
        [TestCase(1UL, 1UL, true, true, false, 0, 5, TestName = "Shot_Denied_ChamberWrapMismatch")]
        public void IsShotAllowed_Denied(ulong sender, ulong holder, bool canShoot, bool reloaded, bool fired, int pos, int live)
        {
            Assert.That(RpcValidation.IsShotAllowed(sender, holder, canShoot, reloaded, fired, pos, live), Is.False);
        }

        [Test]
        public void IsReloadAllowed_OnlyHolderWithUnloadedGun()
        {
            Assert.That(RpcValidation.IsReloadAllowed(1, 1, canShoot: true, isReloaded: false), Is.True);
            Assert.That(RpcValidation.IsReloadAllowed(1, 1, true, isReloaded: true), Is.False, "re-rolling the live chamber at will");
            Assert.That(RpcValidation.IsReloadAllowed(1, 1, canShoot: false, false), Is.False);
            Assert.That(RpcValidation.IsReloadAllowed(2, 1, true, false), Is.False);
            Assert.That(RpcValidation.IsReloadAllowed(None, None, true, false), Is.False);
        }

        // ---------- Cosmetics ----------

        [TestCase(0, 5, true)]
        [TestCase(1, 5, true)]
        [TestCase(5, 5, true)]
        [TestCase(6, 5, false)]
        [TestCase(-1, 5, false)]
        [TestCase(int.MinValue, 5, false)]
        [TestCase(int.MaxValue, 5, false)]
        [TestCase(0, 0, true)]
        [TestCase(1, 0, false)]
        [TestCase(1, -3, false)]
        public void IsValidCosmeticIndex(int index, int count, bool expected)
        {
            Assert.That(RpcValidation.IsValidCosmeticIndex(index, count), Is.EqualTo(expected));
            Assert.That(RpcValidation.SanitizeCosmeticIndex(index, count), Is.EqualTo(expected ? index : 0));
        }

        // ---------- Text ----------

        [Test]
        public void SanitizeChat_NullEmptyOrZeroBudget_IsEmpty()
        {
            Assert.That(RpcValidation.SanitizeChatMessage(null, 50), Is.EqualTo(string.Empty));
            Assert.That(RpcValidation.SanitizeChatMessage("", 50), Is.EqualTo(string.Empty));
            Assert.That(RpcValidation.SanitizeChatMessage("hello", 0), Is.EqualTo(string.Empty));
            Assert.That(RpcValidation.SanitizeChatMessage("hello", -1), Is.EqualTo(string.Empty));
        }

        [Test]
        public void SanitizeChat_ControlCharsBecomeSpaces_AndTrimmed()
        {
            Assert.That(RpcValidation.SanitizeChatMessage("\n\thi\r\n", 50), Is.EqualTo("hi"));
            Assert.That(RpcValidation.SanitizeChatMessage("a\nb c", 50), Is.EqualTo("a b c"));
            Assert.That(RpcValidation.SanitizeChatMessage("   ", 50), Is.EqualTo(string.Empty));
        }

        [Test]
        public void SanitizeChat_ClampsLength()
        {
            string result = RpcValidation.SanitizeChatMessage(new string('x', 500), 64);
            Assert.That(result.Length, Is.EqualTo(64));
        }

        [TestCase("<size=500>HUGE</size>")]
        [TestCase("<color=#FF0000>fake system message")]
        [TestCase("<sprite=0><link=\"x\">")]
        [TestCase("<<b>>")]
        public void SanitizeChat_NeutralisesRichTextTags(string hostile)
        {
            string result = RpcValidation.SanitizeChatMessage(hostile, 200);
            for (int i = 0; i < result.Length; i++)
            {
                if (result[i] == '<')
                {
                    Assert.That(i + 1 < result.Length && result[i + 1] == '​', Is.True, $"'<' at {i} not followed by a zero-width space in \"{result}\"");
                }
            }
            Assert.That(result.Replace("​", ""), Is.EqualTo(hostile), "only the zero-width spaces should be added");
        }

        [Test]
        public void SanitizeChat_DoesNotSplitSurrogatePair()
        {
            string result = RpcValidation.SanitizeChatMessage("ab\U0001F600", 3);
            Assert.That(result, Is.EqualTo("ab"));
            Assert.That(RpcValidation.SanitizeChatMessage("ab\U0001F600", 4), Is.EqualTo("ab\U0001F600"));
        }

        [Test]
        public void ClampChars_Behaviour()
        {
            Assert.That(RpcValidation.ClampChars(null, 5), Is.EqualTo(string.Empty));
            Assert.That(RpcValidation.ClampChars("abc", 0), Is.EqualTo(string.Empty));
            Assert.That(RpcValidation.ClampChars("abc", 5), Is.EqualTo("abc"));
            Assert.That(RpcValidation.ClampChars("abcdef", 3), Is.EqualTo("abc"));
            Assert.That(RpcValidation.ClampChars("\U0001F600\U0001F600", 3), Is.EqualTo("\U0001F600"));
            Assert.That(RpcValidation.ClampChars("\U0001F600", 1), Is.EqualTo(string.Empty));
        }

        [Test]
        public void TruncateUtf8_CutsOnlyAtCodePoints()
        {
            Assert.That(RpcValidation.TruncateUtf8(null, 10), Is.EqualTo(string.Empty));
            Assert.That(RpcValidation.TruncateUtf8("abc", 0), Is.EqualTo(string.Empty));
            Assert.That(RpcValidation.TruncateUtf8("abc", 3), Is.EqualTo("abc"));
            Assert.That(RpcValidation.TruncateUtf8("héllo", 2), Is.EqualTo("h"), "é is 2 bytes and must not be halved");
            Assert.That(RpcValidation.TruncateUtf8("héllo", 3), Is.EqualTo("hé"));
            Assert.That(RpcValidation.TruncateUtf8("\U0001F600x", 3), Is.EqualTo(string.Empty), "4-byte emoji does not fit in 3");
            Assert.That(RpcValidation.TruncateUtf8("\U0001F600x", 4), Is.EqualTo("\U0001F600"));
        }

        [TestCase(29)]
        [TestCase(61)]
        public void TruncateUtf8_ResultAlwaysFitsFixedString(int capacityBytes)
        {
            string[] hostile =
            {
                new string('a', 300),
                string.Concat(System.Linq.Enumerable.Repeat("é", 100)),
                string.Concat(System.Linq.Enumerable.Repeat("\U0001F600", 40)),
                "ab" + string.Concat(System.Linq.Enumerable.Repeat("中", 40)),
            };

            foreach (string input in hostile)
            {
                string truncated = RpcValidation.TruncateUtf8(input, capacityBytes);
                Assert.That(Encoding.UTF8.GetByteCount(truncated), Is.LessThanOrEqualTo(capacityBytes));
                Assert.That(input.StartsWith(truncated), Is.True);
                if (capacityBytes == 29)
                    Assert.DoesNotThrow(() => new FixedString32Bytes(truncated));
                else
                    Assert.DoesNotThrow(() => new FixedString64Bytes(truncated));
            }
        }

        // ---------- Rate limit / payload / stun ----------

        [Test]
        public void IsCooldownElapsed_Boundaries()
        {
            Assert.That(RpcValidation.IsCooldownElapsed(double.NegativeInfinity, 0, 5), Is.True, "never used before");
            Assert.That(RpcValidation.IsCooldownElapsed(10, 15, 5), Is.True, "exactly the cooldown");
            Assert.That(RpcValidation.IsCooldownElapsed(10, 14.999, 5), Is.False);
            Assert.That(RpcValidation.IsCooldownElapsed(10, 10, 0), Is.True);
            Assert.That(RpcValidation.IsCooldownElapsed(20, 10, 5), Is.False, "clock went backwards");
        }

        [TestCase(100, 50, 64, true)]
        [TestCase(100, 64, 64, true)]
        [TestCase(100, 100, 100, true)]
        [TestCase(100, 0, 64, false)]
        [TestCase(100, -5, 64, false)]
        [TestCase(40, 50, 64, false)]
        [TestCase(100, 65, 64, false)]
        [TestCase(0, 0, 0, false)]
        public void IsValidVoicePayload(int payloadLength, int written, int max, bool expected)
        {
            Assert.That(RpcValidation.IsValidVoicePayload(payloadLength, written, max), Is.EqualTo(expected));
        }

        [Test]
        public void Stun_RequiresMinimumServerCountedSlaps()
        {
            Assert.That(RpcValidation.MinSlapsForStun, Is.EqualTo(3));
            Assert.That(RpcValidation.HasEnoughSlapsForStun(-1), Is.False);
            Assert.That(RpcValidation.HasEnoughSlapsForStun(0), Is.False);
            Assert.That(RpcValidation.HasEnoughSlapsForStun(2), Is.False);
            Assert.That(RpcValidation.HasEnoughSlapsForStun(3), Is.True);
            Assert.That(RpcValidation.HasEnoughSlapsForStun(10), Is.True);
        }
    }
}
