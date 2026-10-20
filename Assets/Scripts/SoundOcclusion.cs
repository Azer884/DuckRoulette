using System.Collections.Generic;
using UnityEngine;

/// <summary>Works out how much the world between a listener and a sound muffles it. Shared by
/// proximity voice chat and the boombox music so both react to walls the same way.</summary>
public static class SoundOcclusion
{
    /// <summary>Muffle: how much high frequency is cut, 0..1. Dampen: how much volume is lost, 0..1.</summary>
    public readonly struct Amount
    {
        public readonly float Muffle;
        public readonly float Dampen;

        public Amount(float muffle, float dampen)
        {
            Muffle = Mathf.Clamp01(muffle);
            Dampen = Mathf.Clamp01(dampen);
        }

        public static readonly Amount Clear = new(0f, 0f);
    }

    public static Amount PresetFor(SoundOccluderType type) => type switch
    {
        SoundOccluderType.Wall => new Amount(1f, 0.5f),
        SoundOccluderType.Heavy => new Amount(1f, 0.75f),
        SoundOccluderType.Wood => new Amount(0.65f, 0.3f),
        SoundOccluderType.Metal => new Amount(0.75f, 0.4f),
        SoundOccluderType.Glass => new Amount(0.6f, 0.15f),
        SoundOccluderType.Soft => new Amount(0.7f, 0.25f),
        SoundOccluderType.Cloth => new Amount(0.35f, 0.1f),
        SoundOccluderType.Foliage => new Amount(0.25f, 0.08f),
        SoundOccluderType.Fence => new Amount(0.12f, 0.04f),
        SoundOccluderType.Prop => new Amount(0.3f, 0.12f),
        _ => Amount.Clear
    };

    // Name fragments checked (lower case) on the collider's object and its parents when there is
    // no SoundOccluder. First match wins, so more specific words come first.
    private static readonly (string word, SoundOccluderType type)[] Keywords =
    {
        ("glass", SoundOccluderType.Glass), ("window", SoundOccluderType.Glass),
        ("fence", SoundOccluderType.Fence), ("rail", SoundOccluderType.Fence), ("grate", SoundOccluderType.Fence),
        ("tree", SoundOccluderType.Foliage), ("pine", SoundOccluderType.Foliage), ("bush", SoundOccluderType.Foliage),
        ("shrub", SoundOccluderType.Foliage), ("leaf", SoundOccluderType.Foliage), ("leaves", SoundOccluderType.Foliage),
        ("foliage", SoundOccluderType.Foliage), ("grass", SoundOccluderType.Foliage), ("reed", SoundOccluderType.Foliage),
        ("plant", SoundOccluderType.Foliage),
        ("tent", SoundOccluderType.Cloth), ("cloth", SoundOccluderType.Cloth), ("curtain", SoundOccluderType.Cloth),
        ("snow", SoundOccluderType.Soft), ("hay", SoundOccluderType.Soft),
        ("truck", SoundOccluderType.Metal), ("engine", SoundOccluderType.Metal), ("vehicle", SoundOccluderType.Metal),
        ("pipe", SoundOccluderType.Metal), ("tube", SoundOccluderType.Metal), ("metal", SoundOccluderType.Metal),
        ("rock", SoundOccluderType.Heavy), ("stone", SoundOccluderType.Heavy), ("cliff", SoundOccluderType.Heavy),
        ("boulder", SoundOccluderType.Heavy), ("mountain", SoundOccluderType.Heavy), ("terrain", SoundOccluderType.Heavy),
        ("log", SoundOccluderType.Wood), ("crate", SoundOccluderType.Wood), ("table", SoundOccluderType.Wood),
        ("wood", SoundOccluderType.Wood), ("plank", SoundOccluderType.Wood), ("door", SoundOccluderType.Wood),
        ("bridge", SoundOccluderType.Wood), ("birdge", SoundOccluderType.Wood),
        ("house", SoundOccluderType.Wall), ("cabin", SoundOccluderType.Wall), ("wall", SoundOccluderType.Wall),
        ("building", SoundOccluderType.Wall)
    };

