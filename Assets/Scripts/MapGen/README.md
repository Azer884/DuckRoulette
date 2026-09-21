# Procedural map

One seed produces the whole map: terrain, river, regions, landmarks, cover and the mountain
wall around the edge. `MapGenerator` on the `MapGen` object owns it; everything it creates goes
under a child object called `Generated`, which is wiped and rebuilt on every run.

## Generating

* Inspector: select `MapGen`, then **Generate**, **Reroll seed** or **Clear**. The inspector
  also draws a region preview coloured by region and shaded by height.
* Menu: **Tools > Duck Roulette > Set Up And Generate** rewires the prefab and mountain-piece
  references from disk and then generates. Run this after re-cutting the mountains.
* Play mode: `Generate On Start` builds the map when the scene starts.

The seed is the `seed` string in `noiseSettings`. The same string always gives the same map.

**`Randomise Seed On Start`** rolls a fresh seed every time the game starts, so each launch is a
different map. Leave it off while tuning — with it on nothing is reproducible, and a bug you just
saw is gone on the next run. `RerollSeed()` is public, so round-based code can change the map
between rounds without touching anything else. It uses a GUID rather than a timestamp: two
launches inside the same second would otherwise get the same map, which is the first case anyone
testing the toggle hits.

## Build order

The order is the design, not an implementation detail.

1. **Noise** lays down the raw ground.
2. **River** is carved first, because it crosses everything and every later decision needs to
   know where it runs. Banks are dragged down in a wide soft pass, then the channel is cut into
   the bottom of that valley, then dry land is lifted back out of the water.
3. **Regions** are laid out around the river. Each region is placed for its role, not at random,
   then a weighted Voronoi partition with noisy borders assigns every cell.
4. **Terrain shaping** gives each region the silhouette its role needs: the clearing is levelled,
   the bluff is raised into a plateau with one ramp, the marsh is sunk, the camp gets a flat pad.
5. **Paths** are carved between region centres as a minimum spanning tree plus two shortcut
   links, so the map is connected and has loops rather than dead ends.
6. **Border ridge** goes up at the map edge.
7. **Bridge and outfall** are placed, because they are fixed points the scatter has to work
   around, and they are reserved with the populator so nothing else lands on them.
8. **Scatter table** fills the regions, rejecting any position whose footprint touches something
   already placed.
9. **Ground mesh** is built only now, because scatter rules may flatten the ground they stand on.
10. **Mountains** go on last, standing on the finished border ridge.

## Regions

| Region | Role |
|---|---|
| River | Splits the map. One dry crossing; everywhere else costs you cover. |
| Frozen marsh | Low ground along the water. Reeds and logs, bad sightlines, weak cover. |
| Pipe yard | Against a map edge. The pipe outfall and the polluted ground it drains onto. |
| Camp | Cabin, tents, campfire and the truck. The only inhabited-looking place. |
| Clearing | Centre of the map. Open arena holding the blackjack table. |
| Highground | A plateau with one ramp and two to four igloos. Best sightlines on the map. |
| Woodland | Connective tissue. Trees, logs and snow piles between the landmarks. |

Full per-region design, contents and the reasoning behind the counts and spacing are in
[`MAP_DESIGN.md`](MAP_DESIGN.md).

## The river

The channel floor is cut to `riverValue` and the water surface sits `waterDepth` above it, so
there is a real strip of open water with dry banks either side.

**The shoreline is derived from height, not from a radius.** The valley pass drags the ground
down to just above the waterline; the channel pass then cuts a single smooth cosine from the
floor at the centre to nothing at the rim, with no flat section and no hard break. Where that
bed crosses the water plane is the shore. `TerrainShaper.ResolveWater` flood-fills from the
river's own cells to decide which cells are water, and fills in any hollow elsewhere on the map
that ended up below the surface but is not connected to the river.

This replaced an earlier approach that marked water by radius and clamped the neighbouring land
to a minimum height. That gives a hard boolean edge meeting a clamped plateau, which renders as
a vertical, stair-stepped wall along the whole river. A bed that passes through the waterline at
a shallow angle does not.

Every pass after the carve — region pads, paths, the border ridge, the smoothing pass — stamps
ground without knowing where the water is, and the border ridge in particular walls off both
ends of the river. So the channel is **re-cut** once the terrain is otherwise finished
(`ReCarveChannel`), which is cheap, only ever lowers ground, and is what keeps the water running
continuously from one map edge to the other. The waterline is then resolved from the final
heights.

Exactly one **bridge** and one **outfall tube** are placed, by the river generator rather than
the scatter table, because their positions are defined by the water. The outfall is sized to the
full channel width (`outfallMatchRiverWidth`), turned to face back up the river
(`outfallYawOffset`, 180), and positioned by its *bottom* rather than its pivot so it sits on the
waterline instead of half-buried in the bed. It walks back along the spline until it is clear of
both the border ridge (`outfallInset`) and the bridge. The bridge is aimed with
its forward along the local flow at the middle of the spline, which puts its long horizontal
axis across the water; `bridgeYawOffset` is there for models built the other way round. Note
that it is scaled on its longest *horizontal* side, not its longest side — the bridge model is
as tall as it is long, and sizing on the overall longest side shrinks the span to fit the
railings.

