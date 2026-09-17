using System.Collections.Generic;
using System.Text;
using UnityEngine;

// Pure, side-effect-free checks used by ServerRpc bodies to validate client-originated requests.
// Deliberately free of Netcode/UnityEngine.Object dependencies (only Vector3) so every rule can be
// unit tested in EditMode without a running session. The RPCs themselves still derive the caller
// from ServerRpcParams.Receive.SenderClientId and read server-side state; these helpers only
// decide whether that state makes the request legitimate.
public static class RpcValidation
{
    /// <summary>No gun holder assigned (mirrors GameManager.playerWithGun's default).</summary>
    public const ulong NoClient = ulong.MaxValue;

    /// <summary>True when a and b are at most maxDistance apart (inclusive). A negative or NaN
    /// maxDistance, or any NaN/Infinity coordinate, is never within range.</summary>
    public static bool IsWithinDistance(Vector3 a, Vector3 b, float maxDistance)
    {
        if (!IsFinite(a) || !IsFinite(b) || float.IsNaN(maxDistance) || maxDistance < 0f)
        {
            return false;
        }

        return (a - b).sqrMagnitude <= maxDistance * maxDistance;
    }

    /// <summary>True when every component is a finite number (no NaN/Infinity).</summary>
    public static bool IsFinite(Vector3 v)
    {
        return !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z) &&
               !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);
    }

    /// <summary>Shortest distance from point to the segment [segmentStart, segmentEnd]. A
    /// zero-length segment degenerates to the point-to-point distance.</summary>
    public static float DistancePointToSegment(Vector3 point, Vector3 segmentStart, Vector3 segmentEnd)
    {
        Vector3 segment = segmentEnd - segmentStart;
        float lengthSquared = segment.sqrMagnitude;
        if (lengthSquared <= 1e-8f)
        {
            return Vector3.Distance(point, segmentStart);
        }

        float t = Mathf.Clamp01(Vector3.Dot(point - segmentStart, segment) / lengthSquared);
        return Vector3.Distance(point, segmentStart + segment * t);
    }

    /// <summary>Whether a straight-line projectile fired from origin along direction at speed
    /// could plausibly have reached point by now. The travelled path is the segment from origin
    /// to origin + direction * speed * (elapsedSeconds + travelSlackSeconds); point must lie within
    /// lateralTolerance of it. Zero/invalid direction, negative elapsed time, or non-finite input
    /// is rejected.</summary>
    public static bool IsPointNearShotPath(Vector3 point, Vector3 origin, Vector3 direction, float speed,
        float elapsedSeconds, float lateralTolerance, float travelSlackSeconds)
    {
        if (!IsFinite(point) || !IsFinite(origin) || !IsFinite(direction) ||
            direction.sqrMagnitude < 1e-6f || speed <= 0f || elapsedSeconds < 0f ||
            lateralTolerance < 0f || travelSlackSeconds < 0f)
        {
            return false;
        }

        float travelled = speed * (elapsedSeconds + travelSlackSeconds);
        Vector3 end = origin + direction.normalized * travelled;
        return DistancePointToSegment(point, origin, end) <= lateralTolerance;
    }

    /// <summary>Kill-credit eligibility: the killer is a real client, isn't the victim, the victim
    /// isn't already dead, and the two aren't teammates.</summary>
    public static bool IsKillCreditEligible(ulong victimId, ulong killerId, bool victimAlreadyDead, bool areTeammates)
    {
        return killerId != NoClient && victimId != NoClient && victimId != killerId && !victimAlreadyDead && !areTeammates;
    }

    /// <summary>True when (a, b) appears in teams in either order.</summary>
    public static bool AreTeammates(IReadOnlyList<(ulong, ulong)> teams, ulong a, ulong b)
    {
        if (teams == null)
        {
            return false;
        }

        for (int i = 0; i < teams.Count; i++)
        {
            if ((teams[i].Item1 == a && teams[i].Item2 == b) || (teams[i].Item1 == b && teams[i].Item2 == a))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when clientId appears in any team in teams.</summary>
    public static bool IsInAnyTeam(IReadOnlyList<(ulong, ulong)> teams, ulong clientId)
    {
        if (teams == null)
        {
            return false;
        }

        for (int i = 0; i < teams.Count; i++)
        {
            if (teams[i].Item1 == clientId || teams[i].Item2 == clientId)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Server-side gate for a live shot: the sender currently holds the gun, shooting is
    /// allowed, the gun is loaded, no shot has been fired yet this turn, and the chamber under the
    /// hammer is the live one.</summary>
    public static bool IsShotAllowed(ulong senderId, ulong gunHolderId, bool canShoot, bool isReloaded,
        bool shotAlreadyFiredThisTurn, int bulletPosition, int liveBulletPosition)
    {
        return IsGunHolder(senderId, gunHolderId) && canShoot && isReloaded && !shotAlreadyFiredThisTurn &&
               bulletPosition == liveBulletPosition;
    }

    /// <summary>Server-side gate for a reload: the sender holds the gun, shooting is allowed and
    /// the gun isn't already loaded (a reload re-rolls the live chamber, so it must not be
    /// repeatable at will).</summary>
    public static bool IsReloadAllowed(ulong senderId, ulong gunHolderId, bool canShoot, bool isReloaded)
    {
        return IsGunHolder(senderId, gunHolderId) && canShoot && !isReloaded;
    }

    /// <summary>True when senderId is the assigned gun holder (and one is assigned).</summary>
    public static bool IsGunHolder(ulong senderId, ulong gunHolderId)
    {
        return gunHolderId != NoClient && senderId == gunHolderId;
    }

    /// <summary>Cosmetic indices are 0 (nothing equipped) or 1..itemCount (1-based into the
    /// item array).</summary>
    public static bool IsValidCosmeticIndex(int index, int itemCount)
    {
        return index == 0 || (index >= 1 && index <= itemCount);
    }

    /// <summary>Returns index when valid, otherwise 0 (nothing equipped).</summary>
    public static int SanitizeCosmeticIndex(int index, int itemCount)
    {
        return IsValidCosmeticIndex(index, itemCount) ? index : 0;
    }

    /// <summary>Prepares untrusted text for display to other players: null becomes "", control
    /// characters (newlines, tabs, etc.) become spaces, the result is trimmed and clamped to
    /// maxChars (never splitting a surrogate pair), and every '&lt;' is followed by a zero-width
    /// space so TextMeshPro can't parse it as a rich-text tag (&lt;size&gt;, &lt;color&gt;, ...).</summary>
    public static string SanitizeChatMessage(string message, int maxChars)
    {
        if (string.IsNullOrEmpty(message) || maxChars <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(Mathf.Min(message.Length, maxChars));
        foreach (char c in message)
        {
            builder.Append(char.IsControl(c) ? ' ' : c);
        }

        string clean = ClampChars(builder.ToString().Trim(), maxChars);
        return clean.Replace("<", "<​");
    }

    /// <summary>Clamps value to at most maxChars UTF-16 chars without splitting a surrogate pair.
    /// null becomes "".</summary>
    public static string ClampChars(string value, int maxChars)
    {
        if (string.IsNullOrEmpty(value) || maxChars <= 0)
        {
            return string.Empty;
        }

        if (value.Length <= maxChars)
        {
            return value;
        }

        int length = maxChars;
        if (char.IsHighSurrogate(value[length - 1]))
        {
            length--;
        }

        return value.Substring(0, length);
    }

    /// <summary>Truncates value so its UTF-8 encoding fits in maxBytes, cutting only at whole
    /// code points. Use before constructing a FixedStringNBytes, whose string constructor throws
    /// on truncation (FixedString32Bytes holds 29 UTF-8 bytes, FixedString64Bytes holds 61).</summary>
    public static string TruncateUtf8(string value, int maxBytes)
    {
        if (string.IsNullOrEmpty(value) || maxBytes <= 0)
        {
            return string.Empty;
        }

        if (Encoding.UTF8.GetByteCount(value) <= maxBytes)
        {
            return value;
        }

        int bytes = 0;
        int i = 0;
        while (i < value.Length)
        {
            int charCount = char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]) ? 2 : 1;
            int charBytes = Encoding.UTF8.GetByteCount(value.ToCharArray(i, charCount));
            if (bytes + charBytes > maxBytes)
            {
                break;
            }

            bytes += charBytes;
            i += charCount;
        }

        return value.Substring(0, i);
    }

    /// <summary>True when at least cooldownSeconds have passed since lastTime (a lastTime of
    /// double.NegativeInfinity, i.e. "never", always passes).</summary>
    public static bool IsCooldownElapsed(double lastTime, double now, double cooldownSeconds)
    {
        return now - lastTime >= cooldownSeconds;
    }

    /// <summary>Voice payload sanity: compressedWritten is positive, fits inside the received
    /// array, and doesn't exceed maxBytes.</summary>
    public static bool IsValidVoicePayload(int payloadLength, int compressedWritten, int maxBytes)
    {
        return compressedWritten > 0 && compressedWritten <= payloadLength && compressedWritten <= maxBytes;
    }

    /// <summary>Server-counted slaps needed before a stun request is honoured. The client rolls a
    /// random limit in [3, 10), so fewer than 3 can never be a legitimate stun.</summary>
    public const int MinSlapsForStun = 3;

    /// <summary>True when recordedSlaps (counted server-side from validated slap impacts) is
    /// enough to justify a stun.</summary>
    public static bool HasEnoughSlapsForStun(int recordedSlaps)
    {
        return recordedSlaps >= MinSlapsForStun;
    }
}
