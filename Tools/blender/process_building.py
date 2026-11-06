# Tripo の建物の模型をゲーム用にする（docs/art-direction.md の 4）。
# 使い方:
#   blender -b --factory-startup --python Tools/blender/process_building.py -- <入力.fbx|.glb> <出力フォルダ> <名前> [近い版の三角形数] [遠い版の三角形数]
# 出力: <名前>.fbx（近い版）、<名前>_LOD1.fbx（遠い版）、<名前>_BaseColor.png・<名前>_Normal.png（1024px）、
#       <出力フォルダ>/../preview_<名前>.png（確認用の絵）。底の中心が原点、Y が上。
import bpy, sys, os, math, mathutils

args = sys.argv[sys.argv.index("--") + 1:]
src, out_dir, name = args[0], args[1], args[2]
near_tris = int(args[3]) if len(args) > 3 else 15000
far_tris = int(args[4]) if len(args) > 4 else 6000
os.makedirs(out_dir, exist_ok=True)


def load():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    if src.lower().endswith((".glb", ".gltf")):
        bpy.ops.import_scene.gltf(filepath=src)
    else:
        bpy.ops.import_scene.fbx(filepath=src)
    meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    for o in bpy.context.scene.objects:
        o.select_set(o in meshes)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    obj = bpy.context.view_layer.objects.active
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return obj


def tri_count(o):
    o.data.calc_loop_triangles()
    return len(o.data.loop_triangles)


def reduce(obj, target):
    # 何段かに分けて減らすと形が崩れにくい
    while tri_count(obj) > target * 1.05:
        before = tri_count(obj)
        mod = obj.modifiers.new("dec", 'DECIMATE')
        mod.decimate_type = 'COLLAPSE'
        mod.ratio = max(target / tri_count(obj), 0.1)
        mod.use_collapse_triangulate = True
        bpy.ops.object.modifier_apply(modifier=mod.name)
        # 葉の多い木などは、それ以上減らせない所で止まる（同じ数のまま回り続けないように）
        if tri_count(obj) > before * 0.98:
            print("STUCK", before, "->", tri_count(obj))
            break
    for p in obj.data.polygons:
        p.use_smooth = True


def to_base_origin(obj):
    vs = obj.data.vertices
    xs = [v.co.x for v in vs]; ys = [v.co.y for v in vs]; zs = [v.co.z for v in vs]
    off = mathutils.Vector(((min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2, min(zs)))
    for v in vs:
        v.co -= off
    return (max(xs) - min(xs), max(ys) - min(ys), max(zs) - min(zs))


def export(obj, file):
    obj.name = os.path.splitext(os.path.basename(file))[0]
    bpy.ops.export_scene.fbx(filepath=file, use_selection=True, apply_scale_options='FBX_SCALE_UNITS',
                             axis_forward='-Z', axis_up='Y', path_mode='STRIP', embed_textures=False,
                             mesh_smooth_type='FACE')


def save_textures(obj):
    mat = obj.data.materials[0] if obj.data.materials else None
    if mat is None or not mat.use_nodes:
        return
    for node in mat.node_tree.nodes:
        if node.type != 'TEX_IMAGE' or node.image is None:
            continue
        low = node.image.name.lower()
        kind = "BaseColor" if ("basecolor" in low or "base_color" in low or "albedo" in low or "diffuse" in low) \
            else "Normal" if "normal" in low else None
        # Tripo の画像は名前に種類が無い（tripo_image_xxx_0.jpg）ので、どこにつながっているかで決める
        if kind is None:
            for link in node.outputs["Color"].links:
                if link.to_node.type == 'NORMAL_MAP':
                    kind = "Normal"
                elif link.to_socket.name == "Base Color":
                    kind = "BaseColor"
        if kind is None:
            continue
        img = node.image
        if img.size[0] > 1024:
            img.scale(1024, 1024)
        img.filepath_raw = os.path.join(out_dir, name + "_" + kind + ".png")
        img.file_format = 'PNG'
        img.save()
        print("TEX", kind, img.size[0], img.size[1])


def preview(file):
    scene = bpy.context.scene
    engines = [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items]
    scene.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in engines else 'BLENDER_EEVEE'
    scene.render.resolution_x = 900; scene.render.resolution_y = 700
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); scene.collection.objects.link(cam)
    cam.location = (1.1, -1.1, 1.0); cam.rotation_euler = (math.radians(58), 0, math.radians(45))
    scene.camera = cam
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", 'SUN')); scene.collection.objects.link(sun)
    sun.rotation_euler = (math.radians(40), math.radians(10), math.radians(-30)); sun.data.energy = 4
    world = bpy.data.worlds.new("w"); scene.world = world; world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.8
    scene.render.filepath = file
    bpy.ops.render.render(write_still=True)


# 遠い版を先に作って書き出し、読み直して近い版を作る（元の模型から別々に減らす）
obj = load(); before = tri_count(obj)
reduce(obj, far_tris); to_base_origin(obj)
export(obj, os.path.join(out_dir, name + "_LOD1.fbx"))
obj = load()
reduce(obj, near_tris); size = to_base_origin(obj)
save_textures(obj)
export(obj, os.path.join(out_dir, name + ".fbx"))
print("TRIS", before, "->", tri_count(obj), "/", far_tris)
print("SIZE", tuple(round(s, 3) for s in size))
# 正規化した大きさで撮る（1 x 1 の台に収める）
scale = 1.0 / max(size[0], size[1])
obj.scale = (scale, scale, scale)
preview(os.path.join(out_dir, "..", "preview_" + name + ".png"))