    private const int KeywordParentDepth = 4;

    // Destroyed colliders (the map regenerates) are never looked up again, so the cache is just
    // dropped once it grows past this instead of being pruned.
    private const int MaxCacheSize = 4096;

    private static readonly Dictionary<Collider, Amount> cache = new();
    private static readonly RaycastHit[] hits = new RaycastHit[16];

    public static void ClearCache() => cache.Clear();

    /// <summary>How much this one collider blocks sound. Cached per collider.</summary>
    public static Amount Classify(Collider collider)
    {
        if (cache.TryGetValue(collider, out Amount cached))
        {
            return cached;
        }

        if (cache.Count >= MaxCacheSize)
        {
            cache.Clear();
        }

        Amount amount = ClassifyUncached(collider);
        cache[collider] = amount;
        return amount;
    }

    private static Amount ClassifyUncached(Collider collider)
    {
        SoundOccluder occluder = collider.GetComponentInParent<SoundOccluder>();
        if (occluder != null)
        {
            return occluder.Amount;
        }

        if (collider is TerrainCollider)
        {
            return PresetFor(SoundOccluderType.Heavy);
        }

        Transform t = collider.transform;
        for (int depth = 0; t != null && depth < KeywordParentDepth; depth++, t = t.parent)
        {
            string name = t.name.ToLowerInvariant();
            foreach ((string word, SoundOccluderType type) in Keywords)
            {
                if (name.Contains(word))
                {
                    return PresetFor(type);
                }
            }
        }

        // Unknown object: judge by size. Small things barely matter, thin panels let some through.
        Vector3 size = collider.bounds.size;
        float largest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
        float smallest = Mathf.Min(size.x, Mathf.Min(size.y, size.z));
        if (largest < 1.5f)
        {
            return PresetFor(SoundOccluderType.Prop);
        }
        if (smallest < 0.25f)
        {
            return PresetFor(SoundOccluderType.Wood);
        }
        return PresetFor(SoundOccluderType.Wall);
    }

    /// <summary>Everything one straight line passes through, stacked: two thin walls block more
    /// than one. Colliders under either ignored root (the speaker, the listener) don't count.</summary>
    public static Amount Line(Vector3 from, Vector3 to, LayerMask mask, Transform ignoreA, Transform ignoreB)
    {
        Vector3 delta = to - from;
        float distance = delta.magnitude;
        if (distance < 0.01f)
        {
            return Amount.Clear;
        }

        int count = Physics.RaycastNonAlloc(from, delta / distance, hits, distance, mask, QueryTriggerInteraction.Ignore);
        float clearMuffle = 1f;
        float clearDampen = 1f;
        for (int i = 0; i < count; i++)
        {
            Collider collider = hits[i].collider;
            if (collider == null ||
                (ignoreA != null && collider.transform.IsChildOf(ignoreA)) ||
                (ignoreB != null && collider.transform.IsChildOf(ignoreB)))
            {
                continue;
            }

            Amount amount = Classify(collider);
            clearMuffle *= 1f - amount.Muffle;
            clearDampen *= 1f - amount.Dampen;
        }

        return new Amount(1f - clearMuffle, 1f - clearDampen);
    }

