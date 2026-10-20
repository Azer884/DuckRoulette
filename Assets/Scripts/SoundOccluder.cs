using UnityEngine;

/// <summary>What a collider is made of, as far as sound passing through it is concerned.</summary>
public enum SoundOccluderType
{
    Wall,      // building walls: heavy muffle, half the volume
    Heavy,     // rock, cliffs, terrain: almost nothing gets through
    Wood,      // logs, crates, doors, tables
    Metal,     // vehicles, pipes, engines
    Glass,     // windows: muffled, barely quieter
    Soft,      // snow piles, hay: muffles more than it blocks
    Cloth,     // tents, curtains
    Foliage,   // trees, bushes, reeds
    Fence,     // fences, rails, grates: sound mostly passes
    Prop,      // small loose props
    None       // never blocks sound
}

/// <summary>Overrides how much a collider (and every collider under it) blocks voice chat and
/// the boombox. Objects without one are classified automatically by SoundOcclusion from their
/// collider type, name and size, so this is only needed where the guess is wrong.</summary>
[DisallowMultipleComponent]
public class SoundOccluder : MonoBehaviour
{
    [SerializeField] private SoundOccluderType type = SoundOccluderType.Wall;

    [SerializeField, Tooltip("Use the values below instead of the type's preset.")]
    private bool customValues;

    [SerializeField, Range(0f, 1f), Tooltip("How much high frequency is cut (0 = clear, 1 = fully muffled).")]
    private float muffle = 1f;

    [SerializeField, Range(0f, 1f), Tooltip("How much volume is lost (0 = none, 1 = silent).")]
    private float dampen = 0.5f;

    /// <summary>The preset used when no custom values are set. Settable so generated map
    /// geometry can be tagged from code.</summary>
    public SoundOccluderType Type
    {
        get => type;
        set
        {
            type = value;
            SoundOcclusion.ClearCache();
        }
    }

    public SoundOcclusion.Amount Amount =>
        customValues ? new SoundOcclusion.Amount(muffle, dampen) : SoundOcclusion.PresetFor(type);

    private void OnValidate()
    {
        SoundOcclusion.ClearCache();
    }
}
