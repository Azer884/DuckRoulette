using UnityEngine;

namespace DuckRoulette.MapGen
{
    /// <summary>
    /// Every colour and shading amount the ground bake uses, editable on the MapGenerator in the
    /// inspector. After changing it, press <b>Repaint terrain</b> (or Generate) to rebake.
    ///
    /// The defaults keep the whole map inside a narrow band of white: regions differ by a few
    /// percent of brightness and the faintest warm or cool shift, which is enough to read where you
    /// are without the ground turning grey.
    /// </summary>
    [System.Serializable]
    public class TerrainPalette
    {
        [Header("Region snow")]
        public Color river = new Color(0.90f, 0.94f, 0.98f);
        public Color frozenMarsh = new Color(0.93f, 0.93f, 0.92f);
        public Color pipeYard = new Color(0.91f, 0.90f, 0.89f);
        public Color camp = new Color(0.95f, 0.94f, 0.93f);
        public Color clearing = new Color(0.97f, 0.975f, 0.98f);
        public Color highground = new Color(1.00f, 1.00f, 1.00f);
        public Color woodland = new Color(0.92f, 0.935f, 0.95f);
        public Color mountains = new Color(0.88f, 0.885f, 0.895f);
        [Tooltip("Cells outside every region. Normally none.")]
        public Color unassigned = new Color(0.94f, 0.94f, 0.94f);

        [Header("Pipe yard pollution")]
        [Tooltip("Paint the stains around the pipe outfalls at all.")]
        public bool paintPollution = true;

        [Tooltip("Outer ring of the stain, where the pollution is light.")]
        public Color pollutionLight = new Color(0.86f, 0.855f, 0.84f);

        [Tooltip("Centre of the stain, right at the outfall.")]
        public Color pollutionHeavy = new Color(0.80f, 0.79f, 0.77f);

        [Tooltip("Pollution level (0-1) where the light stain starts.")]
        [Range(0f, 1f)] public float pollutionLightThreshold = 0.25f;

        [Tooltip("Pollution level (0-1) where the heavy stain starts.")]
        [Range(0f, 1f)] public float pollutionHeavyThreshold = 0.6f;

        [Header("Shading")]
        [Tooltip("Brightness of the lowest height band. 1 turns the terraced height bands off.")]
        [Range(0.8f, 1f)] public float lowGroundBrightness = 0.97f;

        [Tooltip("Tint laid over hollows.")]
        public Color hollowTint = new Color(0.92f, 0.95f, 1.00f);
        [Range(0f, 1f)] public float hollowStrength = 0.4f;

        [Tooltip("Tint laid over steep, wind-scoured ground.")]
        public Color slopeTint = new Color(0.90f, 0.905f, 0.915f);
        [Range(0f, 1f)] public float slopeStrength = 0.4f;

        [Tooltip("Brightness multiplier for the large drift patches. 1 turns them off.")]
        [Range(1f, 1.1f)] public float driftBrightness = 1.02f;

        [Tooltip("Final multiplier on everything, so the brightest snow is not blown out under " +
                 "the directional light.")]
        [Range(0.7f, 1f)] public float exposure = 0.96f;

        [Header("Texture")]
        [Tooltip("Size of the baked ground texture in pixels along the longer side. Higher keeps " +
                 "region borders sharp up close. 2048 is about 4cm per pixel on the default map; " +
                 "4096 costs four times the memory and bake time.")]
        public int textureSize = 2048;

        public Color ColourFor(RegionType region)
        {
            switch (region)
            {
                case RegionType.River: return river;
                case RegionType.FrozenMarsh: return frozenMarsh;
                case RegionType.PipeYard: return pipeYard;
                case RegionType.Camp: return camp;
                case RegionType.Clearing: return clearing;
                case RegionType.Highground: return highground;
                case RegionType.Woodland: return woodland;
                case RegionType.Mountains: return mountains;
                default: return unassigned;
            }
        }

        /// <summary>Default palette, for anything that has no MapGenerator to read one from.</summary>
        public static readonly TerrainPalette Default = new TerrainPalette();
    }
}
