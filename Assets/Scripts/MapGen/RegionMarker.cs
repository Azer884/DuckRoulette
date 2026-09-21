using UnityEngine;

namespace DuckRoulette.MapGen
{
    /// <summary>
    /// Marks the centre of a generated region and records what kind of ground it is.
    ///
    /// The whole map is snow, so "snowy" is not a description of anything. Each region gets a
    /// different snow state instead, and that state is what tells the player where they are:
    /// deep dry powder reads as untouched high ground, trodden packed snow reads as a camp,
    /// grey slush reads as the polluted outflow. The colour here is the one the terrain is
    /// painted with, so the marker and the ground always agree.
    /// </summary>
    public class RegionMarker : MonoBehaviour
    {
        public RegionType region = RegionType.None;

        [Tooltip("Rough radius of the region in world units, from its cell count.")]
        public float radius = 10f;

        [Tooltip("Cells the region owns.")]
        public int cellCount;

        [Tooltip("What the snow is doing here. Set by the generator; read by audio, footstep " +
                 "effects and anything else that reacts to ground type.")]
        public string snowType;

        /// <summary>
        /// Snow colours, indexed by <see cref="RegionType"/>.
        ///
        /// Every one of them is white. The map is a snowfield, so hue would be a lie - what changes
        /// between regions is the shade: how much light the snow is giving back, and the faintest
        /// shift towards warm or cold. Deep untouched powder is the brightest thing on the map, the
        /// outflow yard is the dullest, and everything else sits between them. That ordering is what
        /// the player reads, not the colour.
        /// </summary>
        static readonly Color[] SnowColours =
        {
            new Color(0.80f, 0.80f, 0.81f),   // None
            new Color(0.82f, 0.87f, 0.92f),   // River - wet ice, the coolest white
            new Color(0.86f, 0.86f, 0.85f),   // FrozenMarsh - dull crust, faintly warm
            new Color(0.70f, 0.69f, 0.67f),   // PipeYard - dirty snow, the dullest white
            new Color(0.89f, 0.88f, 0.87f),   // Camp - trodden, slightly warm
            new Color(0.94f, 0.95f, 0.96f),   // Clearing - swept hardpack, bright and neutral
            new Color(1.00f, 1.00f, 1.00f),   // Highground - dry powder, the brightest white
            new Color(0.82f, 0.84f, 0.86f),   // Woodland - shaded snow, faintly cool
            new Color(0.55f, 0.55f, 0.56f),   // Mountains - bare rock
        };

        static readonly string[] SnowTypes =
        {
            "None",
            "Wet ice and open water",
            "Crusted snow over frozen reeds",
            "Soot-stained slush and melt puddles",
            "Trodden packed snow with tracks",
            "Swept, wind-polished hardpack",
            "Deep dry powder and sastrugi",
            "Shaded snow with pine litter",
            "Bare rock and snow caps",
        };

        public static Color ColourFor(RegionType region)
        {
            int index = (int)region;
            return index >= 0 && index < SnowColours.Length ? SnowColours[index] : Color.white;
        }

        public static string SnowTypeFor(RegionType region)
        {
            int index = (int)region;
            return index >= 0 && index < SnowTypes.Length ? SnowTypes[index] : "Snow";
        }

        void OnDrawGizmos()
        {
            Gizmos.color = ColourFor(region);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
