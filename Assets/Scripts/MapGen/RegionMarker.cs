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
            // The colours live in TerrainPalette so they can be edited on the MapGenerator.
            return TerrainPalette.Default.ColourFor(region);
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
