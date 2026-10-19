"""Procedural right hand for Lab 03.

Run:  blender -b --factory-startup -P build_hand.py -- [--out DIR] [--previews]

Builds a box-modelled quad mesh (palm grid, four extruded fingers, extruded thumb,
wrist), subdivides it, marks palm/back seams and unwraps it, bakes a skin texture,
rigs it with a 16-bone armature, creates the grab/trigger poses and exports FBX.
"""
import bpy
import bmesh
import math
import os
import sys
import numpy as np
from mathutils import Vector, Matrix, Quaternion

ARGV = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = ARGV[ARGV.index("--out") + 1] if "--out" in ARGV else HERE
PREVIEWS = "--previews" in ARGV
CM = 0.01


def smoothstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def rot(axis, deg):
    return Matrix.Rotation(math.radians(deg), 3, Vector(axis))


# ---------------------------------------------------------------- skeleton
# Right hand, palm facing -Z, fingers along +Y, thumb on the -X side. Units: cm.
FINGERS = [
    # name, spread(deg), lengths (MCP->PIP, PIP->DIP, DIP->tip), radius x, radius z
    ("Index",  7.0, (3.9, 2.45, 2.25), 0.96, 0.84),
    ("Middle", 1.0, (4.35, 2.8, 2.4), 0.99, 0.87),
    ("Ring",  -5.0, (4.05, 2.65, 2.3), 0.93, 0.82),
    ("Pinky", -12.0, (3.25, 2.0, 2.05), 0.82, 0.72),
]
REST_CURL = (6.0, 10.0, 9.0)  # rest flexion of each segment, degrees (cumulative)

NX, NY, NZ = 8, 5, 2
ROW_FRAC = [0.0, 0.17, 0.38, 0.59, 0.80, 1.0]
TOP_Y = [9.25, 9.6, 9.85, 10.05, 9.8, 9.55, 9.15, 8.65, 8.15]


class Finger:
    def __init__(self, name, base, side, segs, rx, rz, roll_end=0.0):
        self.name = name
        self.base = base            # centre of the extrusion region on the palm
        self.side = side            # flexion axis (bone local X)
        self.joints = []            # world positions: MCP, PIP, DIP, tip
        self.rx, self.rz = rx, rz
        self.roll_end = roll_end
        self.segs = segs

    def build(self, start, dirs):
        p = Vector(start)
        self.joints = [p.copy()]
        for L, d in zip(self.segs, dirs):
            p = p + d.normalized() * L
            self.joints.append(p.copy())
        self.dirs = [(self.joints[i + 1] - self.joints[i]).normalized() for i in range(3)]
        self.cum = [0.0, self.segs[0], self.segs[0] + self.segs[1], sum(self.segs)]

    def frame(self, t):
        """Centre, side axis, up axis and direction at arc length t."""
        s = 0 if t < self.cum[1] else (1 if t < self.cum[2] else 2)
        c = self.joints[s] + self.dirs[s] * (t - self.cum[s])
        d = self.dirs[s].copy()
        for j in (1, 2):
            w = float(smoothstep(self.cum[j] - 0.45, self.cum[j] + 0.45, t))
            if 0.0 < w < 1.0:
                d = self.dirs[j - 1].lerp(self.dirs[j], w).normalized()
        side = (self.side - self.side.dot(d) * d).normalized()
        up = side.cross(d).normalized()
        if self.roll_end:
            ang = self.roll_end * float(smoothstep(0.0, self.cum[2], t))
            R = rot(d, ang)
            side, up = R @ side, R @ up
        return c, side, up, d


PALM_HALF_THICK = [1.62, 1.58, 1.49, 1.4, 1.32, 1.18]
PALM_HALF_WIDTH = [3.05, 3.65, 4.0, 4.18, 4.25, 4.22]


def palm_point(i, j, k):
    xn = (i / NX) * 2.0 - 1.0
    frac = ROW_FRAC[j]
    x = xn * PALM_HALF_WIDTH[j]
    y = frac * TOP_Y[i]
    if j == 0:
        y += 0.3 * (1.0 - xn * xn)
    th = PALM_HALF_THICK[j] * (1.0 - 0.14 * max(0.0, xn))
    zd = th * (1.0 - 0.3 * xn * xn) - 0.1
    if j == NY and i % 2 == 1:
        zd += 0.12
    zp = -th * (1.0 - 0.06 * xn * xn) - 0.1
    if xn < -0.3 and frac < 0.65:
        zp -= 0.22 * (1.0 - frac / 0.65)
    if xn > 0.4 and frac < 0.8:
        zp -= 0.18 * (1.0 - frac / 0.8)
    if frac >= 0.8:
        zp -= 0.08
    z = zd if k == 2 else (zp if k == 0 else 0.5 * (zd + zp) - 0.05)
    # round the box: pull the long edges in so the subdivided palm is not slab-like
    if i in (0, NX) and k != 1:
        x *= 0.9
    if i in (0, NX) and k == 1:
        x *= 1.03
    if i in (1, NX - 1) and k != 1:
        z *= 0.92
    if j == 0 and k != 1:
        y += 0.25
    return Vector((x, y, z))


