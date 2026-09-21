# Duck Roulette — snow map design

The map is a hide-and-seek arena that is snow from edge to edge. That is the central design
problem: uniform white gives the player nothing to navigate by, nothing to read cover from, and
no sense of having moved. So the map is not "snowy" as one material — every region is a
different **state of snow**, and that state is doing three jobs at once: it says where you are,
it says what the ground does to you, and it says what kind of cover to expect.

## Design pillars

1. **Read the ground.** From any point you should be able to name the region you are in from the
   snow alone, without a HUD. Every region is white; what changes is the shade, and the shades
   bleed into each other so there is never a drawn line between two of them.
2. **Cover is a resource, not a wall.** Hiding places are limited, spaced, and directional —
   every one of them can be walked around.
3. **Every destination costs you.** A challenge takes time and makes noise. Going for one is a
   bet, not a free pickup.
4. **One map, many routes.** Bridge, ramp and paths make loops. There is never a single lane
   that decides a round.
5. **Dressing still blocks sightlines.** Trees and reeds have no gameplay contract, but they are
   placed at densities that matter to vision.

Values marked `[PLACEHOLDER]` are hypotheses to be playtested, not settled numbers.

---

## The regions

Seven regions plus the mountain wall. Each is a weighted Voronoi cell around a seed placed for
its role, so the layout changes per seed but the relationships do not.

### 1. River — the divide

| | |
|---|---|
| **Snow / ground** | Wet ice and open water. The coolest, least bright white on the map. |
| **Terrain** | A channel cut to −1.6 with the water surface at −0.65. The bed is a single smooth curve from floor to rim, so the shore is wherever it crosses the water plane — no radius, no clamped land, no stair-stepped wall. |
| **Contains** | 1 bridge, 1 outfall tube on its own ground apron. |
| **Player experience** | A barrier with one dry crossing. Crossing anywhere else means going through the water in the open, on the only dark ground on a white map. |

The **bridge** is the only dry crossing. Exactly one, at the middle of the spline, aimed along
the local flow and turned across it, sized from the measured water width so it lands on dry bank
at both ends. One crossing is deliberate: it is a chokepoint the seeker can watch, which is what
makes the crossing worth watching.

The **outfall tube** sits at the head of the channel, sized to the full channel width and turned
to face back up the river. It raises its own ground apron, so it beds into a bank instead of
standing over open water. It is scenery, not cover — it exists so the water level has something
to read against.

### 2. Frozen marsh — the soft edge

| | |
|---|---|
| **Snow / ground** | Crusted snow over frozen reeds. Grey-white, dirty, patchy — the crust breaks and shows the brown underneath. |
| **Terrain** | Sunk `marshDepth` towards the waterline, so it is the low ground next to the river. |
| **Contains** | Reeds ×25–45, small logs, hiding logs ×2–5 (shared with the woodland), pine trees at low density. |
| **Player experience** | Bad sightlines in every direction, but nothing solid to hide behind. You feel hidden and are not. |

The marsh is the map's transition piece: it softens the jump from open water to closed woodland,
and it is where a hider who just got off the river goes next.

### 3. Pipe yard — the dirty end

| | |
|---|---|
| **Snow / ground** | Soot-stained slush and melt puddles. The dullest white on the map, and it dulls further the closer you are to an outfall. |
| **Terrain** | Flattened low and left broken. Short sightlines by design. |
| **Contains** | 1–2 pipe outfalls, 1 truck engine (challenge), beer crate, snow piles, rocks. |
| **Player experience** | Somewhere you are not supposed to be. Cramped, dirty, and the only place where the map edge is a built wall instead of a mountain. |

The **pipe outfall** is a pipe coming out of a concrete wall with the sludge pool it drains into.
The wall is part of the prefab on purpose — a pipe standing alone in open snow makes no sense.
The rule that places it snaps it to five cells from the map edge (`edgeSnap`) and faces it
inwards, so the wall backs into the mountain feet and reads as part of the boundary, and it is the only rule that writes to the **pollution map**: a 14-cell radius of stained
snow that the terrain texture darkens. That stain is what makes the yard feel like an outflow
rather than a place where someone happened to leave a pipe.

### 4. Camp — the inhabited place

| | |
|---|---|
| **Snow / ground** | Trodden packed snow with tracks. Warmer grey than the rest of the map, scuffed, clearly walked on. |
| **Terrain** | A flat pad wide enough for the structures, with the rolling ground around it left alone. |
| **Contains** | 1 cabin, 2–3 tents, 1 campfire, 1 truck (cover), 1 truck engine or beer crate (challenge), pine trees. |
| **Player experience** | The only place that looks lived in. Lots of hard cover, lots of reasons for the seeker to come and check. |

The **campfire** is the map's only warm light. That makes it a strong landmark and a bad place to
hide next to — which is exactly the tension the cover-to-challenge spacing is protecting.

The **truck** is the biggest single hiding spot on the map. It is placed as `Cover`, so the
spacing rules keep it well away from the smaller cover; without that it would create one corner
where a hider has three overlapping options and never has to move.

### 5. Clearing — the arena

| | |
|---|---|
| **Snow / ground** | Swept, wind-polished hardpack. Bright, flat, almost featureless. |
| **Terrain** | Levelled. The most open ground on the map. |
| **Contains** | 1 blackjack table (challenge), snow piles, rocks. Deliberately little else. |
| **Player experience** | Exposed. You can see everything and everything can see you. |

The **blackjack table** sits in the middle of it. Putting the headline challenge in the most
open region is the whole point: you can see the seeker coming, and they can see you standing at
the table, and you have to decide whether to finish.