The bridge measures the water under it (`bridgeSpansBanks`) and sizes itself to land on dry
ground at both ends plus `bridgeBankOverlap`, times `bridgeSpanScale`, instead of using a fixed
span that either stops short in the water or overshoots into the trees. The deck sits at the
**lower** of the two banks minus `bridgeDeckSink`, so both ends bed into the ground — taking the
higher bank leaves the other end hanging.

The outfall sits at the river's **source**, walking forward from the first spline point to the
first position `outfallInset` cells in from the map edge. That inset is smaller than half the
tube's own length on purpose: the back of the tube stays buried in the boundary wall and only
its mouth shows, so the river reads as running out of it. `outfallOffset` nudges it in the
tube's **own local axes** (Z along the tube, X across it), not world axes, so the nudge means
the same thing whichever way the river happens to point on a given seed.

The outfall raises ground under itself (`outfallGroundRadius`, `outfallGroundRise`), because it
sits over the channel and would otherwise hang in the air. It raises **abutments either side of
the flow**, not a pad: a solid pad would dam the river, so the strip the water runs through is
left open and the river passes under the tube. Raising those abutments changes the shoreline, so
the waterline is resolved once more after the river props are placed.

## Content

There are no hiding-spot or challenge behaviour scripts. This is terrain generation plus prop
placement: props are spawned by the scatter table with a `PropRole` that drives their spacing,
and the gameplay behaviour attached to them later is a separate job.

* `ScatterRule` — one entry in the scatter table: prefabs, count range, allowed regions, size,
  spacing, slope limit and so on. Adding content means adding a rule, not editing code. See
  `MAP_DESIGN.md` for the full field list and the default table.
* `RegionMarker` — one per region, at its centre, carrying the region type, cell count, radius
  and the name of its snow type. Spawn logic, minimap, footsteps and ambience read these rather
  than re-running the partition, and it also owns the white-shade palette the ground is painted
  with.

There are no behaviour scripts on the generated content at all: the generator produces terrain,
regions and placed props, and nothing else.

## Snow types

The whole map is snow, so the snow is doing the navigation work. Each region is painted with a
different snow state, and `RegionMarker.snowType` names it for anything that reacts to ground:

| Region | Snow |
|---|---|
| River | Wet ice and open water |
| Frozen marsh | Crusted snow over frozen reeds |
| Pipe yard | Soot-stained slush and melt puddles |
| Camp | Trodden packed snow with tracks |
| Clearing | Swept, wind-polished hardpack |
| Highground | Deep dry powder and sastrugi |
| Woodland | Shaded snow with pine litter |
| Mountains | Bare rock and snow caps |

## The mountains

The mountains in `MapAlpha1.fbx` are one hand-made mesh (object `Cube.001`, ~102k polygons)
wrapped around the reference map. Noise cannot reproduce that silhouette, so it is reused
directly: a Blender script cuts it into a tile library and Unity rebuilds a ring from the tiles.

`Assets/Models/MountainPieces/` holds the result — 32 wall slabs (16 cuts, each also mirrored)
and 8 corner blocks (4 corners, each also mirrored), plus three rocks lifted from the same file.
`pieces.json` records each piece's length, depth, height and polygon count.

Every piece is normalised to one frame: it runs along local +X, the body of the mountain extends
towards local +Z (so the map lies on -Z), and the origin sits mid-length on the inner face at
ground level. `MountainRingBuilder` then walks
the four map edges dropping pieces end to end and puts a corner block on each corner.

Four things keep the result from reading as tiling:

* pieces are **cut** with overlap and **placed** with overlap (`overlap`, default 0.42), so
  neighbours interpenetrate and no two pieces share a silhouette edge;
* each piece is picked at random from the library, mirrors included, and gets yaw, scale and
  height jitter;
* the ring is built in **two rows** (`rows`), the back one further out and larger. The source
  mountains are spiky pinnacles, so a single row leaves daylight between the peaks;
* the bases are sunk below the border ridge, so no piece ever shows its cut bottom.

Two orientation details are worth knowing if the pieces are ever re-exported. Blender writes
the FBX Z-up, so `pieceRotationOffset` (-90 on X) stands each piece up, and the piece's local
scale axes are still Blender's: Y is depth, Z is height. `pieceScale` is recomputed by the
setup tool from the actual imported bounds, so it does not need to be guessed.

Collision is four box colliders at the map edge, not mesh colliders on the mountains — far
cheaper and it cannot leak through a gap between two pieces.

### Re-cutting the mountains

The cutting script is `cut_mountains.py` (kept with the map tooling). Run it headless:

