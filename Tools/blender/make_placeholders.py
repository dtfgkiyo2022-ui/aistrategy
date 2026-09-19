# Generates low-poly placeholder models (infantry, scout, core) and exports FBX for Unity.
# Run: blender.exe --background --python Tools/blender/make_placeholders.py -- <output_dir>
import bpy, sys, math, os

out = sys.argv[sys.argv.index("--") + 1] if "--" in sys.argv else "."
os.makedirs(out, exist_ok=True)

def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)

def add(kind, name, loc, scale, rot=(0, 0, 0), **kw):
    if kind == "cube": bpy.ops.mesh.primitive_cube_add(location=loc, rotation=rot)
    elif kind == "cone": bpy.ops.mesh.primitive_cone_add(location=loc, rotation=rot, vertices=kw.get("v", 8), radius1=1, depth=2)
    elif kind == "cyl": bpy.ops.mesh.primitive_cylinder_add(location=loc, rotation=rot, vertices=kw.get("v", 8), radius=1, depth=2)
    elif kind == "sphere": bpy.ops.mesh.primitive_uv_sphere_add(location=loc, rotation=rot, segments=8, ring_count=6, radius=1)
    o = bpy.context.active_object
    o.name = name
    o.scale = scale
    return o

def export(name, parts):
    bpy.ops.object.select_all(action="DESELECT")
    for p in parts: p.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    obj = bpy.context.active_object
    obj.name = name
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    bpy.ops.export_scene.fbx(filepath=os.path.join(out, name + ".fbx"), use_selection=True,
                             apply_scale_options="FBX_SCALE_UNITS", bake_space_transform=True,
                             axis_forward="-Z", axis_up="Y", mesh_smooth_type="FACE")

# Units are about 1 m wide, pivot at ground level (Unity view lifts nothing: pivot centered at feet).
reset()
export("Infantry", [
    add("cyl", "body", (0, 0, 0.55), (0.28, 0.28, 0.35)),
    add("sphere", "head", (0, 0, 1.15), (0.2, 0.2, 0.2)),
    add("cube", "shield", (0.32, 0, 0.6), (0.05, 0.3, 0.32)),
    add("cyl", "spear", (-0.32, 0, 0.9), (0.03, 0.03, 0.9), v=6),
])
reset()
export("Scout", [
    add("cone", "body", (0, 0, 0.45), (0.25, 0.25, 0.4), rot=(0, math.pi / 2, 0), v=6),
    add("sphere", "head", (0.3, 0, 0.7), (0.15, 0.15, 0.15)),
])
reset()
export("Core", [
    add("cube", "base", (0, 0, 0.6), (3.6, 3.6, 0.6)),
    add("cyl", "tower", (0, 0, 2.4), (1.6, 1.6, 1.2), v=8),
    add("cone", "roof", (0, 0, 4.2), (2.0, 2.0, 0.6), v=8),
    add("cube", "gate", (3.61, 0, 0.9), (0.05, 0.8, 0.9)),
])
print("Wrote placeholders to", out)
