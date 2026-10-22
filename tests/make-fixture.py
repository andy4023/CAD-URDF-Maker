"""Generate an editor fixture and box meshes without reading external CAD files."""
import json
import math
from pathlib import Path
import struct

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / 'build'
OUTPUT.mkdir(exist_ok=True)


def transform(x=0, y=0, z=0):
    return [1, 0, 0, x, 0, 1, 0, y, 0, 0, 1, z, 0, 0, 0, 1]


def box_mesh(path, size):
    x, y, z = [v / 2 for v in size]
    vertices = [(-x, -y, -z), (x, -y, -z), (x, y, -z), (-x, y, -z),
                (-x, -y, z), (x, -y, z), (x, y, z), (-x, y, z)]
    faces = [(0, 2, 1), (0, 3, 2), (4, 5, 6), (4, 6, 7),
             (0, 1, 5), (0, 5, 4), (1, 2, 6), (1, 6, 5),
             (2, 3, 7), (2, 7, 6), (3, 0, 4), (3, 4, 7)]
    body = b''.join(struct.pack('<12fH', 0, 0, 0,
                               *(n for i in face for n in vertices[i]), 0)
                    for face in faces)
    path.write_bytes(b'Procedural analytical box'.ljust(80, b'\0') + struct.pack('<I', 12) + body)


components, links, joints, properties, urls = [], [], [], {}, {}
examples = [('base_link', (0.4, 0.3, 0.15), 3.0, (0, 0, 0.075)),
            ('arm_link', (0.1, 0.1, 0.4), 1.2, (0, 0, 0.35)),
            ('tool_link', (0.12, 0.08, 0.1), 0.4, (0, 0, 0.65))]
for name, size, mass, position in examples:
    x, y, z = size
    physics = dict(Mass=mass, Cog=[0, 0, 0],
                   Inertia=[mass*(y*y+z*z)/12, 0, 0, 0, mass*(x*x+z*z)/12, 0,
                            0, 0, mass*(x*x+y*y)/12], Source='Analytical box')
    cid = 'synthetic-' + name
    frame = transform(*position)
    components.append(dict(Id=cid, Name=name, Path='Procedural box', Configuration='synthetic',
                           PersistentReference='', ZeroTransform=frame, Cad=physics, UnitDensity=None,
                           Override=dict(Mode='cad', Density=0, Complete=physics, Note=''),
                           MaterialAcknowledged=False, MaterialKnown=True, Excluded=False,
                           Hidden=False, Resolved=True, Issue=''))
    links.append(dict(Id=name, Name=name, Components=[cid], ZeroFrame=frame))
    properties[name] = physics
    filename = 'synthetic-' + name + '.stl'
    box_mesh(OUTPUT / filename, size)
    urls[cid] = '/build/' + filename

for name, kind, parent, child, axis, lower, upper in [
        ('arm_joint', 'revolute', 'base_link', 'arm_link', [0, 1, 0], -1, 1),
        ('tool_joint', 'prismatic', 'arm_link', 'tool_link', [0, 0, 1], 0, 0.2)]:
    joints.append(dict(Id=name, Name=name, Type=kind, Parent=parent, Child=child, Axis=axis,
                       Lower=lower, Upper=upper, Effort=10, Velocity=1, Confirmed=False,
                       GeometryReference='', GeometryComponent='', ReferencePoint=None,
                       ReferenceDirection=None, ReferenceValid=True))

project = dict(SchemaVersion=1, Name='synthetic_robot', AssemblyPath='',
               AssemblyTitle='Synthetic box robot', Configuration='synthetic', RootLink='base_link',
               CapturedUtc='', CadStamp=0, Stale=False, ZeroCaptured=True, Components=components,
               Links=links, Joints=joints, Suggestions=[], CadIssues=[],
               Alignment=dict(Enabled=False, Rpy=[0, 0, 0]), ChildFramesFollowRoot=True,
               Export=dict(ChordMetres=0.0001, AngleRadians=math.pi/36))
state = dict(type='state', project=project, properties=properties, meshUrls=urls,
             projectPath='', busy=False)
(OUTPUT / 'synthetic-viewer-fixture.json').write_text(json.dumps(state), encoding='utf-8')
print('Generated synthetic editor fixture: 3 analytical boxes, 2 joints')
