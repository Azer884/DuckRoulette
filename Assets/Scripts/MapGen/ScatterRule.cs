using System.Collections.Generic;
using UnityEngine;

namespace DuckRoulette.MapGen
{
    /// <summary>
    /// What a prop is there to do. The role drives spacing, because in a hide-and-seek map the
    /// distance between two objects is a design decision, not a dressing decision.
    /// </summary>
    public enum PropRole
    {
        /// <summary>Dressing. Blocks sightlines a bit, but nobody hides inside it.</summary>
        Decor,

        /// <summary>A place a hider can break line of sight and stay: log, snow pile, truck.</summary>
        Cover,

        /// <summary>Something the player has to stop and interact with, which makes noise and time.</summary>
        Challenge,

        /// <summary>A structure you navigate by: igloo, tent, pipe outfall, cabin.</summary>
        Landmark,
    }

    /// <summary>
    /// One entry in the map's scatter table.
    ///
    /// Adding content to the map should not mean writing code. A rule says what to spawn, how many
    /// of it, which regions it is allowed in, how big it should end up and how far it has to stay
    /// from everything else. Drop a prefab into a new rule and it is in the map.
    /// </summary>
    [System.Serializable]
    public class ScatterRule
    {
        [Tooltip("Name shown in logs. Has no effect on placement.")]
        public string label = "New rule";

        public bool enabled = true;

        [Tooltip("Prefabs to pick from. One is chosen at random per instance, so variants of the " +
                 "same thing go in the same rule.")]
        public GameObject[] prefabs;

        [Tooltip("Regions this rule is allowed to spawn in. Leave empty to allow the whole map " +
                 "except the river.")]
        public List<RegionType> regions = new List<RegionType>();

        [Tooltip("How many to place: x is the minimum, y the maximum, both inclusive.")]
        public Vector2Int count = new Vector2Int(3, 6);

        [Tooltip("What this prop is for. Drives the spacing rules in MapPopulator.")]
        public PropRole role = PropRole.Decor;

        [Tooltip("Longest side of the placed prop, in cells. Zero keeps the prefab's own scale.")]
        public float sizeTarget = 3f;

        public Vector2 scaleJitter = new Vector2(0.9f, 1.15f);

        [Tooltip("Extra clearance around this prop's footprint, in cells. Nothing else is allowed " +
                 "inside footprint + padding, which is what stops props intersecting.")]
        public float footprintPadding = 0.6f;

        [Tooltip("Extra minimum distance between two instances of this same rule, in cells. Use it " +
                 "to spread a family out (trees look natural clumped, igloos do not).")]
        public float selfSpacing = 0f;

        [Tooltip("Rejected on ground steeper than this, in normalised height per cell.")]
        public float maxSlope = 0.25f;

        [Tooltip("Keeps the prop this many cells away from the map edge, so it never ends up " +
                 "inside the border ridge or a mountain.")]
        public int edgeMargin = 10;

        [Tooltip("Rotates the prop to sit flush on sloped ground. Off for anything that should " +
                 "stay vertical, like trees and tents.")]
        public bool alignToGround;

        [Tooltip("Random lean, in degrees. Ignored when Align To Ground is on.")]
        public float tiltDegrees = 5f;

        [Tooltip("Pushed up or down after being placed on the ground, in world units. Negative " +
                 "sinks the prop, which is how snow piles and half-buried pipes are done.")]
        public float yOffset;

        [Tooltip("Faces the prop away from the nearest map edge instead of a random direction. " +
                 "For things that come out of the boundary wall, like the pipe outfall.")]
        public bool faceAwayFromEdge;

        [Tooltip("Prefers cells close to the map edge. Combined with Face Away From Edge this is " +
                 "what pins the pipe outfall to the boundary.")]
        public bool hugEdge;

        [Tooltip("Snaps the prop to exactly this many cells from the nearest map edge, whatever " +
                 "cell it was sampled at. Zero leaves the sampled position alone. This is how the " +
                 "pipe outfall ends up with its wall buried in the mountains instead of standing " +
                 "in open snow a few cells short of them.")]
        public float edgeSnap;

        [Tooltip("Stains the snow around this prop, in cells. The pipe outfall uses it to create " +
                 "the polluted ground it drains onto.")]
        public float pollutionRadius;

        [Tooltip("Flattens the ground under the prop before placing it, in cells. For anything with " +
                 "a flat base that would otherwise float or sink on a slope.")]
        public float flattenRadius;

        [Tooltip("Lifts the prop so the bottom of its mesh rests on the ground. For models whose " +
                 "pivot is at their centre, which would otherwise end up half buried.")]
        public bool sitOnBase;

        [Tooltip("Turns the prop so its local +Z faces the middle of the region it stands in. For " +
                 "structures with a door, so the entrance faces open ground instead of a cliff.")]
        public bool faceRegionCentre;

        [Tooltip("Keeps this many cells clear in front of the prop's local +Z, so nothing else is " +
                 "placed in front of a door. Zero reserves nothing.")]
        public float frontClearance;

        [Tooltip("After placing, slides the prop back towards the nearest map edge until its back " +
                 "meets the rising ground of the border wall, and keeps it level. For pipes that " +
                 "should come out of the wall rather than stand in front of it. Implies Face Away " +
                 "From Edge.")]
        public bool snapToWall;

        [Tooltip("How much of the prop's depth is pushed into the wall when Snap To Wall is on.")]
        [Range(0f, 0.9f)]
        public float wallBury = 0.35f;

        [Tooltip("Gives the prop a Rigidbody (and a convex collider if it has none), so players can " +
                 "knock it around. Physics props are not marked static.")]
        public bool physicsBody;

        [Tooltip("Rigidbody mass per cubic world unit of the prop's bounds, when Physics Body is on.")]
        public float physicsDensity = 20f;

        [Tooltip("Adds a MeshCollider built from the mesh when the prefab has no collider of its " +
                 "own. Non-convex, so an igloo's door and inside stay walkable.")]
        public bool addMeshCollider;

        /// <summary>Picks an instance count for this run.</summary>
        public int RollCount(System.Random rng)
        {
            int low = Mathf.Min(count.x, count.y);
            int high = Mathf.Max(count.x, count.y);
            return rng.Next(low, high + 1);
        }

        public GameObject PickPrefab(System.Random rng)
        {
            if (prefabs == null || prefabs.Length == 0)
            {
                return null;
            }

            return prefabs[rng.Next(prefabs.Length)];
        }

        public bool Allows(RegionType region)
        {
            if (region == RegionType.River || region == RegionType.Mountains)
            {
                return false;
            }

            if (regions == null || regions.Count == 0)
            {
                return true;
            }

            return regions.Contains(region);
        }
    }
}