def build_base_mesh():
    bm = bmesh.new()
    lu = bm.verts.layers.int.new("lu")
    lv = bm.verts.layers.int.new("lv")
    mid = bm.verts.layers.int.new("mid")
    grp = bm.verts.layers.int.new("grp")

    verts = {}
    for i in range(NX + 1):
        for j in range(NY + 1):
            for k in range(NZ + 1):
                if i in (0, NX) or j in (0, NY) or k in (0, NZ):
                    v = bm.verts.new(palm_point(i, j, k))
                    v[mid] = 1 if k == 1 else 0
                    v[grp] = -1
                    verts[(i, j, k)] = v

    faces = {}

    def quad(key, a, b, c, d):
        faces[key] = bm.faces.new((verts[a], verts[b], verts[c], verts[d]))

    for i in range(NX):
        for j in range(NY):
            quad(("p", i, j), (i, j, 0), (i + 1, j, 0), (i + 1, j + 1, 0), (i, j + 1, 0))
            quad(("d", i, j), (i, j, NZ), (i, j + 1, NZ), (i + 1, j + 1, NZ), (i + 1, j, NZ))
    for i in range(NX):
        for k in range(NZ):
            quad(("w", i, k), (i, 0, k), (i, 0, k + 1), (i + 1, 0, k + 1), (i + 1, 0, k))
            quad(("t", i, k), (i, NY, k), (i + 1, NY, k), (i + 1, NY, k + 1), (i, NY, k + 1))
    for j in range(NY):
        for k in range(NZ):
            quad(("l", j, k), (0, j, k), (0, j + 1, k), (0, j + 1, k + 1), (0, j, k + 1))
            quad(("r", j, k), (NX, j, k), (NX, j, k + 1), (NX, j + 1, k + 1), (NX, j + 1, k))
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    return bm, verts, faces, (lu, lv, mid, grp)


def region_boundary(region):
    rs = set(region)
    return [e for f in region for e in f.edges if sum(1 for o in e.link_faces if o in rs) == 1]


def extrude_rings(bm, region, layers, ring_fn, n_rings):
    lu, lv, mid, grp = layers
    for r in range(n_rings):
        ret = bmesh.ops.extrude_face_region(bm, geom=region)
        bmesh.ops.delete(bm, geom=region, context="FACES_ONLY")
        new_verts = [g for g in ret["geom"] if isinstance(g, bmesh.types.BMVert)]
        vset = set(new_verts)
        region = [g for g in ret["geom"] if isinstance(g, bmesh.types.BMFace)
                  and all(v in vset for v in g.verts)]
        for v in new_verts:
            v.co = ring_fn(r, v[lu], v[lv], r == n_rings - 1)
    return region


def ellipse_offset(u, v, ru, rv):
    n = math.hypot(u, v)
    if n == 0:
        return 0.0, 0.0
    return u / n * ru, v / n * rv


