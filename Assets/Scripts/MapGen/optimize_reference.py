"""Optimize the reference map FBX: decimate the geometry and give everything a real name.

MapAlpha1.fbx is 678 MB and 13.9M vertices, almost all of it in twelve trees that carry
1.07M vertices each. Unity imports it, but it is unusable as a working reference: the import
is slow, the scene is heavy, and every object is called Cube.001 or Trunk.007.

This pass:
  * renames every object after what it actually is, using the source names as a lookup;
  * decimates each mesh to a polygon budget scaled to how large the object is on screen;
  * drops leftover empties and zero-geometry objects;
  * exports Assets/Models/MapAlpha1_Optimized.fbx.

Run headless:  blender -b --python optimize_reference.py
"""

import bpy
import json
import os
import re
import time

SOURCE = r"C:\Users\azerh\UnityProjects\DuckRoulette(Map)\Assets\MapAlpha1.fbx"
OUT_DIR = r"C:\Users\azerh\UnityProjects\DuckRoulette(Map)\Assets\Models"
OUT_FBX = os.path.join(OUT_DIR, "MapAlpha1_Optimized.fbx")
REPORT = os.path.join(OUT_DIR, "MapAlpha1_Optimized.json")

# Polygon budgets by size band. Big silhouette objects keep more detail than scatter props.
BUDGET_LARGE = 40000     # anything over 200k polys: the mountain ring, the rock formations
BUDGET_MEDIUM = 6000     # 20k-200k polys: trees, big rocks
BUDGET_SMALL = 2500      # under 20k polys: small props
KEEP_BELOW = 400         # tiny meshes are left alone

# Source name -> readable name. Numbered families get a running index.
RENAME_EXACT = {
    "Cube.001": "MountainRing",
    "Plane.019": "GroundPlane",
    "Pallet Homes.002": "House_Pallet",
    "Rock 03": "Rock_Photoscan",
    "Stylized Rough Landscape Rock": "Rock_Large",
    "undercarriage": "Truck_Undercarriage",
    "Cylinder": "RockFormation_A",
    "Cylinder.012": "RockFormation_B",
    "Cylinder.011": "Prop_Barrel",
    "Cylinder.019": "Prop_Silo",
}

RENAME_PATTERNS = [
    (re.compile(r"^Trunk"), "Tree_Pine"),
    (re.compile(r"^Stylized Rough Landscape Rock"), "Rock"),
    (re.compile(r"^Wheel"), "Truck_Wheel"),
    (re.compile(r"^Cylinder"), "Prop_Post"),
    (re.compile(r"^Circle"), "Prop_Ring"),
    (re.compile(r"^Plane"), "Prop_Panel"),
    (re.compile(r"^Cube"), "Prop_Crate"),
    (re.compile(r"^Pallet Homes"), "House_Pallet"),
    (re.compile(r"^Garden bamboo bridge"), "Bridge_Bamboo"),
    (re.compile(r"^Pinzgauer"), "Truck"),
    (re.compile(r"^Photoscanned Rock"), "Rock_Photoscan"),
    (re.compile(r"^Vert"), "Prop_Marker"),
]


def log(message):
    print("[optimize] %s" % message, flush=True)


def target_name(source_name, counters):
    if source_name in RENAME_EXACT:
        return RENAME_EXACT[source_name]

    for pattern, base in RENAME_PATTERNS:
        if pattern.match(source_name):
            counters[base] = counters.get(base, 0) + 1
            return "%s_%02d" % (base, counters[base])

    counters["Prop"] = counters.get("Prop", 0) + 1
    return "Prop_%02d" % counters["Prop"]


def budget_for(polygon_count):
    if polygon_count > 200000:
        return BUDGET_LARGE
    if polygon_count > 20000:
        return BUDGET_MEDIUM
    return BUDGET_SMALL


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    start = time.time()
    bpy.ops.import_scene.fbx(filepath=SOURCE)
    log("imported %d objects in %.1fs" % (len(bpy.data.objects), time.time() - start))

    before_verts = sum(len(o.data.vertices) for o in bpy.data.objects if o.type == 'MESH')
    before_polys = sum(len(o.data.polygons) for o in bpy.data.objects if o.type == 'MESH')

    # Empties carry nothing once the meshes are flattened out of their hierarchy.
    for obj in list(bpy.data.objects):
        if obj.type != 'MESH':
            bpy.data.objects.remove(obj, do_unlink=True)
            continue
        if len(obj.data.polygons) == 0:
            bpy.data.objects.remove(obj, do_unlink=True)

    counters = {}
    entries = []

    # One object at a time: decimate it, apply immediately, move on. Applying every modifier
    # in one pass at the end crashes Blender on a file this size, and doing it per object
    # also lets the freed memory come back as we go.
    bpy.ops.object.select_all(action='DESELECT')

    for obj in sorted(bpy.data.objects, key=lambda o: o.name):
        polygons = len(obj.data.polygons)
        source = obj.name
        new_name = target_name(source, counters)

        budget = budget_for(polygons)
        ratio = 1.0
        if polygons > KEEP_BELOW and polygons > budget:
            ratio = budget / float(polygons)
            modifier = obj.modifiers.new(name="Decimate", type='DECIMATE')
            modifier.decimate_type = 'COLLAPSE'
            modifier.ratio = ratio
            modifier.use_collapse_triangulate = True

            bpy.context.view_layer.objects.active = obj
            obj.select_set(True)
            try:
                bpy.ops.object.modifier_apply(modifier=modifier.name)
            except RuntimeError as error:
                log("could not apply decimate to %s: %s" % (source, error))
                obj.modifiers.remove(modifier)
            obj.select_set(False)

        obj.name = new_name
        obj.data.name = new_name
        entries.append({
            "source": source,
            "name": new_name,
            "polysBefore": polygons,
            "polysAfter": len(obj.data.polygons),
            "ratio": round(ratio, 4),
        })
        log("%-34s -> %-22s %7d -> %6d" % (source, new_name, polygons, len(obj.data.polygons)))

    after_verts = sum(len(o.data.vertices) for o in bpy.data.objects)
    after_polys = sum(len(o.data.polygons) for o in bpy.data.objects)

    os.makedirs(OUT_DIR, exist_ok=True)
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.export_scene.fbx(
        filepath=OUT_FBX,
        use_selection=True,
        object_types={'MESH'},
        apply_unit_scale=True,
        mesh_smooth_type='FACE',
        use_mesh_modifiers=True,
        axis_forward='-Z',
        axis_up='Y',
    )

    report = {
        "objects": len(entries),
        "vertsBefore": before_verts,
        "vertsAfter": after_verts,
        "polysBefore": before_polys,
        "polysAfter": after_polys,
        "entries": entries,
    }
    with open(REPORT, "w") as handle:
        json.dump(report, handle, indent=1)

    log("verts %d -> %d (%.1f%%)" % (before_verts, after_verts, 100.0 * after_verts / max(1, before_verts)))
    log("polys %d -> %d" % (before_polys, after_polys))
    log("wrote %s" % OUT_FBX)


main()
