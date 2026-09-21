"""Cut the reference map's mountain ring into a reusable tile library.

The ring (object "Cube.001" in MapAlpha1.fbx) is one continuous mesh that surrounds the
whole reference map. It is split into:

  * wall pieces  - straight-ish slabs taken from the four sides of the ring, cut with
                   overlap so that neighbouring slabs interpenetrate instead of meeting
                   at a visible seam;
  * corner pieces - blocks taken across each diagonal, used at the four map corners.

Every piece is rotated into one canonical frame (the piece runs along +X, the map is
towards +Y, the base sits at z = 0) and its origin is placed on the inner edge, so the
Unity side can drop a piece straight onto the map border with nothing but a yaw.

Mirrored copies are exported too, which doubles the library without needing negative
scale in Unity (negative scale flips triangle winding and breaks lighting).
"""

import bpy
import json
import math
import os
import time
from mathutils import Vector

FBX = r"C:\Users\azerh\UnityProjects\DuckRoulette(Map)\Assets\MapAlpha1.fbx"
OUT_DIR = r"C:\Users\azerh\UnityProjects\DuckRoulette(Map)\Assets\Models\MountainPieces"
MANIFEST = os.path.join(OUT_DIR, "pieces.json")

MOUNTAIN_OBJECT = "Cube.001"
ROCK_EXPORTS = {
    "Rock_Small": "Stylized Rough Landscape Rock.001",
    "Rock_Large": "Stylized Rough Landscape Rock",
    "Rock_Photoscan": "Rock 03",
}

SLABS_PER_SIDE = 4
SLAB_OVERLAP = 0.22      # fraction of a slab's width added to each end
CORNER_SPAN = 0.30       # how much of the diagonal a corner piece spans
MAX_FACE_SPAN = 80.0     # drop the few huge base/backing polygons that span the whole ring

os.makedirs(OUT_DIR, exist_ok=True)


def log(msg):
    print("[cut] %s" % msg, flush=True)


def import_reference():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    t0 = time.time()
    bpy.ops.import_scene.fbx(filepath=FBX)
    log("imported in %.1fs, %d objects" % (time.time() - t0, len(bpy.data.objects)))


def purge_all_but(keep_names):
    """Drop the heavy foliage so the rest of the script has room to work."""
    for obj in list(bpy.data.objects):
        if obj.name not in keep_names:
            bpy.data.objects.remove(obj, do_unlink=True)
    for mesh in list(bpy.data.meshes):
        if mesh.users == 0:
            bpy.data.meshes.remove(mesh)
    log("purged down to %d objects" % len(bpy.data.objects))


def read_source(obj):
    """World-space vertices, faces, per-face material index and per-loop UVs."""
    mesh = obj.data
    matrix = obj.matrix_world
    verts = [matrix @ v.co for v in mesh.vertices]

    uv_layer = mesh.uv_layers.active
    faces = []
    for poly in mesh.polygons:
        loop_uvs = []
        if uv_layer:
            for loop_index in poly.loop_indices:
                uv = uv_layer.data[loop_index].uv
                loop_uvs.append((uv[0], uv[1]))
        faces.append({
            "verts": list(poly.vertices),
            "material": poly.material_index,
            "uvs": loop_uvs,
        })

    materials = [m for m in mesh.materials]
    return verts, faces, materials


def face_centres(verts, faces):
    centres = []
    for face in faces:
        acc = Vector((0.0, 0.0, 0.0))
        for vi in face["verts"]:
            acc += verts[vi]
        centres.append(acc / len(face["verts"]))
    return centres


