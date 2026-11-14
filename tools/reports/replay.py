"""Offline replay of actual ShiftRecording JSON plus optional exact decision sidecars."""
from __future__ import annotations

from .common import read_json, source
from .html import details, e, embedded, page
from .network import inspect


def load_record(path, decisions=None, model=None):
    record = read_json(path)
    frames = record.get('frames', [])
    if not frames or any(not isinstance(f, list) or len(f) < 3 for f in frames):
        raise ValueError('replay requires actual ShiftRecording frames [t, px, pz, ...]')
    import math
    if any(not all(type(f[i]) in (int, float) and math.isfinite(f[i])
                   for i in (0, 1, 2, *([7, 8, 9] if len(f) > 9 else []))) for f in frames):
        raise ValueError('frames contain nonfinite/non-numeric coordinates')
    if any(b[0] < a[0] for a, b in zip(frames, frames[1:])):
        raise ValueError('recording timestamps out of order')
    out = {'label': record.get('model') or record.get('bot') or record.get('player') or 'recorded shift',
           'seed': record.get('seed'), 'shift': record.get('shift'), 'layout': record.get('layout'),
           'frames': frames, 'map': record.get('map') or record.get('plan'), 'decisions': [], 'source': source(path),
           'missing': []}
    if out['seed'] is None or out['shift'] is None:
        out['missing'].append('seed/shift metadata unavailable; paired replay cannot be verified')
    if not out['map']:
        out['missing'].append('recorded map geometry unavailable; view shows coordinate trajectories only')
    if not decisions:
        out['missing'].append('per-decision inputs, probabilities, activations and saliency unavailable; position frames cannot supply them')
        return out
    trace = read_json(decisions)
    if trace.get('seed') != out['seed'] or trace.get('shift') != out['shift']:
        raise ValueError('decision trace seed/shift differs from replay')
    out['decision_source'] = source(decisions)
    if any(b['t'] < a['t'] for a, b in zip(trace.get('decisions', []), trace.get('decisions', [])[1:])):
        raise ValueError('decision timestamps out of order')
    net = read_json(model) if model else None
    exact = net is not None and trace.get('model_sha256') == source(model)['sha256']
    for d in trace.get('decisions', []):
        if type(d.get('t')) not in (int, float) or not frames[0][0] <= d['t'] <= frames[-1][0]:
            raise ValueError('decision timestamp outside recorded shift')
        row = dict(d)
        probs = row.get('probabilities')
        if probs is not None and (not isinstance(probs, list) or not probs or
             any(type(p) not in (int, float) or not math.isfinite(p) or not 0 <= p <= 1 for p in probs) or
             abs(sum(probs)-1) > 1e-4):
            raise ValueError('recorded probabilities must be a normalized finite distribution')
        if probs is None:
            row['probabilities_missing'] = 'recorded choice probabilities unavailable; raw logits are not legal-pair chances'
        row.pop('saliency', None)  # Unverified supplied rankings are never presented as computed evidence.
        row['inspection'] = None
        row['missing'] = 'saliency/activations unavailable: exact model hash, inputs and chosen head required'
        if exact and 'inputs' in d and type(d.get('branch')) is int and type(d.get('choice')) is int:
            try:
                row['inspection'] = inspect(net, d['inputs'], d['branch'], d['choice'])
                row['missing'] = None
            except (KeyError, ValueError, IndexError, TypeError, OverflowError) as ex:
                row['missing'] = 'unsupported network/decision: ' + str(ex)
        out['decisions'].append(row)
    if not out['decisions']:
        out['missing'].append('decision trace has no decisions')
    return out


