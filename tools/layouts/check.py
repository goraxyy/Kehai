#!/usr/bin/env python3
"""Export reviewed shelf topology at metre scale; no Unity/scene import needed.

This checks source geometry only, not the scene NavMesh. Outputs stay outside the repo.
"""
import argparse
from collections import deque
from html import escape
import json
from pathlib import Path
import re

SOURCE = Path(__file__).resolve().parents[2] / 'Assets/!_Project/_Game/Level/Scripts/StoreLayouts.cs'


def layouts():
    result = {}
    for ident, name, held, body in re.findall(r'new Layout\("([^"]+)", "([^"]+)", (true|false),\s*(.*?)\),', SOURCE.read_text(), re.S):
        rows = re.findall(r'"([#D.+|$F]+)"', body)
        result[ident] = {'name': name, 'heldout': held == 'true', 'rows': rows}
    return result


def rectangles(rows):
    def shelf(r,c):
        return 0 < r < len(rows)-1 and 0 < c < len(rows[0])-1 and rows[r][c] in '#+|'
    for r in range(1,len(rows)-1):
        for c in range(1,len(rows[0])-1):
            if not shelf(r,c): continue
            x,z = (c-.5)*2.5,(r-.5)*2.5
            w,e,n,s = shelf(r,c-1),shelf(r,c+1),shelf(r-1,c),shelf(r+1,c)
            if (w or e) and not n and not s:
                yield x-1.25,z-.5,2.5,1,rows[r][c]
            elif (n or s) and not w and not e:
                yield x-.5,z-1.25,1,2.5,rows[r][c]
            else:
                yield x-.5,z-.5,1,1,rows[r][c]
                for dx,dz,a,b,on in [(-.875,0,.75,1,w),(.875,0,.75,1,e),(0,-.875,1,.75,n),(0,.875,1,.75,s)]:
                    if on: yield x+dx-a/2,z+dz-b/2,a,b,rows[r][c]


def inspect(layout):
    rows = layout['rows']
    if len(rows)!=24 or any(len(row)!=26 for row in rows):
        raise ValueError('expected 26 × 24 drawing')
    rects=list(rectangles(rows))
    # Half-metre sample grid with a 0.3 m body clearance; validates pedestrian paths through
    # thin shelves rather than declaring the entire drawing tile solid.
    step=.5; nx,nz=120,110
    free=set()
    for x in range(nx):
        for z in range(nz):
            px,pz=(x+.5)*step,(z+.5)*step
            if all(not(a-.3 < px < a+w+.3 and b-.3 < pz < b+h+.3) for a,b,w,h,_ in rects):
                free.add((x,z))
    start=(35,1); seen={start}; pending=deque(seen)
    if start not in free: raise ValueError('north door apron blocked')
    while pending:
        x,z=pending.popleft()
        for target in ((x-1,z),(x+1,z),(x,z-1),(x,z+1)):
            if target in free and target not in seen: seen.add(target);pending.append(target)
    # North entrance/exit, west staff/storage openings, south two cold/storage doors.
    aprons=[(17.5,1),(42.5,1),(1,2.5),(1,22.5),(17.5,54),(42.5,54)]
    unreachable=[p for p in aprons if (int(p[0]/step),int(p[1]/step)) not in seen]
    if unreachable:raise ValueError('unreachable apron: '+str(unreachable))
    return {'source_only':True,'free_samples':len(free),'reachable_samples':len(seen),
            'reachable_fraction':len(seen)/len(free),'aprons_reachable':True,
            'shelf_pitch_m':2.5,'shelf_depth_m':1,'navmesh_checked':False}


def svg(layout):
    body=[]
    for x,z,w,h,kind in rectangles(layout['rows']):
        colour='#edbc60' if kind=='+' else '#9daeb5'
        body.append(f'<rect x="{x}" y="{z}" width="{w}" height="{h}" fill="{colour}" stroke="#263236" stroke-width=".08"/>')
    for r,row in enumerate(layout['rows'][1:-1],1):
        for c,ch in enumerate(row[1:-1],1):
            if ch in 'F$':
                body.append(f'<rect x="{(c-.5)*2.5-1}" y="{(r-.5)*2.5-.4}" width="2" height=".8" fill="'+('#4bb7cd' if ch=='F' else '#8ea864')+'"/>')
    return '<svg xmlns="http://www.w3.org/2000/svg" viewBox="-2 -5 64 64"><rect x="0" y="0" width="60" height="55" fill="#20282c" stroke="#eee" stroke-width=".3"/><text x="0" y="-2" fill="#111" font-size="2">'+escape(layout['name'])+'</text>'+''.join(body)+'<text x="0" y="59" font-size="1.4">2.5 m pitch · 1 m shelves · amber low · grey tall · blue cold (fixed)</text></svg>'


def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--out',type=Path)
    args=parser.parse_args();results={}
    if args.out:
        repo=SOURCE.parents[5].resolve()
        # Outputs are generated review assets, never committed art.
        out=args.out.resolve()
        if out==repo or repo in out.parents:raise SystemExit('Choose a private output directory outside the repository')
        out.mkdir(parents=True,exist_ok=True)
    for ident,layout in layouts().items():
        results[ident]=inspect(layout)
        if args.out:(args.out/(ident+'.svg')).write_text(svg(layout))
    print(json.dumps(results,indent=2))
    if args.out:
        (args.out/'source-check.json').write_text(json.dumps(results,indent=2)+'\n')
        (args.out/'index.html').write_text('<!doctype html><meta charset="utf-8"><title>Kehai layout review</title><style>body{font:16px sans-serif}img{width:48%;max-width:650px}</style><h1>Thin shelf footprints — source review</h1><p>Scene navigation, shelf stock and camera views still require Unity checks.</p>'+''.join(f'<img alt="{ident}" src="{ident}.svg">' for ident in results))

if __name__=='__main__':main()
