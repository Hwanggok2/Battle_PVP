"""Extrude azureguy's CC0 hook silhouette; preserve the eyelet, omit the 2D rope.
Source: https://opengameart.org/content/grappling-hook
Only local vector geometry is processed. No images or external code are executed.
"""
from pathlib import Path
import re
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[2]
folder = root / 'Assets/Remodel/Skills/Source/Azureguy'
svg = ET.parse(folder / 'GrapplingHook.svg')
path = svg.getroot().find('.//*[@id="path3020-4"]').attrib['d']
tokens = re.findall(r'[mclz]|[-+]?(?:\d*\.\d+|\d+\.?\d*)(?:[eE][-+]?\d+)?', path)
loops, points, pos, start, i, command = [], [], (0., 0.), (0., 0.), 0, None
while i < len(tokens):
    if tokens[i] in ('m', 'c', 'l', 'z'):
        command = tokens[i]; i += 1
    if command == 'z':
        loops.append(points); points = []; pos = start; command = None; continue
    size = 6 if command == 'c' else 2
    vals = list(map(float, tokens[i:i+size])); i += size
    if command == 'c':
        a = (pos[0]+vals[0], pos[1]+vals[1]); b = (pos[0]+vals[2], pos[1]+vals[3]); c = (pos[0]+vals[4], pos[1]+vals[5])
        for j in range(1, 7):
            t = j/6; u = 1-t
            points.append(tuple(u*u*u*pos[k]+3*u*u*t*a[k]+3*u*t*t*b[k]+t*t*t*c[k] for k in (0, 1)))
        pos = c
    else:
        pos = (pos[0]+vals[0], pos[1]+vals[1]); points.append(pos)
        if command == 'm': start = pos; command = 'l'
assert len(loops) == 2, 'Expected hook outline and eyelet hole'
all_points = [p for loop in loops for p in loop]
minx, maxx = min(p[0] for p in all_points), max(p[0] for p in all_points)
miny, maxy = min(p[1] for p in all_points), max(p[1] for p in all_points)
scale = .50/(maxy-miny)
vertices, faces = [], []
def vertex(p, side): return ((p[0]-(minx+maxx)/2)*scale, side*.024, ((miny+maxy)/2-p[1])*scale)
def face(points):
    first = len(vertices)+1; vertices.extend(points)
    faces.extend([(first, first+j, first+j+1) for j in range(1, len(points)-1)])
edges = [(loop[j], loop[(j+1)%len(loop)]) for loop in loops for j in range(len(loop))]
# Even-odd scanline trapezoids handle the original eyelet without filling it.
ys = sorted(set(p[1] for p in all_points))
for low, high in zip(ys, ys[1:]):
    if high-low < 1e-6: continue
    mid = (low+high)/2
    crossing = [(a,b) for a,b in edges if min(a[1],b[1]) < mid < max(a[1],b[1])]
    def at(edge,y):
        a,b = edge; return (a[0]+(b[0]-a[0])*(y-a[1])/(b[1]-a[1]), y)
    crossing.sort(key=lambda e: at(e,mid)[0])
    assert len(crossing)%2 == 0
    for left,right in zip(crossing[::2],crossing[1::2]):
        quad = [at(left,low),at(right,low),at(right,high),at(left,high)]
        face([vertex(p,1) for p in quad]); face([vertex(p,-1) for p in reversed(quad)])
for a,b in edges:
    face([vertex(a,-1),vertex(a,1),vertex(b,1),vertex(b,-1)])
text = ['# CC0 silhouette by azureguy; extruded for Battle PVP', 'o GrapplingHook']
text += ['v %.7f %.7f %.7f'%v for v in vertices]
text += ['f %d %d %d'%f for f in faces]
(folder/'GrapplingHook.obj').write_text('\n'.join(text)+'\n')
print(f'Hook: {len(vertices)} vertices, {len(faces)} triangles; length 0.50m, thickness 0.048m')