def make_hand(split_fingers=True):
    bm, verts, faces, layers = build_base_mesh()
    base_loops = []
    lu, lv, mid, grp = layers
    fingers = []

    # ---- four fingers
    for f, (name, spread, segs, rx, rz) in enumerate(FINGERS):
        region = [faces[("t", i, k)] for i in (2 * f, 2 * f + 1) for k in range(NZ)]
        for i in range(2 * f, 2 * f + 3):
            for k in range(NZ + 1):
                v = verts[(i, NY, k)]
                v[lu], v[lv] = i - (2 * f + 1), k - 1
        base = sum((verts[(i, NY, k)].co for i in range(2 * f, 2 * f + 3) for k in range(NZ + 1)), Vector()) / 9
        side = rot((0, 0, 1), spread) @ Vector((1, 0, 0))
        along = rot((0, 0, 1), spread) @ Vector((0, 1, 0))
        fg = Finger(name, base, side, segs, rx, rz)
        dirs = []
        acc = 0.0
        for c in REST_CURL:
            acc += c
            dirs.append(rot(side, -acc) @ along)
        mcp = base - along * 0.95 + Vector((0, 0, 0.1))
        fg.build(mcp, dirs)
        t0 = (base - mcp).dot(along)
        L1, L2, L3 = segs
        T = sum(segs)
        rings = [
            (t0 + 0.45, 1.06, 0.0), (t0 + 1.4, 1.0, 0.0), (L1 - 0.38, 0.965, 0.0), (L1, 0.975, 0.0),
            (L1 + 0.38, 0.95, 0.0), (L1 + L2 * 0.5, 0.91, 0.0), (L1 + L2 - 0.3, 0.89, 0.0),
            (L1 + L2, 0.895, 0.0), (L1 + L2 + 0.3, 0.875, -0.02), (L1 + L2 + L3 * 0.55, 0.85, -0.05),
            (T - 0.55, 0.78, -0.08), (T - 0.18, 0.5, -0.06),
        ]

        def ring_fn(r, u, v, cap, fg=fg, rings=rings, T=T):
            t, s, dz = rings[r]
            c, sd, up, d = fg.frame(t)
            if cap and u == 0 and v == 0:
                c2, sd2, up2, d2 = fg.frame(T)
                return c2 + d2 * 0.12 + up2 * (dz * fg.rz)
            ox, oz = ellipse_offset(u, v, fg.rx * s, fg.rz * s)
            if v > 0:
                oz *= 1.0 - 0.14 * float(smoothstep(L1 + L2 - 0.5, T - 0.6, t))
            return c + sd * ox + up * (oz + dz * fg.rz)

        base_loops += region_boundary(region)
        extrude_rings(bm, region, layers, ring_fn, len(rings))
        fg.profile = [(0.0, 1.15)] + [(t, sc) for t, sc, _ in rings]
        fg.frame_fn = fg.frame
        fingers.append(fg)

    # ---- thumb, from the -X side face
    region = [faces[("l", j, k)] for j in (1, 2) for k in range(NZ)]
    for j in range(1, 4):
        for k in range(NZ + 1):
            v = verts[(0, j, k)]
            v[lu], v[lv] = j - 2, k - 1
    tbase = sum((verts[(0, j, k)].co for j in range(1, 4) for k in range(NZ + 1)), Vector()) / 9
    tdirs = [Vector((-0.66, 0.64, -0.3)), Vector((-0.3, 0.92, -0.2)), Vector((-0.1, 0.99, -0.1))]
    cmc = tbase + Vector((0.95, -1.2, 0.05))
    # the region's local axes (+Y, +Z) become the thumb's side / up before the roll
    th = Finger("Thumb", tbase, Vector((0, 1, 0)), (4.3, 3.05, 2.75), 1.14, 1.0, roll_end=-62.0)
    th.build(cmc, tdirs)
    th.side = Vector((0.0, 1.0, 0.0))

    def thumb_frame(t):
        c, _, _, d = th.frame(t)
        side = Vector((0.0, 1.0, 0.0))
        side = (side - side.dot(d) * d).normalized()
        up = Vector((0, 0, 1))
        up = (up - up.dot(d) * d - up.dot(side) * side).normalized()
        ang = th.roll_end * float(smoothstep(1.5, th.cum[2], t))
        R = rot(d, ang)
        return c, R @ side, R @ up, d

    th.frame_fn = thumb_frame
    L1, L2, L3 = th.segs
    T = sum(th.segs)
    trings = [
        (2.05, 1.75, 1.2, 0.0), (3.0, 1.38, 1.1, 0.0), (L1 - 0.45, 1.08, 1.0, 0.0), (L1, 1.1, 1.0, 0.0),
        (L1 + 0.4, 1.0, 0.97, 0.0), (L1 + L2 * 0.5, 0.97, 0.95, 0.0), (L1 + L2 - 0.32, 0.93, 0.92, 0.0),
        (L1 + L2, 0.96, 0.93, 0.0), (L1 + L2 + 0.32, 0.93, 0.9, 0.0), (L1 + L2 + L3 * 0.5, 0.92, 0.86, -0.05),
        (T - 0.55, 0.8, 0.75, -0.08), (T - 0.18, 0.52, 0.5, -0.06),
    ]

    def thumb_ring(r, u, v, cap):
        t, su, sv, push = trings[r]
        c, sd, up, d = thumb_frame(t)
        c = c + Vector((-push, 0, 0))
        if cap and u == 0 and v == 0:
            c2, sd2, up2, d2 = thumb_frame(T)
            return c2 + d2 * 0.14
        # region u runs along +Y (toward the fingers) -> thumb side; v along +Z -> up
        ox, oz = ellipse_offset(u, v, th.rx * su, th.rz * sv)
        if v > 0:
            oz *= 1.0 - 0.14 * float(smoothstep(L1 + L2 - 0.5, T - 0.6, t))
        return c + sd * ox + up * oz

    base_loops += region_boundary(region)
    extrude_rings(bm, region, layers, thumb_ring, len(trings))
    th.profile = [(0.0, 1.9)] + [(t, su) for t, su, _, _ in trings]
    fingers.insert(0, th)

    # ---- wrist stub from the bottom face
    region = [faces[("w", i, k)] for i in range(NX) for k in range(NZ)]
    for i in range(NX + 1):
        for k in range(NZ + 1):
            v = verts[(i, 0, k)]
            v[lu], v[lv] = i - NX // 2, k - 1
    wrings = [(-1.3, 2.95, 1.45), (-3.6, 2.8, 1.5)]

    def wrist_ring(r, u, v, cap):
        y, rx, rz = wrings[r]
        if cap and (abs(u) < NX // 2 and v == 0):
            return Vector((u / (NX // 2) * rx * 0.9, y - 0.25, -0.05))
        ox, oz = ellipse_offset(u / (NX // 2), v, rx, rz)
        return Vector((ox, y, oz - 0.05))

    extrude_rings(bm, region, layers, wrist_ring, len(wrings))

    bmesh.ops.delete(bm, geom=[e for e in bm.edges if not e.link_faces], context="EDGES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))

    # extruded vertices inherit "mid" from the k == 1 row they were copied from,
    # so the seam runs along the side of every finger and splits palm from back
    for e in bm.edges:
        a, b = e.verts
        e.seam = bool(a[mid] and b[mid])
    if split_fingers:
        for e in base_loops:
            if e.is_valid:
                e.seam = True

    me = bpy.data.meshes.new("Hand")
    bm.to_mesh(me)
    bm.free()
    me.transform(Matrix.Scale(CM, 4))
    obj = bpy.data.objects.new("Hand", me)
    bpy.context.scene.collection.objects.link(obj)
    return obj, fingers


# ---------------------------------------------------------------- previews
def look_at(cam, target):
    d = Vector(target) - cam.location
    cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()


def render_views(obj, prefix, views=None, res=640, engine="BLENDER_WORKBENCH"):
    scn = bpy.context.scene
    scn.render.engine = engine
    scn.render.resolution_x = res
    scn.render.resolution_y = res
    if engine == "BLENDER_WORKBENCH":
        scn.display.shading.light = "STUDIO"
        scn.display.shading.color_type = "TEXTURE" if obj.data.uv_layers and obj.active_material else "OBJECT"
        scn.display.shading.show_cavity = False
    cam_data = bpy.data.cameras.get("PreviewCam") or bpy.data.cameras.new("PreviewCam")
    cam = bpy.data.objects.get("PreviewCam") or bpy.data.objects.new("PreviewCam", cam_data)
    if cam.name not in scn.collection.objects:
        scn.collection.objects.link(cam)
    scn.camera = cam
    cam_data.lens = 50
    center = Vector((0, 0.045, -0.005))
    views = views or {
        "top": (0, 0.05, 0.42), "bottom": (0, 0.05, -0.42), "side": (-0.42, 0.05, 0.0),
        "persp": (-0.25, -0.18, 0.28), "front": (0.0, 0.45, 0.05), "pinky": (0.42, 0.05, 0.05),
    }
    for name, loc in views.items():
        cam.location = Vector(loc)
        look_at(cam, center)
        if name in ("top", "bottom"):
            cam.rotation_euler = (0, 0, 0) if name == "top" else (math.pi, 0, 0)
            cam.location = Vector((0, 0.05, loc[2]))
        scn.render.filepath = os.path.join(OUT, "previews", f"{prefix}_{name}.png")
        bpy.ops.render.render(write_still=True)
    contact_sheet([os.path.join(OUT, "previews", f"{prefix}_{n}.png") for n in views],
                  os.path.join(OUT, "previews", f"{prefix}_sheet.png"))


def contact_sheet(paths, out, cols=3):
    tiles = []
    for p in paths:
        img = bpy.data.images.load(p)
        w, h = img.size
        a = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)
        tiles.append(a)
        bpy.data.images.remove(img)
    h, w = tiles[0].shape[:2]
    rows = (len(tiles) + cols - 1) // cols
    sheet = np.ones((rows * h, cols * w, 4), dtype=np.float32)
    for n, t in enumerate(tiles):
        t = t.copy()
        t[..., :3] = t[..., :3] * t[..., 3:4] + 0.85 * (1.0 - t[..., 3:4])
        t[..., 3] = 1.0
        r, c = divmod(n, cols)
        sheet[(rows - 1 - r) * h:(rows - r) * h, c * w:(c + 1) * w] = t
    img = bpy.data.images.new("sheet", cols * w, rows * h, alpha=True)
    img.pixels[:] = sheet.ravel()
    img.filepath_raw = out
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)




# ---------------------------------------------------------------- UVs
def select_only(obj):
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def unwrap(obj, method="MINIMUM_STRETCH"):
    select_only(obj)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.unwrap(method=method, margin=0.02)
    bpy.ops.uv.pack_islands(rotate=True, shape_method="CONCAVE", margin=0.01)
    bpy.ops.object.mode_set(mode="OBJECT")


def uv_distortion(me):
    """Area-weighted angle (sigma1/sigma2) and area distortion of the UV map."""
    me.calc_loop_triangles()
    n = len(me.loop_triangles)
    loops = np.empty(n * 3, np.int32)
    tverts = np.empty(n * 3, np.int32)
    me.loop_triangles.foreach_get("loops", loops)
    me.loop_triangles.foreach_get("vertices", tverts)
    co = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    uv = np.empty(len(me.loops) * 2)
    me.uv_layers.active.data.foreach_get("uv", uv)
    P = co.reshape(-1, 3)[tverts].reshape(n, 3, 3)
    U = uv.reshape(-1, 2)[loops].reshape(n, 3, 2)
    e1, e2 = P[:, 1] - P[:, 0], P[:, 2] - P[:, 0]
    nrm = np.cross(e1, e2)
    a3 = 0.5 * np.linalg.norm(nrm, axis=1)
    ok = a3 > 1e-14
    e1, e2, nrm, a3, U = e1[ok], e2[ok], nrm[ok], a3[ok], U[ok]
    x = e1 / np.linalg.norm(e1, axis=1)[:, None]
    y = np.cross(nrm / np.linalg.norm(nrm, axis=1)[:, None], x)
    Pm = np.stack([np.stack([(e1 * x).sum(1), (e1 * y).sum(1)], 1),
                   np.stack([(e2 * x).sum(1), (e2 * y).sum(1)], 1)], 2)
    Um = np.stack([U[:, 1] - U[:, 0], U[:, 2] - U[:, 0]], 2)
    J = Um @ np.linalg.inv(Pm)
    s = np.linalg.svd(J, compute_uv=False)
    conf = s[:, 0] / np.maximum(s[:, 1], 1e-12)
    area = s[:, 0] * s[:, 1]
    g = (area * a3).sum() / a3.sum()
    ratio = area / g
    w = a3 / a3.sum()
    order = np.argsort(conf)
    p95 = conf[order][np.searchsorted(np.cumsum(w[order]), 0.95)]
    uv_area = (np.abs(np.cross(Um[:, :, 0], Um[:, :, 1])) * 0.5).sum()
    return {
        "angle_mean": float((conf * w).sum()),
        "angle_p95": float(p95),
        "area_within_25pct": float(w[(ratio > 0.8) & (ratio < 1.25)].sum()),
        "area_within_50pct": float(w[(ratio > 0.667) & (ratio < 1.5)].sum()),
        "uv_coverage": float(uv_area),
    }


def count_islands(me):
    bm = bmesh.new()
    bm.from_mesh(me)
    seen, islands = set(), 0
    for f in bm.faces:
        if f.index in seen:
            continue
        islands += 1
        stack = [f]
        seen.add(f.index)
        while stack:
            c = stack.pop()
            for e in c.edges:
                if e.seam:
                    continue
                for o in e.link_faces:
                    if o.index not in seen:
                        seen.add(o.index)
                        stack.append(o)
    bm.free()
    return islands


# ---------------------------------------------------------------- texture
TEX = 2048


def new_float_image(name, size=TEX):
    img = bpy.data.images.get(name)
    if img:
        bpy.data.images.remove(img)
    img = bpy.data.images.new(name, size, size, alpha=True, float_buffer=True)
    img.colorspace_settings.name = "Non-Color"
    return img


def bake_pass(obj, kind, img, samples=1):
    """kind: POSITION / NORMAL (emission of geometry data) or AO."""
    scn = bpy.context.scene
    scn.render.engine = "CYCLES"
    scn.cycles.device = "CPU"
    scn.cycles.samples = samples
    mat = bpy.data.materials.get("BakeData") or bpy.data.materials.new("BakeData")
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    geo = nt.nodes.new("ShaderNodeNewGeometry")
    emis = nt.nodes.new("ShaderNodeEmission")
    diff = nt.nodes.new("ShaderNodeBsdfDiffuse")
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = img
    nt.nodes.active = tex
    if kind == "AO":
        nt.links.new(diff.outputs[0], out.inputs[0])
    else:
        nt.links.new(geo.outputs["Position" if kind == "POSITION" else "Normal"], emis.inputs["Color"])
        nt.links.new(emis.outputs[0], out.inputs[0])
    obj.data.materials.clear()
    obj.data.materials.append(mat)
    select_only(obj)
    scn.render.bake.margin = 24
    scn.render.bake.margin_type = "ADJACENT_FACES"
    bpy.ops.object.bake(type="AO" if kind == "AO" else "EMIT", use_clear=True)
    return np.array(img.pixels[:], dtype=np.float32).reshape(img.size[1], img.size[0], 4)


class Noise3:
    def __init__(self, seed=7, n=64):
        self.n = n
        self.t = np.random.default_rng(seed).random((n, n, n)).astype(np.float32)

    def __call__(self, p):
        i = np.floor(p).astype(np.int64)
        f = (p - i).astype(np.float32)
        f = f * f * (3 - 2 * f)
        i %= self.n
        j = (i + 1) % self.n
        out = 0
        for dx in (0, 1):
            ix, wx = (j[:, 0], f[:, 0]) if dx else (i[:, 0], 1 - f[:, 0])
            for dy in (0, 1):
                iy, wy = (j[:, 1], f[:, 1]) if dy else (i[:, 1], 1 - f[:, 1])
                for dz in (0, 1):
                    iz, wz = (j[:, 2], f[:, 2]) if dz else (i[:, 2], 1 - f[:, 2])
                    out = out + self.t[ix, iy, iz] * wx * wy * wz
        return out

    def fbm(self, p, freqs):
        acc, norm = 0, 0
        for k, (fq, amp) in enumerate(freqs):
            acc = acc + (self(p * fq + 17.3 * k) - 0.5) * amp
            norm += amp
        return acc / norm


def finger_coords(fingers, P):
    """For points P (cm): nearest finger, arc length t along it, distance in finger
    radii, and the offset along the finger's side / up axes (in radii)."""
    best = np.full(len(P), np.inf, np.float32)
    fid = np.full(len(P), -1, np.int32)
    tt = np.zeros(len(P), np.float32)
    for f, fg in enumerate(fingers):
        J = [np.array(j, np.float32) for j in fg.joints]
        dmin = np.full(len(P), np.inf, np.float32)
        tbest = np.zeros(len(P), np.float32)
        for k in range(3):
            d = J[k + 1] - J[k]
            L = np.linalg.norm(d)
            d /= L
            u = (P - J[k]) @ d
            u = np.clip(u, -np.inf if k == 0 else 0.0, np.inf if k == 2 else L)
            dist = np.linalg.norm(P - (J[k] + u[:, None] * d), axis=1)
            better = dist < dmin
            dmin = np.where(better, dist, dmin)
            tbest = np.where(better, fg.cum[k] + u, tbest)
        prof_t = np.array([p[0] for p in fg.profile], np.float32)
        prof_s = np.array([p[1] for p in fg.profile], np.float32)
        nd = dmin / (fg.rx * np.interp(tbest, prof_t, prof_s))
        better = nd < best
        best = np.where(better, nd, best)
        fid = np.where(better, f, fid)
        tt = np.where(better, tbest, tt)
    side = np.zeros(len(P), np.float32)
    up = np.zeros(len(P), np.float32)
    for f, fg in enumerate(fingers):
        m = np.where(fid == f)[0]
        if not len(m):
            continue
        ts = np.linspace(-1.5, fg.cum[3] + 0.5, 200)
        frames = [fg.frame_fn(float(x)) for x in ts]
        C = np.array([fr[0] for fr in frames], np.float32)
        S = np.array([fr[1] for fr in frames], np.float32)
        U = np.array([fr[2] for fr in frames], np.float32)
        k = np.clip(np.searchsorted(ts, tt[m]), 0, len(ts) - 1)
        off = P[m] - C[k]
        side[m] = (off * S[k]).sum(1) / fg.rx
        up[m] = (off * U[k]).sum(1) / fg.rz
    return fid, tt, best, side, up


def gauss(x, w):
    return np.exp(-(x / w) ** 2)


def seg_dist(P2, a, b):
    a, b = np.array(a, np.float32), np.array(b, np.float32)
    d = b - a
    u = np.clip(((P2 - a) @ d) / (d @ d), 0, 1)
    return np.linalg.norm(P2 - (a + u[:, None] * d), axis=1)


def polyline_dist(P2, pts):
    return np.min([seg_dist(P2, pts[i], pts[i + 1]) for i in range(len(pts) - 1)], axis=0)


def srgb(c):
    return np.array(c, np.float32) / 255.0


def skin_texture(obj, fingers):
    """Bake position / normal / AO into UV space, then paint the skin per texel from
    the 3D data, so the colour is continuous across every UV seam."""
    pos = bake_pass(obj, "POSITION", new_float_image("bake_pos"))
    nor = bake_pass(obj, "NORMAL", new_float_image("bake_nor"))
    ao = bake_pass(obj, "AO", new_float_image("bake_ao"), samples=96)
    H, W = pos.shape[:2]
    idx = np.where((np.linalg.norm(nor[..., :3], axis=2) > 0.5).ravel())[0]
    P = pos.reshape(-1, 4)[idx, :3] / CM
    N = nor.reshape(-1, 4)[idx, :3]
    A = ao.reshape(-1, 4)[idx, 0]
    noise = Noise3()

    fid, t, nd, side, up = finger_coords(fingers, P)
    on_finger = (fid >= 0) & (nd < 1.6) & (t > np.array([fg.profile[1][0] - 0.6 for fg in fingers], np.float32)[np.maximum(fid, 0)])
    on_thumb = on_finger & (fid == 0)
    on_long = on_finger & (fid > 0)
    fsafe = np.maximum(fid, 0)
    T = np.array([fg.cum[3] for fg in fingers], np.float32)[fsafe]
    J1 = np.array([fg.cum[1] for fg in fingers], np.float32)[fsafe]
    J2 = np.array([fg.cum[2] for fg in fingers], np.float32)[fsafe]

    back, palm, pink = srgb((214, 160, 128)), srgb((232, 180, 155)), srgb((212, 126, 110))
    w_palm = smoothstep(0.25, -0.45, N[:, 2])
    w_palm = np.where(on_thumb, smoothstep(0.3, -0.6, up), w_palm)
    w_palm = np.where(on_long, smoothstep(0.35, -0.55, up), w_palm)
    col = back[None] * (1 - w_palm[:, None]) + palm[None] * w_palm[:, None]

    # warmth on fingertips, knuckles and palm pads
    red = on_finger * smoothstep(T - 2.2, T - 0.2, t) * 0.45
    red = red + on_long * (1 - w_palm) * (gauss(t - J1, 0.45) * 0.35 + gauss(t - J2, 0.35) * 0.25
                                          + gauss(t - 0.9, 0.6) * 0.3)
    red = red + w_palm * (1 - on_finger) * smoothstep(5.0, 9.5, P[:, 1]) * 0.15
    red = red + w_palm * gauss(np.linalg.norm(P[:, :2] - np.array([-3.4, 2.8]), axis=1), 2.2) * 0.2
    red = red + w_palm * gauss(np.linalg.norm(P[:, :2] - np.array([3.2, 3.0]), axis=1), 2.0) * 0.15
    red = np.clip(red + noise.fbm(P, [(0.5, 1.0), (1.3, 0.6)]) * 0.35, 0, 0.8)
    col = col * (1 - red[:, None]) + pink[None] * red[:, None]

    mott = noise.fbm(P, [(0.7, 1.0), (2.1, 0.6), (5.0, 0.35)])
    pores = noise.fbm(P, [(22.0, 1.0), (45.0, 0.6)])
    col *= (1 + mott * 0.16 + pores * 0.08)[:, None]

    def darken(mask, amount, tint=(0.62, 0.42, 0.38)):
        nonlocal col
        m = np.clip(mask * amount, 0, 1)[:, None]
        col = col * (1 - m) + col * np.array(tint, np.float32)[None] * m

    # palmar flexion creases (two lines per joint) and the finger-base crease
    palmar = on_long * smoothstep(-0.2, -0.75, up)
    for Jx, w in ((J1, 1.0), (J2, 0.8)):
        darken(palmar * (gauss(t - Jx + 0.09, 0.05) + gauss(t - Jx - 0.1, 0.05)), 0.75 * w)
    base_t = np.array([fg.profile[1][0] - 0.05 for fg in fingers], np.float32)[fsafe]
    darken(palmar * gauss(t - base_t, 0.07), 0.6)
    thumb_palmar = on_thumb * smoothstep(-0.2, -0.75, up)
    darken(thumb_palmar * gauss(t - fingers[0].cum[2], 0.07), 0.7)
    darken(thumb_palmar * gauss(t - fingers[0].cum[1] - 0.2, 0.08), 0.5)
    # dorsal knuckle wrinkles
    dorsal = on_long * smoothstep(0.3, 0.8, up)
    wr = (np.sin((t - J1) * 30.0) * 0.5 + 0.5) * gauss(t - J1, 0.3) * gauss(side, 0.55)
    wr2 = (np.sin((t - J2) * 36.0) * 0.5 + 0.5) * gauss(t - J2, 0.18) * gauss(side, 0.5)
    darken(dorsal * (wr * 0.5 + wr2 * 0.4), 0.55)

    # palm lines: heart, head, life (and a faint fate line)
    in_palm = (~on_finger) * smoothstep(-0.3, -0.7, N[:, 2]) * smoothstep(-0.5, 0.5, P[:, 1])
    P2 = P[:, :2]
    heart = polyline_dist(P2, [(4.5, 7.2), (2.6, 7.6), (0.6, 8.0), (-1.0, 8.9)])
    head = polyline_dist(P2, [(-3.9, 6.9), (-2.0, 6.0), (0.3, 5.3), (2.8, 4.7)])
    life = polyline_dist(P2, [(-3.9, 6.9), (-2.6, 5.4), (-2.0, 3.6), (-2.1, 1.6), (-2.6, 0.4)])
    fate = polyline_dist(P2, [(0.3, 1.0), (0.4, 4.2), (0.2, 6.4)])
    lines = gauss(heart, 0.07) + gauss(head, 0.065) + gauss(life, 0.07) + 0.35 * gauss(fate, 0.05)
    lines *= 0.75 + 0.5 * (noise(P * 3.0) - 0.5)
    darken(in_palm * lines, 0.8)
    wrist_lines = gauss(P[:, 1] + 0.6, 0.06) + 0.7 * gauss(P[:, 1] + 1.0, 0.05)
    darken(smoothstep(-0.2, -0.7, N[:, 2]) * wrist_lines, 0.5)

    # nails with lunula and free edge
    near_tip = on_finger & (t > T - 1.9)
    nail_len = np.where(fid == 0, 1.55, 1.35)
    tn = (t - (T - 0.12 - nail_len)) / nail_len   # 0 at the cuticle, 1 at the free edge
    nail = smoothstep(0.62, 0.52, np.abs(side)) * smoothstep(0.0, 0.08, tn) * smoothstep(1.06, 0.98, tn)
    nail *= smoothstep(0.25, 0.5, up) * near_tip
    free_edge = smoothstep(0.82, 0.92, tn)
    lunula = smoothstep(0.26, 0.16, np.hypot(tn * 1.6, side * 0.8)) * 0.8
    ncol = srgb((236, 176, 170))[None] * (1 - free_edge[:, None]) + srgb((246, 236, 226))[None] * free_edge[:, None]
    ncol = ncol * (1 - lunula[:, None]) + srgb((246, 214, 208))[None] * lunula[:, None]
    ncol *= (1 + 0.04 * np.sin(side * 40.0))[:, None]
    col = col * (1 - nail[:, None]) + ncol * nail[:, None]
    top = near_tip * smoothstep(0.25, 0.5, up)
    darken(top * smoothstep(0.66, 0.56, np.abs(side)) * gauss(tn + 0.02, 0.04), 0.55)
    darken(top * gauss(np.abs(side) - 0.6, 0.04) * (tn > 0) * (tn < 0.95), 0.35)

    col *= (0.55 + 0.45 * A)[:, None]
    col = np.clip(col, 0, 1)

    out = np.zeros((H * W, 4), np.float32)
    out[:, :3] = back
    out[idx, :3] = col
    out[:, 3] = 1.0
    return save_image("Hand_Albedo", out, W, H, os.path.join(OUT, "textures", "Hand_Albedo.png"))


def save_image(name, rgba, W, H, path):
    img = bpy.data.images.get(name)
    if img:
        bpy.data.images.remove(img)
    img = bpy.data.images.new(name, W, H, alpha=False)
    img.pixels[:] = rgba.ravel()
    os.makedirs(os.path.dirname(path), exist_ok=True)
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    img.filepath = path
    return img


def uv_layout_png(me, path, background, size=2048):
    """UV wireframe drawn over the texture (the GPU layout exporter needs a window)."""
    W, H = background.size
    img = np.array(background.pixels[:], np.float32).reshape(H, W, 4)
    img[..., :3] = img[..., :3] * 0.55 + 0.45
    uv = np.empty(len(me.loops) * 2)
    me.uv_layers.active.data.foreach_get("uv", uv)
    uv = uv.reshape(-1, 2)
    a, b = [], []
    for p in me.polygons:
        ls = list(p.loop_indices)
        a += ls
        b += ls[1:] + ls[:1]
    A, B = uv[a], uv[b]
    for s in np.linspace(0, 1, 12):
        q = A * (1 - s) + B * s
        x = np.clip((q[:, 0] * W).astype(int), 0, W - 1)
        y = np.clip((q[:, 1] * H).astype(int), 0, H - 1)
        img[y, x, :3] = (0.12, 0.2, 0.45)
    save_image("Hand_UV_Layout", img, W, H, path)


def checker_image():
    img = bpy.data.images.new("UV_Checker", 1024, 1024, alpha=False)
    img.generated_type = "COLOR_GRID"
    path = os.path.join(OUT, "textures", "UV_Checker.png")
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    img.filepath = path
    return img


def textured_material(name, image, roughness=0.55, sss=True):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = image
    nt.nodes.active = tex
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(bsdf.outputs[0], out.inputs[0])
    bsdf.inputs["Roughness"].default_value = roughness
    if sss and "Subsurface Weight" in bsdf.inputs:
        bsdf.inputs["Subsurface Weight"].default_value = 0.12
        bsdf.inputs["Subsurface Radius"].default_value = (1.0, 0.35, 0.2)
        bsdf.inputs["Subsurface Scale"].default_value = 0.004
    return mat


# ---------------------------------------------------------------- rig
def build_rig(fingers):
    arm = bpy.data.armatures.new("HandRig")
    rig = bpy.data.objects.new("HandRig", arm)
    bpy.context.scene.collection.objects.link(rig)
    select_only(rig)
    bpy.ops.object.mode_set(mode="EDIT")
    eb = arm.edit_bones
    root = eb.new("Hand")
    root.head = Vector((0.0, -2.0, 0.0)) * CM
    root.tail = Vector((0.0, 6.0, 0.0)) * CM
    root.align_roll(Vector((0, 0, 1)))
    for fg in fingers:
        parent = root
        for k in range(3):
            b = eb.new(f"{fg.name}_{k + 1}")
            b.head = fg.joints[k] * CM
            b.tail = fg.joints[k + 1] * CM
            # bone Z = back of the finger, so bone X is the flexion axis
            b.align_roll(fg.frame_fn(0.5 * (fg.cum[k] + fg.cum[k + 1]))[2])
            b.parent = parent
            b.use_connect = k > 0
            parent = b
    bpy.ops.object.mode_set(mode="OBJECT")
    return rig


def skin_weights(obj, rig, fingers):
    """Rigid weights per phalanx with smooth blends across each joint."""
    me = obj.data
    co = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    P = (co.reshape(-1, 3) / CM).astype(np.float32)
    fid, t, nd, _, _ = finger_coords(fingers, P)
    groups = {b.name: obj.vertex_groups.new(name=b.name) for b in rig.data.bones}
    weights = {name: np.zeros(len(P), np.float32) for name in groups}
    weights["Hand"][:] = 1.0
    for f, fg in enumerate(fingers):
        J1, J2 = fg.cum[1], fg.cum[2]
        if fg.name == "Thumb":
            memb = smoothstep(1.85, 1.3, nd) * (fid == f)
            s0 = smoothstep(0.3, 2.4, t)
            s1 = smoothstep(J1 - 0.55, J1 + 0.55, t)
        else:
            memb = smoothstep(2.0, 1.35, nd) * (fid == f)
            s0 = smoothstep(-0.5, 1.1, t)
            s1 = smoothstep(J1 - 0.45, J1 + 0.45, t)
        s2 = smoothstep(J2 - 0.4, J2 + 0.4, t)
        weights[f"{fg.name}_1"] += memb * (s0 - s1)
        weights[f"{fg.name}_2"] += memb * (s1 - s2)
        weights[f"{fg.name}_3"] += memb * s2
        weights["Hand"] -= memb * s0
    for name, w in weights.items():
        vg = groups[name]
        for i in np.where(w > 1e-4)[0]:
            vg.add([int(i)], float(w[i]), "REPLACE")
    obj.parent = rig
    mod = obj.modifiers.new("Armature", "ARMATURE")
    mod.object = rig


def q_flex(x_deg, z_deg=0.0):
    return (Quaternion((0, 0, 1), math.radians(z_deg)) @ Quaternion((1, 0, 0), math.radians(x_deg)))


# negative X rotation curls a phalanx toward the palm; Z closes the spread
GRIP = {
    "Middle_1": (-62, 0), "Middle_2": (-88, 0), "Middle_3": (-48, 0),
    "Ring_1": (-64, 4), "Ring_2": (-90, 0), "Ring_3": (-48, 0),
    "Pinky_1": (-66, 9), "Pinky_2": (-86, 0), "Pinky_3": (-46, 0),
    "Thumb_2": (-8, 0), "Thumb_3": (-12, 0),
}
TRIGGER = {
    "Index_1": (-36, -3), "Index_2": (-62, 0), "Index_3": (-36, 0),
    "Thumb_3": (-6, 0),
}
POSES = {
    "Idle": {},
    "Grip": GRIP,
    "Trigger": TRIGGER,
    "Fist": {**GRIP, **TRIGGER, "Thumb_1": (-10, 0), "Thumb_2": (-22, 0), "Thumb_3": (-28, 0)},
}


def apply_pose(rig, pose):
    for pb in rig.pose.bones:
        pb.rotation_mode = "QUATERNION"
        pb.location = (0, 0, 0)
        pb.scale = (1, 1, 1)
        x, z = pose.get(pb.name, (0, 0))
        pb.rotation_quaternion = q_flex(x, z)


def make_actions(rig):
    rig.animation_data_create()
    actions = {}
    for name, pose in POSES.items():
        act = bpy.data.actions.new(name)
        act.use_fake_user = True
        rig.animation_data.action = act
        apply_pose(rig, pose)
        for frame in (0, 1):
            for pb in rig.pose.bones:
                pb.keyframe_insert("rotation_quaternion", frame=frame)
                pb.keyframe_insert("location", frame=frame)
        actions[name] = act
    rig.animation_data.action = actions["Idle"]
    return actions


def make_showcase(rig, actions):
    """A timeline animation through all poses, used for the Blender preview video."""
    act = bpy.data.actions.new("Showcase")
    act.use_fake_user = True
    rig.animation_data.action = act
    keys = [(1, "Idle"), (18, "Idle"), (34, "Grip"), (52, "Grip"), (68, "Idle"), (84, "Trigger"),
            (100, "Trigger"), (116, "Fist"), (134, "Fist"), (150, "Idle"), (160, "Idle")]
    for frame, pose in keys:
        apply_pose(rig, POSES[pose])
        for pb in rig.pose.bones:
            pb.keyframe_insert("rotation_quaternion", frame=frame)
    bpy.context.scene.frame_start, bpy.context.scene.frame_end = 1, 160
    return act


def export_fbx(rig, obj, path):
    for o in bpy.context.view_layer.objects:
        o.select_set(o in (rig, obj))
    bpy.context.view_layer.objects.active = rig
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"ARMATURE", "MESH"},
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z", axis_up="Y", use_mesh_modifiers=True, mesh_smooth_type="FACE",
        add_leaf_bones=False, use_armature_deform_only=True,
        bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
        bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0,
        path_mode="COPY", embed_textures=False,
    )


# ---------------------------------------------------------------- main
def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    obj, fingers = make_hand(split_fingers=True)
    sub = obj.modifiers.new("Subsurf", "SUBSURF")
    sub.levels = 2
    select_only(obj)
    bpy.ops.object.modifier_apply(modifier="Subsurf")
    for p in obj.data.polygons:
        p.use_smooth = True
    unwrap(obj, "MINIMUM_STRETCH")
    stats = uv_distortion(obj.data)
    stats["islands"] = count_islands(obj.data)
    stats["verts"] = len(obj.data.vertices)
    stats["tris"] = sum(len(p.vertices) - 2 for p in obj.data.polygons)
    print("UVSTATS", stats)

    albedo = skin_texture(obj, fingers)
    uv_layout_png(obj.data, os.path.join(OUT, "textures", "Hand_UV_Layout.png"), albedo)
    checker = checker_image()
    skin = textured_material("Hand_Skin", albedo)
    textured_material("Hand_UVChecker", checker, roughness=0.6, sss=False).use_fake_user = True
    obj.data.materials.clear()
    obj.data.materials.append(skin)
    bpy.data.materials.remove(bpy.data.materials["BakeData"])
    for name in ("bake_pos", "bake_nor", "bake_ao"):
        bpy.data.images.remove(bpy.data.images[name])

    rig = build_rig(fingers)
    skin_weights(obj, rig, fingers)
    actions = make_actions(rig)
    bpy.context.scene.frame_set(0)
    export_fbx(rig, obj, os.path.join(OUT, "Hand.fbx"))

    make_showcase(rig, actions)
    rig.animation_data.action = actions["Idle"]
    bpy.context.scene.render.engine = "BLENDER_EEVEE"
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "Hand.blend"))

    if PREVIEWS:
        for name in POSES:
            rig.animation_data.action = actions[name]
            bpy.context.scene.frame_set(0)
            render_views(obj, f"pose_{name}", res=480)
        obj.data.materials[0] = bpy.data.materials["Hand_UVChecker"]
        rig.animation_data.action = actions["Idle"]
        bpy.context.scene.frame_set(0)
        render_views(obj, "checker", res=480)


main()
