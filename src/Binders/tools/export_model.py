"""Export the arm and leg binders of "Arm and Leg Binders.blend" into the mod's runtime model (assets/binders.bnd) and its
colour texture (assets/binders_color.png, 1024 px).

Usage (Blender 5.x, headless):
    blender -b --disable-autoexec "<Arm and Leg Binders.blend>" --python mods/Binders/tools/export_model.py -- <out.bnd> <out.png>

The file holds two skinned meshes in their rest pose, "Arm_Binder" and "Leg_Binder" (one armature each, a cage of strap / side
bones that is not posed), each a left and a right sleeve: a cup closed at the bottom (z low; the elbow / knee goes in it) and
open at the top (shoulder / hip). The folded limb lies inside front to back: the arm sleeves are symmetric front / back, the leg
sleeves are straight at the front (y low, the thigh) and bulge backwards (the shin). Blender axes are kept (metres; x = her
left, y = back, z = up along the sleeve; the character faces -y). Every sleeve is one rigid part (vertices split by side, x > 0
= her left).

Format (little endian): "BNDR" u32 version=1
  u32 partCount, per part: u8 kind (0 arm, 1 leg), u8 side (0 her left, 1 her right),
      f32x3 bottom (centre of the cup's lowest ring), f32x3 top (centre of the opening's ring),
      u32 vertexCount, f32x3 positions[], f32x3 normals[], f32x2 uvs[], u32 indexCount, u32 indices[]
"""
import os
import struct
import sys

import bpy
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
out_path = os.path.abspath(args[0] if len(args) > 0 else "binders.bnd")
png_path = os.path.abspath(args[1] if len(args) > 1 else "binders_color.png")  # absolute: Blender resolves image paths against the .blend
TEXTURE = "Binders_Textures_Base_Color.png"
TEXTURE_SIZE = 1024
RING = 0.004  # m: vertices this close to the lowest / highest z make the bottom / top rings

dg = bpy.context.evaluated_depsgraph_get()
parts = []
for kind, name in ((0, "Arm_Binder"), (1, "Leg_Binder")):
    o = bpy.data.objects[name]
    e = o.evaluated_get(dg)
    m = e.to_mesh()
    m.calc_loop_triangles()
    mw = e.matrix_world
    nmat = mw.to_3x3().inverted().transposed()
    verts = [mw @ v.co for v in m.vertices]
    uv = m.uv_layers.active.data
    normals = m.corner_normals
    for side in (0, 1):
        def mine(i):
            return (verts[i].x > 0) == (side == 0)
        ids = [i for i in range(len(verts)) if mine(i)]
        zs = [verts[i].z for i in ids]
        z0, z1 = min(zs), max(zs)

        def ring_centre(sel):
            pts = [verts[i] for i in ids if sel(verts[i].z)]
            lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
            hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
            return (lo + hi) / 2
        bottom = ring_centre(lambda z: z < z0 + RING)
        top = ring_centre(lambda z: z > z1 - RING)
        pos, nrm, uvs, idx, index_of = [], [], [], [], {}
        for tri in m.loop_triangles:
            if not mine(tri.vertices[0]):
                continue
            if any(not mine(v) for v in tri.vertices):
                raise SystemExit("a triangle spans both sides")
            for li, v in zip(tri.loops, tri.vertices):
                n = (nmat @ normals[li].vector).normalized()
                t = uv[li].uv
                k = (v, round(n.x, 4), round(n.y, 4), round(n.z, 4), round(t.x, 5), round(t.y, 5))
                if k not in index_of:
                    index_of[k] = len(pos)
                    pos.append(verts[v])
                    nrm.append(n)
                    uvs.append((t.x, t.y))
                idx.append(index_of[k])
        parts.append((kind, side, bottom, top, pos, nrm, uvs, idx))
        print("BND-CHECK %s side %d: bottom %s top %s, %d vertices, %d triangles" % (
            name, side, tuple(round(c, 4) for c in bottom), tuple(round(c, 4) for c in top), len(pos), len(idx) // 3))
    e.to_mesh_clear()


def v3(f, v):
    f.write(struct.pack("<3f", v[0], v[1], v[2]))


with open(out_path, "wb") as f:
    f.write(b"BNDR")
    f.write(struct.pack("<I", 1))
    f.write(struct.pack("<I", len(parts)))
    for kind, side, bottom, top, pos, nrm, uvs, idx in parts:
        f.write(struct.pack("<2B", kind, side))
        v3(f, bottom)
        v3(f, top)
        f.write(struct.pack("<I", len(pos)))
        for p in pos:
            v3(f, p)
        for n in nrm:
            v3(f, n)
        for t in uvs:
            f.write(struct.pack("<2f", t[0], t[1]))
        f.write(struct.pack("<I", len(idx)))
        f.write(struct.pack("<%dI" % len(idx), *idx))
print("BND-CHECK wrote %s: %d parts" % (out_path, len(parts)))

img = bpy.data.images[TEXTURE]
img.scale(TEXTURE_SIZE, TEXTURE_SIZE)
img.filepath_raw = png_path
img.file_format = "PNG"
img.save()
print("BND-CHECK wrote %s (%d px)" % (png_path, TEXTURE_SIZE))
