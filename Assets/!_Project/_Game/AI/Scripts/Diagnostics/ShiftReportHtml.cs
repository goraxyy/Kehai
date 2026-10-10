namespace Kehai.Karen
{
    // The shift report: one self-contained HTML file (no internet needed) with the store's
    // floor plan, a replay of the whole shift with a timeline, the moments worth a clip, a
    // clickable list of what happened, and the analysis. Same shapes and colours as the
    // in-game F1 map.
    public static class ShiftReportHtml
    {
        public static string Build(string json, int shift) =>
            Template.Replace("__TITLE__", "Shift " + shift + " — report")
                    .Replace("__ANTAGONIST__", GameNames.Antagonist)
                    .Replace("__DATA__", json.Replace("</", "<\\/"));

        const string Template = @"<!doctype html>
<html lang='en'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width, initial-scale=1'>
<title>__TITLE__</title>
<style>
:root{--bg:#0f1116;--panel:#171a21;--line:#262b35;--text:#e8e9ec;--dim:#8a909c;--you:#4dd2ff;--karen:#ff5454;--guess:#ffd640;--ok:#40c86e;--ask:#bb6bd9;--wait:#f28c32;--till:#f2c94c;--store:#b48cff}
*{box-sizing:border-box}
body{margin:0;background:var(--bg);color:var(--text);font:16px/1.45 -apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif}
header{padding:18px 24px;border-bottom:1px solid var(--line)}
h1{margin:0 0 4px;font-size:24px} h2{font-size:18px;margin:0 0 10px}
.sub{color:var(--dim)}
.cards{display:flex;flex-wrap:wrap;gap:10px;margin-top:14px}
.card{background:var(--panel);border:1px solid var(--line);border-radius:10px;padding:10px 14px;min-width:120px}
.card b{display:block;font-size:22px}
.card span{color:var(--dim);font-size:13px}
main{display:grid;grid-template-columns:minmax(0,1.6fr) minmax(300px,1fr);gap:16px;padding:16px 24px}
@media (max-width:900px){main{grid-template-columns:1fr}}
.box{background:var(--panel);border:1px solid var(--line);border-radius:12px;padding:14px}
canvas{width:100%;display:block;background:#12141a;border-radius:8px;touch-action:none}
.controls{display:flex;flex-wrap:wrap;align-items:center;gap:10px;margin-top:10px}
button,select{background:#232833;color:var(--text);border:1px solid var(--line);border-radius:8px;padding:6px 12px;font:inherit;cursor:pointer}
input[type=range]{flex:1;min-width:160px}
.time{font-variant-numeric:tabular-nums;min-width:110px}
.layers{display:flex;flex-wrap:wrap;gap:12px;margin-top:8px;color:var(--dim);font-size:14px}
.layers label{cursor:pointer}
#events{max-height:640px;overflow:auto;margin-top:8px}
.ev{display:flex;gap:10px;padding:6px 8px;border-radius:6px;cursor:pointer;font-size:15px}
.ev:hover{background:#20242e}
.ev .t{color:var(--dim);font-variant-numeric:tabular-nums;min-width:44px}
.ev .dot{width:10px;height:10px;border-radius:50%;margin-top:6px;flex:none}
.ev.now{background:#252b38}
.filters{display:flex;flex-wrap:wrap;gap:6px}
.filters button.on{background:#3a4254}
.legend{display:grid;grid-template-columns:repeat(auto-fill,minmax(160px,1fr));gap:6px 14px;margin-top:10px;font-size:14px;color:var(--dim)}
.legend i{display:inline-block;width:12px;height:12px;border-radius:50%;margin-right:8px;vertical-align:-1px}
.findings li{margin:6px 0}
.bars div{display:flex;align-items:center;gap:8px;margin:4px 0;font-size:14px}
.bars .bar{height:10px;background:var(--you);border-radius:5px}
.grid2{display:grid;grid-template-columns:repeat(auto-fill,minmax(260px,1fr));gap:16px}
.mbar{position:relative;height:20px;margin-top:10px;background:#1c2029;border:1px solid var(--line);border-radius:6px;cursor:pointer;overflow:hidden}
.mbar b{position:absolute;top:0;bottom:0;background:#ffd64038}
.mbar i{position:absolute;top:3px;bottom:3px;width:3px;border-radius:2px}
.mbar u{position:absolute;top:0;bottom:0;width:2px;background:#fff;left:0}
#moments{max-height:260px;overflow:auto;margin-bottom:14px}
.moment{padding:6px 8px;border-radius:6px;cursor:pointer;font-size:15px}
.moment:hover{background:#20242e}
.moment .sc{color:var(--guess);font-weight:600;margin-right:10px;font-variant-numeric:tabular-nums}
.moment .ids{color:var(--dim);font-size:13px}
</style></head><body>
<header><h1 id='title'></h1><div class='sub' id='subtitle'></div><div class='cards' id='cards'></div></header>
<main>
 <section class='box'>
  <canvas id='map' width='1200' height='900'></canvas>
  <div class='controls'>
   <button id='play'>Play</button>
   <select id='speed'><option value='1'>1×</option><option value='4' selected>4×</option><option value='16'>16×</option><option value='64'>64×</option></select>
   <input id='slider' type='range' min='0' max='1000' value='0'>
   <span class='time' id='time'></span>
  </div>
  <div class='mbar' id='mbar' title='Clip moments (gold) and the markers that made them: click to jump'><u id='mcur'></u></div>
  <div class='layers'>
   <label><input type='checkbox' id='lCone' checked> __ANTAGONIST__'s view</label>
   <label><input type='checkbox' id='lGuess' checked> Her guess</label>
   <label><input type='checkbox' id='lSound' checked> Sounds</label>
   <label><input type='checkbox' id='lCust' checked> Customers</label>
   <label><input type='checkbox' id='lTrail' checked> Trails</label>
   <label><input type='checkbox' id='lJobs' checked> Jobs</label>
   <label><input type='checkbox' id='lRooms' checked> Room names</label>
  </div>
  <div class='legend' id='legend'></div>
 </section>
 <section class='box'>
  <h2>Clip moments</h2>
  <div id='moments'></div>
  <h2>What happened</h2>
  <div class='filters' id='filters'></div>
  <div id='events'></div>
 </section>
</main>
<section style='padding:0 24px 24px'><div class='box'><h2>Analysis</h2><ul class='findings' id='findings'></ul><div class='grid2' id='tables'></div></div></section>
<script id='data' type='application/json'>__DATA__</script>
<script>
'use strict';
const D = JSON.parse(document.getElementById('data').textContent);
const P = D.plan || {bounds:[0,0,100,100],floor:[],shapes:[],rooms:[],pins:[],sections:{}};
const F = D.frames, E = D.events;
const END = F.length ? F[F.length-1][0] : 0;
const COL = {you:'#4dd2ff',karen:'#ff5454',guess:'#ffd640',shop:'#a0a8b2',till:'#f2c94c',wait:'#f28c32',ask:'#bb6bd9',follow:'#40c86e',leave:'#5f6670',puppet:'#e6283c',spill:'#96642d',empty:'#ff9628',warn:'#ffe63c',store:'#b48cff',other:'#c8c8c8'};
const CUST = ['shop','shop','till','wait','leave','puppet','ask','ask','follow','ask','puppet','puppet'];
const CUST_WORDS = ['shopping','using a bin','heading to the till','waiting at the till','leaving','looking around (sent by __ANTAGONIST__)','needs directions','talking to you','following you to a shelf','lost you — waiting','taken over by __ANTAGONIST__','not a real customer'];
const fmt = s => { s = Math.max(0,s); return Math.floor(s/60)+':'+String(Math.floor(s%60)).padStart(2,'0'); };
const $ = id => document.getElementById(id);

// Header
$('title').textContent = 'Shift ' + D.shift + (D.player ? ' — ' + D.player : '');
$('subtitle').textContent = (D.started||'') + ' · ' + fmt(D.length) + (D.clockedOut ? ' · clocked out' : ' · did not clock out') + (D.rung ? ' · __ANTAGONIST__ ' + D.rung : '');
const N = (D.analysis && D.analysis.numbers) || {};
[['Times __ANTAGONIST__ spotted you','spotted'],['Closest she got (m)','closest (m)'],['Catches','caught'],['Customers served','served'],['Walked (m)','walked (m)'],['Warning sounds','warnings']].forEach(([k,label])=>{
  if (!(k in N)) return; const v = N[k]; const c = document.createElement('div'); c.className='card';
  c.innerHTML = '<b>'+(v<0?'—':(v>=100?Math.round(v):v))+'</b><span>'+label+'</span>'; $('cards').appendChild(c); });

// Map geometry
const cv = $('map'), g = cv.getContext('2d');
const B = P.bounds, BW = B[2]-B[0], BH = B[3]-B[1];
function fit(){ const w = cv.clientWidth; cv.width = w*devicePixelRatio; cv.height = Math.round(w*BH/BW)*devicePixelRatio; cv.style.height = Math.round(w*BH/BW)+'px'; }
let S = 1;
const X = x => (x-B[0])*S, Y = y => (B[3]-y)*S;
function background(){
  S = cv.width/BW;
  g.fillStyle='#12141a'; g.fillRect(0,0,cv.width,cv.height);
  for (const t of P.floor){ g.fillStyle = t[6] ? '#2c3830' : '#3e434e'; g.beginPath(); g.moveTo(X(t[0]),Y(t[1])); g.lineTo(X(t[2]),Y(t[3])); g.lineTo(X(t[4]),Y(t[5])); g.closePath(); g.fill(); g.strokeStyle=g.fillStyle; g.lineWidth=1; g.stroke(); }
  const order = {Fixture:0,Shelf:1,Door:2,AutoDoor:2,Wall:3};
  for (const s of [...P.shapes].sort((a,b)=>order[a.k]-order[b.k])){
    g.beginPath(); for (let i=0;i<8;i+=2) (i?g.lineTo:g.moveTo).call(g,X(s.c[i]),Y(s.c[i+1])); g.closePath();
    if (s.k==='Wall'){ g.fillStyle='#d6dbe4'; g.fill(); g.strokeStyle='#d6dbe4'; g.lineWidth=Math.max(1.5,S*0.12); g.stroke(); }
    else if (s.k==='Shelf'){ const c = P.sections[s.s] || '#8a857a'; g.fillStyle=c+'99'; g.fill(); g.strokeStyle=c; g.lineWidth=1; g.stroke(); }
    else if (s.k==='Fixture'){ g.fillStyle='#60646e'; g.fill(); }
    else { g.fillStyle='#eba840'; g.fill(); g.strokeStyle='#eba840'; g.lineWidth=Math.max(2,S*0.15); g.stroke(); }
  }
}
function shelfCentre(bay){ const s = P.shapes.find(x=>x.b===bay); return s ? [(s.c[0]+s.c[4])/2,(s.c[1]+s.c[5])/2, s] : null; }
function outline(s, colour, w){ g.beginPath(); for (let i=0;i<8;i+=2) (i?g.lineTo:g.moveTo).call(g,X(s.c[i]),Y(s.c[i+1])); g.closePath(); g.strokeStyle=colour; g.lineWidth=w; g.stroke(); }
function dot(x,y,r,colour){ g.beginPath(); g.arc(X(x),Y(y),r,0,Math.PI*2); g.fillStyle=colour; g.fill(); }
function ring(x,y,r,colour,a,w){ g.globalAlpha=a; g.beginPath(); g.arc(X(x),Y(y),r,0,Math.PI*2); g.strokeStyle=colour; g.lineWidth=w||2; g.stroke(); g.globalAlpha=1; }
function label(x,y,text,colour,size,align){ g.font='600 '+size+'px sans-serif'; g.textAlign=align||'left'; g.textBaseline='middle'; g.fillStyle='#000a'; g.fillText(text,x+1,y+1); g.fillStyle=colour; g.fillText(text,x,y); }
function frameAt(t){ let lo=0,hi=F.length-1; while(lo<hi){ const m=(lo+hi+1)>>1; if (F[m][0]<=t) lo=m; else hi=m-1; } return F[lo]; }

let T = 0, playing = false;
function draw(){
  if (!F.length) return;
  background();
  const f = frameAt(T), px = S, u = devicePixelRatio || 1, fs = Math.round(Math.max(13, cv.clientWidth/58)*u);
  const L = id => $(id).checked;
  if (L('lRooms')) for (const r of P.rooms) label(X(r.x),Y(r.y),r.t,'#ffffffb0',fs,'center');
  if (!f[17]) { g.fillStyle='#00001088'; g.fillRect(0,0,cv.width,cv.height); label(cv.width/2, fs*1.5, 'LIGHTS OUT', COL.warn, fs*1.4, 'center'); }
  const cust=f[18], spills=f[19], empty=f[20], bins=f[21], bags=f[22], locked=f[23], props=f[24];
  if (L('lJobs')){
    for (const b of empty){ const s = shelfCentre(b); if (s) outline(s[2], COL.empty, Math.max(2*u,px*0.2)); }
    for (const s of spills) dot(s[0],s[1],Math.max(5*u,px*0.7),COL.spill);
    for (const b of bins){ const k = b[3]? Math.min(1,b[2]/b[3]):0; g.fillStyle = 'rgb('+Math.round(77+165*k)+','+Math.round(204-140*k)+',102)'; const r=Math.max(4*u,px*0.45); g.fillRect(X(b[0])-r,Y(b[1])-r,r*2,r*2); }
    for (const b of bags){ g.fillStyle='#403348'; const r=Math.max(3*u,px*0.35); g.fillRect(X(b[0])-r,Y(b[1])-r,r*2,r*2); }
    for (const d of locked){ label(X(d[0]),Y(d[1]),'✕',COL.karen,fs*1.3,'center'); }
    for (const p of props){ if (p[0]===0){ g.fillStyle='#8c5a26'; const r=Math.max(5*u,px*0.8); g.fillRect(X(p[1])-r,Y(p[2])-r,r*2,r*2);} else if (p[0]===1){ g.globalAlpha=0.35; dot(p[1],p[2],px*p[3],'#dfe6ff'); g.globalAlpha=1;} else if (p[0]===2){ g.fillStyle='#fff'; g.fillRect(X(p[1])-4*u,Y(p[2])-4*u,8*u,8*u);} else dot(p[1],p[2],Math.max(3*u,px*0.3),'#cc9966'); }
  }
  if (L('lTrail')){
    const from = Math.max(0,T-60);
    g.lineWidth = Math.max(1.5*u,px*0.12);
    for (const [xi,yi,colour] of [[1,2,COL.you],[8,9,COL.karen]]){
      g.strokeStyle=colour; g.globalAlpha=0.55; g.beginPath(); let first=true;
      for (const fr of F){ if (fr[0]<from) continue; if (fr[0]>T) break; if (xi===8 && !fr[7]) continue; (first?g.moveTo:g.lineTo).call(g,X(fr[xi]),Y(fr[yi])); first=false; }
      g.stroke(); g.globalAlpha=1;
    }
  }
  if (L('lSound')) for (const e of E){ if (e.k!=='sound' || e.x===undefined) continue; const age=T-e.t; if (age<0) break; const life=(e.s||'').indexOf('footsteps')>=0?0.8:1.6; if (age>life) continue;
      const colour = e.w==='you'?COL.you: e.w==='__ANTAGONIST__'?COL.karen: e.w==='a customer'?COL.other:COL.store;
      ring(e.x,e.y,Math.max(5*u,px*Math.min(e.r||5,25)*(0.25+0.75*age/life)),colour,0.8*(1-age/life),1.5*u); }
  for (const e of E){ if (e.k!=='Warning' || e.x===undefined) continue; const age=T-e.t; if (age<0) break; if (age>3.5) continue;
      ring(e.x,e.y,Math.max(12*u,px*4),COL.warn,0.8,3*u); label(X(e.x),Y(e.y),'!',COL.warn,fs*1.5,'center'); }
  if (L('lCust')) for (const c of cust){
      const colour = COL[CUST[c[4]]] || COL.shop, r = Math.max(6*u,px*0.5);
      if (c[6]>=0){ const s = shelfCentre(c[6]); if (s){ g.setLineDash([6*u,5*u]); g.strokeStyle=COL.ask; g.lineWidth=2*u; g.beginPath(); g.moveTo(X(c[1]),Y(c[2])); g.lineTo(X(s[0]),Y(s[1])); g.stroke(); g.setLineDash([]); outline(s[2],COL.ask,Math.max(2.5*u,px*0.25)); } }
      if (c[4]===8){ g.setLineDash([8*u,8*u]); g.strokeStyle=COL.follow; g.lineWidth=2*u; g.beginPath(); g.moveTo(X(c[1]),Y(c[2])); g.lineTo(X(f[1]),Y(f[2])); g.stroke(); g.setLineDash([]); }
      dot(c[1],c[2],r+1.5*u,'#000c'); dot(c[1],c[2],r,colour);
      if (c[4]===6||c[4]===9) label(X(c[1]),Y(c[2])-r*2.2,'?',COL.ask,fs,'center');
      if (c[4]===3 && c[5]>=20) label(X(c[1])+r*1.6,Y(c[2]),c[5]+'s',c[5]>=60?COL.karen:COL.wait,Math.round(fs*0.8));
      if (c[4]>=10) ring(c[1],c[2],r*1.9,COL.puppet,1,2*u);
  }
  if (f[7]){
    if (L('lGuess') && f[16]>0.05){ const r = px*(12-9*Math.min(1,f[16])); g.setLineDash([7*u,6*u]); ring(f[14],f[15],r,COL.guess,0.9,2.5*u); g.setLineDash([]); label(X(f[14]),Y(f[15])+r+fs*0.7,'her guess',COL.guess,Math.round(fs*0.8),'center'); }
    if (L('lCone')){ const a=(f[10]-90)*Math.PI/180; g.beginPath(); g.moveTo(X(f[8]),Y(f[9])); g.arc(X(f[8]),Y(f[9]),px*18,a-Math.PI/3,a+Math.PI/3); g.closePath(); g.fillStyle = f[12]? '#ff54545e':'#ff545430'; g.fill(); }
    const kx=X(f[8]),ky=Y(f[9]),kr=Math.max(8*u,px*0.8); g.save(); g.translate(kx,ky); g.rotate(Math.PI/4); g.fillStyle = f[13] && (Math.floor(T*4)%2) ? '#fff':COL.karen; g.fillRect(-kr/1.4,-kr/1.4,kr*1.4,kr*1.4); g.restore();
    label(kx+kr*1.3,ky,f[13]?'__ANTAGONIST__ — chasing':'__ANTAGONIST__',COL.karen,fs);
  }
  const ux=X(f[1]),uy=Y(f[2]),ur=Math.max(10*u,px*1.1); g.save(); g.translate(ux,uy); g.rotate(f[3]*Math.PI/180);
  g.beginPath(); g.moveTo(0,-ur); g.lineTo(ur*0.62,ur*0.8); g.lineTo(0,ur*0.35); g.lineTo(-ur*0.62,ur*0.8); g.closePath(); g.fillStyle=COL.you; g.fill(); g.strokeStyle='#fff'; g.lineWidth=2*u; g.stroke(); g.restore();
  label(ux+ur*1.2,uy,f[5]?'You (lectured)':'You',COL.you,fs);
  $('time').textContent = fmt(T)+' / '+fmt(END);
  $('slider').value = END? Math.round(1000*T/END) : 0;
  $('mcur').style.left = (END ? 100*T/END : 0)+'%';
  highlightEvents();
}

// Timeline
let last = performance.now();
function tick(now){ const dt=(now-last)/1000; last=now; if (playing){ T=Math.min(END,T+dt*Number($('speed').value)); if (T>=END){ playing=false; $('play').textContent='Play'; } draw(); } requestAnimationFrame(tick); }
$('play').onclick = ()=>{ if (T>=END) T=0; playing=!playing; $('play').textContent = playing?'Pause':'Play'; };
$('slider').oninput = e=>{ T = END*e.target.value/1000; draw(); };
document.querySelectorAll('.layers input').forEach(i=>i.onchange=draw);
window.addEventListener('resize',()=>{ fit(); draw(); });

// Events
const KIND = {job:['Your work',COL.follow],customer:['Customers',COL.ask],store:['The store',COL.store],Seen:['__ANTAGONIST__ saw you',COL.karen],Chase:['Chases',COL.karen],Warning:['Warnings',COL.warn],Plan:['Her plans','#ff7373'],Heard:['What she heard','#ff9980'],Guess:['Her guesses',COL.guess],Mood:['Pace','#b3bfd9'],Learned:['What she learned','#d99aff'],Blink:['Blinks',COL.you]};
const active = new Set(Object.keys(KIND));
function renderFilters(){ const box=$('filters'); box.innerHTML='';
  for (const k of Object.keys(KIND)){ if (!E.some(e=>e.k===k)) continue; const b=document.createElement('button'); b.textContent=KIND[k][0]; b.className=active.has(k)?'on':''; b.onclick=()=>{ active.has(k)?active.delete(k):active.add(k); renderFilters(); renderEvents(); }; box.appendChild(b); } }
let rows = [];
function renderEvents(){ const box=$('events'); box.innerHTML=''; rows=[];
  for (const e of E){ if (!KIND[e.k] || !active.has(e.k)) continue;
    const d=document.createElement('div'); d.className='ev'; d.innerHTML='<span class=t>'+fmt(e.t)+'</span><span class=dot style=background:'+KIND[e.k][1]+'></span><span></span>';
    d.lastChild.textContent = e.s || ''; d.onclick=()=>{ T=Math.max(0,e.t-1.5); playing=false; $('play').textContent='Play'; draw(); };
    box.appendChild(d); rows.push([e.t,d]); } }
function highlightEvents(){ let cur=null; for (const [t,d] of rows){ d.classList.remove('now'); if (t<=T) cur=d; } if (cur) cur.classList.add('now'); }

// Clip moments: gold stretches on the strip, the markers that made them as ticks, and the list
const MK = D.markers || [], MO = D.moments || [];
function seek(t){ T=Math.max(0,Math.min(END,t)); playing=false; $('play').textContent='Play'; draw(); }
if (END){
  const bar=$('mbar');
  for (const m of MO){ const b=document.createElement('b'); b.style.left=(100*m.start/END)+'%'; b.style.width=Math.max(0.3,100*(m.end-m.start)/END)+'%'; b.title=fmt(m.start)+'–'+fmt(m.end)+' · score '+Math.round(m.score); bar.appendChild(b); }
  for (const k of MK){ const i=document.createElement('i'); const w=Math.min(1,(k.weight||0)/10);
    i.style.left='calc('+(100*k.t/END)+'% - 1px)'; i.style.background = k.id==='manual_bug' ? COL.karen : 'rgba(255,214,64,'+(0.35+0.65*w)+')';
    i.title=fmt(k.t)+' · '+k.id+(k.text?' — '+k.text:''); bar.appendChild(i); }
  bar.onclick = e=>{ const r=bar.getBoundingClientRect(); seek(END*(e.clientX-r.left)/r.width); };
}
if (!MO.length) $('moments').innerHTML='<div class=sub>No clip moments in this shift yet.</div>';
for (const m of MO.slice(0,20)){ const d=document.createElement('div'); d.className='moment';
  d.innerHTML='<span class=sc></span><span></span><div class=ids></div>';
  d.children[0].textContent = Math.round(m.score) + (m.kept ? ' ★' : '');
  d.children[1].textContent = fmt(m.start)+'–'+fmt(m.end)+'  '+((m.captionSeed && m.captionSeed[0]) || '');
  d.children[2].textContent = [...new Set(m.markers.map(x=>x.id))].join(' · ') + (m.tags && m.tags.length ? '  —  '+m.tags.join(', ') : '');
  d.onclick=()=>seek(m.start); $('moments').appendChild(d); }

// Legend
[['You',COL.you],['__ANTAGONIST__ (with her view cone)',COL.karen],['Her guess of where you are',COL.guess],['Customer shopping',COL.shop],['Heading to the till',COL.till],['Waiting at the till (seconds)',COL.wait],['Wants directions (line to the shelf)',COL.ask],['Following you',COL.follow],['__ANTAGONIST__\'s puppet',COL.puppet],['Your noise (the ring shows how far it carried)',COL.you],['__ANTAGONIST__\'s noise',COL.karen],['A customer\'s noise',COL.other],['The store\'s noise (doors, machines)',COL.store],['Warning — a trick is coming',COL.warn],['Spill',COL.spill],['Empty shelf',COL.empty]]
 .forEach(([t,c])=>{ const d=document.createElement('div'); d.innerHTML='<i style=background:'+c+'></i>'; d.appendChild(document.createTextNode(t)); $('legend').appendChild(d); });

// Analysis
const A = D.analysis || {findings:[]};
for (const f of A.findings||[]){ const li=document.createElement('li'); li.textContent=f; $('findings').appendChild(li); }
function table(title, rows, unit){ if (!rows || !rows.length) return; const box=document.createElement('div'); box.className='bars';
  const max=Math.max(...rows.map(r=>r[1])); box.innerHTML='<h2>'+title+'</h2>';
  for (const [k,v] of rows.slice(0,10)){ const d=document.createElement('div'); d.innerHTML='<span style=min-width:190px></span><span class=bar style=width:'+Math.max(4,160*v/max)+'px></span><span>'+(unit==='time'?fmt(v):v)+'</span>'; d.firstChild.textContent=k; box.appendChild(d); }
  $('tables').appendChild(box); }
table('Where you spent the shift', A.areas, 'time'); table('Jobs you did', A.jobs); table('What __ANTAGONIST__ did', A.tricks); table('Noises you made', A.noises);

fit(); renderFilters(); renderEvents(); draw(); requestAnimationFrame(tick);
</script></body></html>";
    }
}