```
blender -b --python cut_mountains.py
```

Tunables at the top of the script: `SLABS_PER_SIDE` (how many cuts per map edge),
`SLAB_OVERLAP` (how much neighbouring slabs share), `CORNER_SPAN` (how much of the diagonal a
corner block covers) and `MAX_FACE_SPAN` (drops the handful of map-spanning polygons in the
source mesh — without it three pieces come out with meaningless bounds).

Afterwards run **Tools > Duck Roulette > Set Up And Generate**, which re-finds the pieces and
recomputes `pieceScale` so roughly four slabs cover each map edge.

## Prop scaling and collision

Prefabs arrive at wildly different scales — the tube prefab is hundredths of a unit across while
the house is tens. Rather than fixing each prefab, every rule has a `sizeTarget` and the
populator rescales each instance so its longest side matches it. Zero keeps the prefab's own
scale.

Nothing intersects: each placed prop records the radius of the circle that contains its
footprint — half the diagonal, not half the longest side, because props are placed at a random
yaw — and a candidate is rejected unless it clears every existing footprint plus
`generalClearance`. On top of that, `PropRole` pairs have design distances (see
[`MAP_DESIGN.md`](MAP_DESIGN.md)).

## Ground texture

The map is snow from edge to edge, so the ground is where all the navigation information has to
live. Colouring the regions is not an option - a green or a red patch in a snowfield reads as a
bug - so every region gets its own **shade of white** instead.

The rest of the game is cel shaded (Unity Toon Shader: flat colour, one hard shadow step), so
the ground is painted the same way: a handful of flat shades with crisp, smoothly curving edges,
and no fine surface detail. An earlier version layered drift noise, wind ripples, sparkle and a
detail normal map on top; it looked like a photo under a cartoon and was removed.

`paintRegionTexture` bakes `Assets/Generated/RegionMap.png` and puts it on
`Assets/Generated/TerrainRegions.mat`. That material is reset from the toon template
`Assets/Materials/Terrain/Terrain.mat` (`Toon/Toon`) on every bake, so the ground shades like
the props. No normal map is baked; lighting comes from the mesh normals. The terrain mesh
carries 0..1 UVs across the grid, so the texture lines up cell for cell.

The shades run from the brightest thing on the map to the dullest: deep powder on the highground,
then the swept clearing, the camp, the marsh, the shaded woodland, the wet ice of the river, and
the dirty snow of the pipe yard. That ordering is what the player reads, not the colour. The
palette lives in `RegionMarker.SnowColours`.

**Curved borders.** The region map holds one value per cell, so picking the nearest cell's
region per texel makes every border a staircase. Instead each region gets its own weight mask,
blurred, and every texel takes the flat colour of the strongest region at a domain-warped
sample point. Borders come out as clean, gently wobbling curves.

On top of that, each switched on by a threshold rather than blended in: three terraced height
bands, one cool tint for real hollows (curvature smoothed first so small dips don't speckle),
one scoured grey for steep ground, a few big patches of brighter drift, and two flat soot tones
around the pipe outfalls.

**Perlin's lattice.** Unity's Perlin noise sits on an axis-aligned lattice and returns the same
value at every integer coordinate. Sampled at the low frequencies this bake wants it shows up as
large rectangular blocks with hard vertical and horizontal edges. `RotatedFbm` samples each field
on a domain rotated by an odd angle, which turns those edges off the axes.

## The optimized reference

`optimize_reference.py` decimates `MapAlpha1.fbx` and renames every object after what it
actually is, writing `Assets/Models/MapAlpha1_Optimized.fbx` plus a JSON report of what each
object was and what it became.

```
blender -b --python optimize_reference.py
```

The source file is 678 MB and 13.9M vertices, almost all of it twelve trees at 1.07M vertices
each, and every object named `Cube.001` or `Trunk.007`. The optimized file is 58 MB and 786k
vertices — 5.7% of the original — with names like `MountainRing`, `Tree_Pine_04`, `House_Pallet`
and `Truck_Wheel_02`. Polygon budgets are per size band (`BUDGET_LARGE/MEDIUM/SMALL`) so the
mountain ring keeps its silhouette while the scatter props lose the detail nobody sees.

The decimate modifiers are applied one object at a time. Applying them all in one pass at the
end crashes Blender on a file this size.

## Known placeholders

* Igloo, tent, campfire, truck, truck engine, blackjack table, beer crate, pipe outfall, snow
  pile, hiding log, small log, pine tree, reeds and wall section are all primitive stand-ins
  from **Tools > Duck Roulette > Create Placeholder Prefabs**.
* The cabin uses `Assets/House.prefab`, the bridge `Assets/Birdge.prefab`, and the river outfall
  `Assets/Tube 1.prefab`.
* The mountain pieces import with the FBX's own materials. Assign the project's terrain/rock
  materials to them once and every generated ring picks them up.