def build_piece(name, verts, faces, materials, face_indices, yaw_degrees, mirror):
    """Create a new object holding only the given faces, rotated into the canonical frame."""
    if not face_indices:
        log("skipped %s (no faces)" % name)
        return None

    yaw = math.radians(yaw_degrees)
    cos_y, sin_y = math.cos(yaw), math.sin(yaw)

    remap = {}
    new_verts = []
    for fi in face_indices:
        for vi in faces[fi]["verts"]:
            if vi not in remap:
                source = verts[vi]
                x = source.x * cos_y - source.y * sin_y
                y = source.x * sin_y + source.y * cos_y
                if mirror:
                    x = -x
                remap[vi] = len(new_verts)
                new_verts.append((x, y, source.z))

    new_faces = []
    for fi in face_indices:
        indices = [remap[vi] for vi in faces[fi]["verts"]]
        if mirror:
            indices.reverse()   # keep the winding consistent after the flip
        new_faces.append(indices)

    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(new_verts, [], new_faces)
    mesh.validate(verbose=False)

    for material in materials:
        mesh.materials.append(material)

    uv_layer = mesh.uv_layers.new(name="UVMap")
    loop_cursor = 0
    for poly, fi in zip(mesh.polygons, face_indices):
        poly.material_index = faces[fi]["material"]
        uvs = faces[fi]["uvs"]
        if mirror:
            uvs = list(reversed(uvs))
        for local_index, loop_index in enumerate(poly.loop_indices):
            if local_index < len(uvs):
                uv_layer.data[loop_index].uv = uvs[local_index]
        loop_cursor += poly.loop_total

    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)

    # Move the mesh so the origin sits mid-length on the inner edge, base at zero.
    xs = [v[0] for v in new_verts]
    ys = [v[1] for v in new_verts]
    zs = [v[2] for v in new_verts]
    shift = Vector(((min(xs) + max(xs)) * -0.5, -max(ys), -min(zs)))
    for vertex in mesh.vertices:
        vertex.co += shift

    mesh.calc_normals_split() if hasattr(mesh, "calc_normals_split") else None

    return {
        "object": obj,
        "name": name,
        "length": max(xs) - min(xs),
        "depth": max(ys) - min(ys),
        "height": max(zs) - min(zs),
        "faces": len(face_indices),
    }


def export_piece(info):
    path = os.path.join(OUT_DIR, info["name"] + ".fbx")
    bpy.ops.object.select_all(action='DESELECT')
    info["object"].select_set(True)
    bpy.context.view_layer.objects.active = info["object"]
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        object_types={'MESH'},
        apply_unit_scale=True,
        bake_space_transform=False,
        mesh_smooth_type='FACE',
        use_mesh_modifiers=True,
        axis_forward='-Z',
        axis_up='Y',
    )
    return path


def cut_mountains():
    src = bpy.data.objects[MOUNTAIN_OBJECT]
    verts, faces, materials = read_source(src)
    log("mountain ring: %d verts, %d faces" % (len(verts), len(faces)))

    xs = [v.x for v in verts]
    ys = [v.y for v in verts]
    zs = [v.z for v in verts]
    centre = Vector(((min(xs) + max(xs)) * 0.5, (min(ys) + max(ys)) * 0.5, 0.0))
    for i, v in enumerate(verts):
        verts[i] = v - centre

    centres = [c - centre for c in face_centres([v + centre for v in verts], faces)]

    # A handful of polygons in the source ring span the entire map (the flat underside and a
    # backing skirt). They are invisible from inside the map but they wreck every piece's
    # bounds, so the origin and the placement length end up meaningless. Drop them.
    oversized = set()
    for fi, face in enumerate(faces):
        points = [verts[vi] for vi in face["verts"]]
        span = max(max(p[axis] for p in points) - min(p[axis] for p in points) for axis in range(3))
        if span > MAX_FACE_SPAN:
            oversized.add(fi)
    log("dropping %d oversized faces" % len(oversized))
    log("ring extent x %.1f..%.1f  y %.1f..%.1f  z %.1f..%.1f"
        % (min(xs), max(xs), min(ys), max(ys), min(zs), max(zs)))

    half_x = (max(xs) - min(xs)) * 0.5
    half_y = (max(ys) - min(ys)) * 0.5

    # Side bands: a face belongs to the side whose axis dominates its offset from centre.
    bands = {"N": [], "S": [], "E": [], "W": []}
    for fi, c in enumerate(centres):
        if fi in oversized:
            continue
        if abs(c.y) >= abs(c.x):
            bands["N" if c.y > 0 else "S"].append(fi)
        else:
            bands["E" if c.x > 0 else "W"].append(fi)
    log("band sizes " + ", ".join("%s=%d" % (k, len(v)) for k, v in bands.items()))

    # Each side is rotated so that it runs along +X with the map towards +Y.
    band_yaw = {"N": 180.0, "S": 0.0, "E": -90.0, "W": 90.0}
    band_axis = {"N": "x", "S": "x", "E": "y", "W": "y"}

    pieces = []
    for band, face_indices in bands.items():
        axis = band_axis[band]
        values = [getattr(centres[fi], axis) for fi in face_indices]
        lo, hi = min(values), max(values)
        step = (hi - lo) / SLABS_PER_SIDE
        pad = step * SLAB_OVERLAP

        for slab in range(SLABS_PER_SIDE):
            slab_lo = lo + slab * step - pad
            slab_hi = lo + (slab + 1) * step + pad
            selected = [fi for fi in face_indices
                        if slab_lo <= getattr(centres[fi], axis) <= slab_hi]
            name = "Mtn_Wall_%s%d" % (band, slab)
            info = build_piece(name, verts, faces, materials, selected, band_yaw[band], False)
            if info:
                info["kind"] = "wall"
                pieces.append(info)
            mirrored = build_piece(name + "_M", verts, faces, materials, selected,
                                   band_yaw[band], True)
            if mirrored:
                mirrored["kind"] = "wall"
                pieces.append(mirrored)

    # Corner pieces straddle each diagonal so the corner reads as one mass, not two walls.
    corner_yaw = {"NE": -135.0, "SE": -45.0, "SW": 45.0, "NW": 135.0}
    corner_sign = {"NE": (1, 1), "SE": (1, -1), "SW": (-1, -1), "NW": (-1, 1)}
    for corner, (sx, sy) in corner_sign.items():
        selected = []
        for fi, c in enumerate(centres):
            if fi in oversized:
                continue
            if c.x * sx <= 0 or c.y * sy <= 0:
                continue
            # Normalised position along the diagonal: 0 on one axis, 1 on the other.
            nx = abs(c.x) / half_x
            ny = abs(c.y) / half_y
            if abs(nx - ny) <= CORNER_SPAN and max(nx, ny) > 0.45:
                selected.append(fi)
        name = "Mtn_Corner_%s" % corner
        info = build_piece(name, verts, faces, materials, selected, corner_yaw[corner], False)
        if info:
            info["kind"] = "corner"
            pieces.append(info)
        mirrored = build_piece(name + "_M", verts, faces, materials, selected,
                               corner_yaw[corner], True)
        if mirrored:
            mirrored["kind"] = "corner"
            pieces.append(mirrored)

    return pieces


