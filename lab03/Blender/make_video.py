"""Encodes the Unity demo frames (with captions) into an MP4 using Blender's video sequencer.

Run:  blender -b --factory-startup -P make_video.py -- UNITY_FRAMES OUT.mp4
"""
import bpy
import os
import sys

unity_dir, out_path = sys.argv[sys.argv.index("--") + 1:][:2]

scn = bpy.context.scene
scn.render.resolution_x, scn.render.resolution_y = 1280, 720
scn.render.resolution_percentage = 100
scn.render.fps = 30
scn.view_settings.view_transform = "Standard"
scn.view_settings.look = "None"
se = scn.sequence_editor_create()
strips = se.strips if hasattr(se, "strips") else se.sequences


def image_sequence(folder, files, channel, start):
    s = strips.new_image(os.path.basename(folder), os.path.join(folder, files[0]), channel, start, fit_method="FIT")
    for f in files[1:]:
        s.elements.append(f)
    return s


def effect(name, kind, channel, start, length):
    try:
        return strips.new_effect(name, kind, channel, start, length=length)
    except TypeError:
        return strips.new_effect(name, kind, channel, start, frame_end=start + length)


def text(body, start, length, size=29, y=0.07, channel=5, box=True):
    t = effect("T%d" % start, "TEXT", channel, start, length)
    t.text = body
    t.font_size = size
    t.location = (0.5, y)
    t.anchor_x = "CENTER"
    t.anchor_y = "CENTER"
    t.color = (1, 1, 1, 1)
    t.use_box = box
    if box:
        t.box_color = (0.0, 0.0, 0.0, 0.62)
        t.box_margin = 0.012
    return t


frame = 1
files = sorted(f for f in os.listdir(unity_dir) if f.endswith(".jpg"))
image_sequence(unity_dir, files, 1, frame)
captions = []
with open(os.path.join(unity_dir, "captions.txt"), encoding="utf-8") as fh:
    for line in fh:
        if line.strip():
            k, body = line.rstrip("\n").split("\t", 1)
            captions.append((int(k), body))
for i, (k, body) in enumerate(captions):
    end = captions[i + 1][0] if i + 1 < len(captions) else len(files)
    text(body, frame + k, end - k)
frame += len(files)

scn.frame_start, scn.frame_end = 1, frame - 1
img = scn.render.image_settings
if hasattr(img, "media_type"):
    img.media_type = "VIDEO"
img.file_format = "FFMPEG"
scn.render.ffmpeg.format = "MPEG4"
scn.render.ffmpeg.codec = "H264"
scn.render.ffmpeg.constant_rate_factor = "HIGH"
scn.render.ffmpeg.ffmpeg_preset = "GOOD"
scn.render.ffmpeg.audio_codec = "NONE"
scn.render.filepath = out_path
bpy.ops.render.render(animation=True)
print("VIDEO", out_path, scn.frame_end, "frames")
