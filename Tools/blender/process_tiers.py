# Tripo の元の模型から、画質の中・高・最高の模型と絵を作る（docs/settings.md の 2.1）。低は process_building.py の今のファイル。
# 使い方:
#   blender -b --factory-startup --python Tools/blender/process_tiers.py -- <入力.fbx|.glb> <出力フォルダ> <名前> <building|resource>
# 出力（<出力フォルダ>/）:
#   <名前>_U.fbx・<名前>_U_LOD1.fbx（最高）、<名前>_H.fbx・<名前>_H_LOD1.fbx（高）、<名前>_M.fbx・<名前>_M_LOD1.fbx（中）
#   <名前>_BaseColor2k.jpg・<名前>_Normal2k.png（中・高・最高で共有）、<名前>_BaseColor4k.jpg（建物の最高だけ）
# 一度読み込んだ元の模型を、細かい順に少しずつ減らしながら書き出す（1 回の読み込みで全部の段）。底の中心が原点、Y が上。
import bpy, sys, os

args = sys.argv[sys.argv.index("--") + 1:]
src, out_dir, name, kind = args[0], args[1], args[2], args[3]
os.makedirs(out_dir, exist_ok=True)

# (三角形の数, 書き出す名前の後ろ) を細かい順に。同じ数は 1 回減らして 2 つ書き出す
if kind == "building":
    STEPS = [(120000, ["_U"]), (60000, ["_H"]), (30000, ["_M", "_U_LOD1"]), (20000, ["_H_LOD1"]), (12000, ["_M_LOD1"])]
    COLOR_4K = True
else:
    STEPS = [(30000, ["_U"]), (15000, ["_H"]), (8000, ["_U_LOD1", "_M"]), (5000, ["_H_LOD1"]), (3000, ["_M_LOD1"])]
    COLOR_4K = False

# process_building.py と同じ読み込み・減らし方・書き出し（低と同じ向きになるように）


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
    while tri_count(obj) > target * 1.05:
        before = tri_count(obj)
        mod = obj.modifiers.new("dec", 'DECIMATE')
        mod.decimate_type = 'COLLAPSE'
        mod.ratio = max(target / tri_count(obj), 0.1)
        mod.use_collapse_triangulate = True
        bpy.ops.object.modifier_apply(modifier=mod.name)
        if tri_count(obj) > before * 0.98:
            print("STUCK", before, "->", tri_count(obj))
            break
    for p in obj.data.polygons:
        p.use_smooth = True


def base_offset(obj):
    import mathutils
    vs = obj.data.vertices
    xs = [v.co.x for v in vs]; ys = [v.co.y for v in vs]; zs = [v.co.z for v in vs]
    return mathutils.Vector(((min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2, min(zs)))


def export(obj, file, offset):
    # 原点は一番細かい版で決めた位置に固定する（段ごとにずれないように）。書き出しの間だけ動かす
    for v in obj.data.vertices:
        v.co -= offset
    obj.name = os.path.splitext(os.path.basename(file))[0]
    bpy.ops.export_scene.fbx(filepath=file, use_selection=True, apply_scale_options='FBX_SCALE_UNITS',
                             axis_forward='-Z', axis_up='Y', path_mode='STRIP', embed_textures=False,
                             mesh_smooth_type='FACE')
    for v in obj.data.vertices:
        v.co += offset


def save_image(img, file, fmt, size):
    if img.size[0] > size:
        img.scale(size, size)
    scene = bpy.context.scene
    # save_render は表示用の色変換（AgX など）をかけるので、色が変わらない Standard にする
    scene.view_settings.view_transform = 'Standard'
    scene.view_settings.look = 'None'
    scene.view_settings.exposure = 0.0
    scene.view_settings.gamma = 1.0
    scene.render.image_settings.file_format = fmt
    if fmt == 'JPEG':
        scene.render.image_settings.quality = 92
    else:
        scene.render.image_settings.color_mode = 'RGB'
        scene.render.image_settings.compression = 90
    img.save_render(file, scene=scene)
    print("TEX", os.path.basename(file), img.size[0], img.size[1])


def save_textures(obj):
    mat = obj.data.materials[0] if obj.data.materials else None
    if mat is None or not mat.use_nodes:
        return
    color = normal = None
    for node in mat.node_tree.nodes:
        if node.type != 'TEX_IMAGE' or node.image is None:
            continue
        low = node.image.name.lower()
        k = "BaseColor" if ("basecolor" in low or "base_color" in low or "albedo" in low or "diffuse" in low) \
            else "Normal" if "normal" in low else None
        if k is None:
            for link in node.outputs["Color"].links:
                if link.to_node.type == 'NORMAL_MAP':
                    k = "Normal"
                elif link.to_socket.name == "Base Color":
                    k = "BaseColor"
        if k == "BaseColor":
            color = node.image
        elif k == "Normal":
            normal = node.image
    if color is not None:
        if COLOR_4K:
            save_image(color, os.path.join(out_dir, name + "_BaseColor4k.jpg"), 'JPEG', 4096)
        save_image(color, os.path.join(out_dir, name + "_BaseColor2k.jpg"), 'JPEG', 2048)
    if normal is not None:
        save_image(normal, os.path.join(out_dir, name + "_Normal2k.png"), 'PNG', 2048)


obj = load(); before = tri_count(obj)
save_textures(obj)
offset = None
for target, suffixes in STEPS:
    reduce(obj, target)
    if offset is None:
        offset = base_offset(obj)
    for suffix in suffixes:
        export(obj, os.path.join(out_dir, name + suffix + ".fbx"), offset)
    print("STEP", target, "->", tri_count(obj), suffixes)
print("TRIS", before)