def export_rocks():
    exported = []
    for export_name, source_name in ROCK_EXPORTS.items():
        obj = bpy.data.objects.get(source_name)
        if obj is None:
            log("rock source missing: %s" % source_name)
            continue
        obj.name = export_name
        obj.data.name = export_name
        obj.location = (0, 0, 0)
        obj.rotation_euler = (0, 0, 0)

        bpy.ops.object.select_all(action='DESELECT')
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.origin_set(type='ORIGIN_GEOMETRY', center='BOUNDS')

        # These props are photogrammetry-dense; a collapse keeps them usable as scatter.
        modifier = obj.modifiers.new(name="Decimate", type='DECIMATE')
        modifier.ratio = min(1.0, 6000.0 / max(1, len(obj.data.polygons)))

        path = os.path.join(OUT_DIR, export_name + ".fbx")
        bpy.ops.export_scene.fbx(
            filepath=path, use_selection=True, object_types={'MESH'},
            apply_unit_scale=True, mesh_smooth_type='FACE', use_mesh_modifiers=True,
            axis_forward='-Z', axis_up='Y')
        exported.append(export_name)
        log("exported rock %s" % export_name)
    return exported


def main():
    import_reference()
    keep = {MOUNTAIN_OBJECT} | set(ROCK_EXPORTS.values())
    purge_all_but(keep)

    pieces = cut_mountains()
    bpy.data.objects.remove(bpy.data.objects[MOUNTAIN_OBJECT], do_unlink=True)

    manifest = {"pieces": []}
    for info in pieces:
        export_piece(info)
        manifest["pieces"].append({
            "name": info["name"],
            "kind": info["kind"],
            "length": round(info["length"], 3),
            "depth": round(info["depth"], 3),
            "height": round(info["height"], 3),
            "faces": info["faces"],
        })
        log("exported %s  len=%.1f depth=%.1f height=%.1f faces=%d"
            % (info["name"], info["length"], info["depth"], info["height"], info["faces"]))

    manifest["rocks"] = export_rocks()
    with open(MANIFEST, "w") as handle:
        json.dump(manifest, handle, indent=1)
    log("DONE %d pieces" % len(manifest["pieces"]))


main()