    /// <summary>Occlusion between a listener and a sound. Besides the direct line, three lines
    /// aim a little to each side of and above the sound, so standing half behind a corner or
    /// peeking over cover gives a partial muffle instead of flipping fully on or off.</summary>
    public static Amount Evaluate(Vector3 listener, Vector3 source, LayerMask mask, Transform ignoreA, Transform ignoreB,
        float spread = 0.6f)
    {
        Vector3 toSource = source - listener;
        Vector3 side = Vector3.Cross(Vector3.up, toSource);
        side = side.sqrMagnitude > 0.0001f ? side.normalized : Vector3.right;

        const float directWeight = 0.5f;
        const float sideWeight = (1f - directWeight) / 3f;

        Amount direct = Line(listener, source, mask, ignoreA, ignoreB);
        Amount left = Line(listener, source - side * spread, mask, ignoreA, ignoreB);
        Amount right = Line(listener, source + side * spread, mask, ignoreA, ignoreB);
        Amount up = Line(listener, source + Vector3.up * spread, mask, ignoreA, ignoreB);

        return new Amount(
            direct.Muffle * directWeight + (left.Muffle + right.Muffle + up.Muffle) * sideWeight,
            direct.Dampen * directWeight + (left.Dampen + right.Dampen + up.Dampen) * sideWeight);
    }
}

/// <summary>Drives a low-pass and a reverb filter next to an AudioSource from an occlusion
/// amount, easing toward it so a wall coming between never clicks the sound on or off.</summary>
public sealed class OcclusionFilter
{
    private const float ClearCutoff = 22000f;

    private readonly AudioLowPassFilter lowPass;
    private readonly AudioReverbFilter reverb;
    private readonly float muffledCutoff;

    public float Muffle { get; private set; }
    public float Dampen { get; private set; }

    /// <summary>Multiply the source's volume by this.</summary>
    public float VolumeMultiplier => 1f - Dampen;

    public OcclusionFilter(GameObject host, float muffledCutoff)
    {
        this.muffledCutoff = Mathf.Clamp(muffledCutoff, 10f, ClearCutoff);

        lowPass = host.GetComponent<AudioLowPassFilter>();
        if (lowPass == null) lowPass = host.AddComponent<AudioLowPassFilter>();

        reverb = host.GetComponent<AudioReverbFilter>();
        if (reverb == null) reverb = host.AddComponent<AudioReverbFilter>();

        // A small room, with its level driven below instead of switched on and off.
        reverb.reverbPreset = AudioReverbPreset.User;
        reverb.dryLevel = 0f;
        reverb.roomHF = -454f;
        reverb.roomLF = 0f;
        reverb.decayTime = 0.4f;
        reverb.decayHFRatio = 0.83f;
        reverb.reflectionsLevel = -1646f;
        reverb.reflectionsDelay = 0.002f;
        reverb.reverbLevel = 53f;
        reverb.reverbDelay = 0.003f;
        reverb.diffusion = 100f;
        reverb.density = 100f;

        Apply();
    }

    /// <summary>Eases toward target. blendSeconds is roughly how long a full change takes.</summary>
    public void Step(SoundOcclusion.Amount target, float blendSeconds, float deltaTime)
    {
        float k = 1f - Mathf.Exp(-deltaTime * 4f / Mathf.Max(0.01f, blendSeconds));
        Muffle = Mathf.Lerp(Muffle, target.Muffle, k);
        Dampen = Mathf.Lerp(Dampen, target.Dampen, k);
        Apply();
    }

    /// <summary>Jumps straight to target (no easing).</summary>
    public void Set(SoundOcclusion.Amount target)
    {
        Muffle = target.Muffle;
        Dampen = target.Dampen;
        Apply();
    }

    private void Apply()
    {
        if (lowPass == null || reverb == null)
        {
            return;
        }

        // Cutoff moves on a log scale so the muffle sounds even across the whole range.
        lowPass.enabled = Muffle > 0.001f;
        lowPass.cutoffFrequency = ClearCutoff * Mathf.Pow(muffledCutoff / ClearCutoff, Muffle);

        // The "other room" echo fades in with the muffle.
        float wet = Mathf.InverseLerp(0.2f, 1f, Muffle);
        reverb.enabled = wet > 0.001f;
        reverb.room = Mathf.Lerp(-6000f, -1000f, wet);
    }
}