### 6. Highground — the bluff

| | |
|---|---|
| **Snow / ground** | Deep dry powder and wind-carved sastrugi. The brightest white on the map. |
| **Terrain** | A plateau with a flat core and a steep skirt, lifted about 9 units above the map average, with **one** ramp. |
| **Contains** | 2–4 igloos, snow piles, rocks, thin pine cover. |
| **Player experience** | The best sightlines in the game and one way in. Powerful to hold, terrible to be caught on. |

The **igloos** give the top structures to fight around rather than a bare platform, and they are
spread with `selfSpacing` so they read as a settlement rather than a cluster. One ramp is a
design decision, not a limitation: a plateau with four approaches is just a hill.

### 7. Woodland — the connective tissue

| | |
|---|---|
| **Snow / ground** | Shaded snow with pine litter. Blue in the shadows, uneven drifts around the trunks. |
| **Terrain** | Left as the raw noise. Rolling, no flattening. |
| **Contains** | Pine trees ×45–70 (the bulk of them), hiding logs ×2–5, small logs, snow piles, rocks, beer crate. |
| **Player experience** | The default state of the map. Broken sightlines, plenty of cover, nothing worth defending. |

The woodland is what the other regions are measured against. It is where a hider goes when they
have no plan.

### 8. Mountains — the wall

Rebuilt from the reference model's own hand-made ring, cut into 40 tiles and reassembled in two
staggered rows (see `README.md`). Not playable: four box colliders at the map edge do the
blocking, and the mountains are the view.

---

## Object roles and spacing

Every prop is placed by a `ScatterRule` and carries a `PropRole`. The role decides how much room
it needs, because in hide-and-seek the distance between two objects **is** the balance number.

| Role | What it means | Spacing rule |
|---|---|---|
| `Cover` | A hider can break line of sight and stay: truck, hiding log, snow pile. | 14 cells to other cover `[PLACEHOLDER]` |
| `Challenge` | You stop, it takes time, it makes noise: blackjack table, truck engine, beer crate. | 26 cells to other challenges `[PLACEHOLDER]` |
| `Landmark` | A structure you navigate by: igloo, tent, cabin, pipe outfall, campfire. | 12 cells to other landmarks |
| `Decor` | Dressing. Blocks sight, nobody hides inside it. | Footprint only |
| | Cover next to a challenge | 9 cells minimum `[PLACEHOLDER]` |

**Why 14 cells between hiding spots.** Roughly two seconds of running. Close enough that a
spotted hider has somewhere to rotate to; far enough that one sweep past a snow pile does not
also clear the log behind it. Tune this first — it is the single number that decides whether
rounds end too fast or drag.

**Why 26 cells between challenges.** Challenges are destinations. If two sit in the same part of
the map, the seeker camps one spot and covers both, and half the map goes unused.

**Why 9 cells from cover to a challenge.** A challenge pulls the seeker in and makes noise.
Cover right next to one would let a hider bait and hide in the same square metre.

**Nothing intersects.** On top of the role rules, every placed prop records the radius of its own
footprint, and a candidate is rejected unless it clears every existing footprint plus
`generalClearance`. The river's bridge and outfall are reserved before the scatter runs, so
nothing ends up standing inside them either.

---

## Adding your own objects

No code. Select `MapGen`, open **Populator > Scatter table**, add a rule:

| Field | What it does |
|---|---|
| `prefabs` | One is picked at random per instance — put variants of the same thing in one rule. |
| `regions` | Which regions it may spawn in. Empty means anywhere except the river and mountains. |
| `count` | Inclusive range, rolled per seed. `(2, 5)` means two to five. |
| `role` | Drives the spacing table above. |
| `sizeTarget` | Longest side of the placed prop, in cells. Prefabs come in at wildly different scales; this normalises them. Zero keeps the prefab's own scale. |
| `selfSpacing` | Extra distance between instances of this rule. Trees look right clumped, igloos do not. |
| `flattenRadius` | Levels the ground under the prop first. Use for anything with a flat base. |
| `alignToGround` | Lies the prop on the slope. Off for anything that should stay vertical. |
| `hugEdge` / `faceAwayFromEdge` | Pins the prop to the map boundary and turns it inwards. |
| `pollutionRadius` | Stains the snow around it. |

Rules run top to bottom and earlier rules get first pick of space, so keep landmarks and cover
above dressing.

The defaults are written by **Tools > Duck Roulette > Set Up Map Generator**, in
`MapSetupTool.BuildDefaultRules()`. Editing that method changes the map's content design; editing
the inspector tunes one scene.

---

## Placeholders

`Tools > Duck Roulette > Create Placeholder Prefabs` builds stand-ins out of primitives in
`Assets/Prefabs/Placeholders`: igloo, tent, campfire, truck, truck engine, blackjack table, beer
crate, pipe outfall, snow pile, hiding log, small log, pine tree, reeds, wall section.

They are crude but correctly proportioned, because footprint and silhouette are what the scatter
rules and the spacing maths work with. Swapping in the real model later changes nothing else:
point the rule's `prefabs` at the new asset and the sizing, spacing and collision all still hold.

The cabin uses the existing `Assets/House.prefab`, the bridge `Assets/Birdge.prefab`, and the
river outfall `Assets/Tube 1.prefab`.

---

## Open questions for playtest

- Is 14 cells between hiding spots right for the duck's run speed? Measure the actual traversal
  time before tuning anything else.
- Does the clearing need a second piece of cover, or is "completely exposed" the point?
- Should the bridge be destructible or blockable, given it is the only dry crossing?
- Four to seven snow piles across five regions may be too thin once the seeker knows the map.