def render(records):
    if len(records) > 2:
        raise ValueError('at most two replay records')
    if len(records) == 2:
        for key in ('seed', 'shift', 'layout'):
            if records[0][key] is None or records[0][key] != records[1][key]:
                raise ValueError('paired replay requires matching explicit seed, shift and layout')
    body = '<p>Blue: player. Coral: Karen when her recorded presence flag is on. Scrubbing uses recorded samples; timestamps shown per panel.</p>'
    body += '<label>Recorded time <output id="time"></output><input id="scrub" type="range" step="0.1"></label><div class="grid">'
    for i, r in enumerate(records):
        body += f'<section><h2>{e(r["label"])}</h2><p>Seed {e(r["seed"])} · shift {e(r["shift"])} · layout {e(r["layout"])}</p>'
        body += ''.join(f'<p class="bad">{e(m)}</p>' for m in r['missing'])
        body += f'<canvas id="map{i}" width="520" height="420"></canvas><p id="frame{i}"></p><h3>Recorded decision / network</h3><canvas id="net{i}" width="520" height="180"></canvas><pre id="decision{i}"></pre>'
        body += details('Recording provenance', {k: v for k, v in r.items() if k.endswith('source')}) + '</section>'
    body += '</div>'
    script = 'const records=' + embedded(records) + ';' + r'''
const scrub=document.getElementById('scrub');
scrub.min=Math.min(...records.map(r=>r.frames[0][0]));scrub.max=Math.max(...records.map(r=>r.frames.at(-1)[0]));scrub.value=scrub.min;
function paint(){const t=Number(scrub.value);document.getElementById('time').textContent=t.toFixed(1)+' s';
records.forEach((r,i)=>{const c=document.getElementById('map'+i), ctx=c.getContext('2d');ctx.clearRect(0,0,c.width,c.height);
const coords=r.frames.flatMap(f=>f.length>10&&f[7]?[ [f[1],f[2]],[f[8],f[9]] ]:[[f[1],f[2]]]);
const xs=coords.map(p=>p[0]),zs=coords.map(p=>p[1]);const bounds=r.map&&r.map.bounds;let x0=bounds?bounds[0]:xs.reduce((a,b)=>Math.min(a,b),Infinity),x1=bounds?bounds[2]:xs.reduce((a,b)=>Math.max(a,b),-Infinity),z0=bounds?bounds[1]:zs.reduce((a,b)=>Math.min(a,b),Infinity),z1=bounds?bounds[3]:zs.reduce((a,b)=>Math.max(a,b),-Infinity);
const scale=Math.min(470/Math.max(x1-x0,1),370/Math.max(z1-z0,1));const X=x=>25+(x-x0)*scale,Z=z=>395-(z-z0)*scale;
if(r.map&&Array.isArray(r.map.floor)){ctx.fillStyle='#263c48';r.map.floor.forEach(s=>{ctx.beginPath();ctx.moveTo(X(s[0]),Z(s[1]));ctx.lineTo(X(s[2]),Z(s[3]));ctx.lineTo(X(s[4]),Z(s[5]));ctx.closePath();ctx.fill();});}
if(r.map&&Array.isArray(r.map.shapes)){ctx.fillStyle='#4a5a63';r.map.shapes.forEach(s=>{if(!s.c)return;ctx.beginPath();for(let j=0;j<s.c.length;j+=2){j?ctx.lineTo(X(s.c[j]),Z(s.c[j+1])):ctx.moveTo(X(s.c[j]),Z(s.c[j+1]));}ctx.closePath();ctx.fill();});}
if(r.map&&Array.isArray(r.map.segments)){ctx.strokeStyle='#788993';r.map.segments.forEach(s=>{ctx.beginPath();ctx.moveTo(X(s[0]),Z(s[1]));ctx.lineTo(X(s[2]),Z(s[3]));ctx.stroke();});}
const within=t>=r.frames[0][0]&&t<=r.frames.at(-1)[0];const fs=r.frames.filter(f=>f[0]<=t);
ctx.strokeStyle='#5db8eb';ctx.beginPath();fs.forEach((f,j)=>j?ctx.lineTo(X(f[1]),Z(f[2])):ctx.moveTo(X(f[1]),Z(f[2])));ctx.stroke();
const f=within?fs.at(-1):null;document.getElementById('frame'+i).textContent=f?'Recorded frame: '+f[0]+' s':'No recorded frame at this time';
function dot(x,z,color){ctx.fillStyle=color;ctx.beginPath();ctx.arc(X(x),Z(z),6,0,2*Math.PI);ctx.fill();}
if(f){dot(f[1],f[2],'#5db8eb');if(f.length>10&&f[7])dot(f[8],f[9],'#ff9e8f');}
const d=within?r.decisions.filter(d=>d.t<=t).at(-1):null, view=document.getElementById('decision'+i);
view.textContent=d?JSON.stringify(d,null,2):'Decision inputs, probabilities, activations and saliency unavailable at this time.';
const nc=document.getElementById('net'+i),nx=nc.getContext('2d');nx.clearRect(0,0,nc.width,nc.height);
if(d&&d.inspection){const layers=[...d.inspection.layers,d.inspection.logits];layers.forEach((layer,j)=>{const n=Math.min(layer.length,24);for(let k=0;k<n;k++){let value=layer[k];nx.fillStyle=value<0?'#ff9e8f':'#72c9c1';nx.globalAlpha=.2+.8*Math.tanh(Math.abs(value));nx.beginPath();nx.arc(20+j*470/Math.max(layers.length-1,1),10+k*155/Math.max(n-1,1),3,0,2*Math.PI);nx.fill();}nx.globalAlpha=1;nx.fillStyle='#dce5ed';nx.fillText(layer.length+' units',10+j*470/Math.max(layers.length-1,1),178);});}
});}scrub.oninput=paint;paint();'''
    return page('Recorded shift replay', body, script)
