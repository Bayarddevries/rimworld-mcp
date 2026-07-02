using System.Net;
using System.Text;

namespace RimworldMcp
{
    /// <summary>
    /// Serves the mobile-responsive colony dashboard HTML page.
    /// GET /dashboard
    /// </summary>
    public static class DashboardHandler
    {
        public static string ServeDashboard(HttpListenerRequest req)
        {
            return DashboardHtml;
        }

        private const string DashboardHtml = @"<!DOCTYPE html>
<html lang=""en"">
<head>
<meta charset=""UTF-8"">
<meta name=""viewport"" content=""width=device-width,initial-scale=1,user-scalable=no"">
<title>RimWorld — Colony Dashboard</title>
<style>
*{box-sizing:border-box;margin:0;padding:0}
body{font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;background:#0d1117;color:#c9d1d9;padding:12px;min-height:100vh}
h1{font-size:18px;font-weight:600;color:#58a6ff;margin-bottom:8px}
.controls{display:flex;gap:6px;margin-bottom:12px;flex-wrap:wrap}
.controls button{background:#21262d;border:1px solid #30363d;color:#c9d1d9;padding:8px 14px;border-radius:6px;font-size:13px;cursor:pointer;font-weight:500;transition:all .15s}
.controls button:hover{background:#30363d;border-color:#58a6ff}
.controls button.active{background:#1f6feb;border-color:#1f6feb;color:#fff}
.pause-btn{background:#da3633!important;border-color:#da3633!important;color:#fff!important}
.pause-btn.paused{background:#238636!important;border-color:#238636!important}
.grid{display:grid;grid-template-columns:1fr 1fr;gap:8px;margin-bottom:12px}
.card{background:#161b22;border:1px solid #30363d;border-radius:8px;padding:10px}
.card h3{font-size:11px;text-transform:uppercase;color:#8b949e;margin-bottom:4px;letter-spacing:.5px}
.card .val{font-size:20px;font-weight:600;color:#f0f6fc}
.card .sub{font-size:11px;color:#8b949e;margin-top:2px}
.full{grid-column:1/-1}
.section-title{font-size:13px;font-weight:600;color:#8b949e;margin:12px 0 6px;text-transform:uppercase;letter-spacing:.5px}
.pawn-card{background:#161b22;border:1px solid #30363d;border-radius:8px;padding:8px 10px;margin-bottom:6px;display:flex;justify-content:space-between;align-items:center}
.pawn-info{flex:1}
.pawn-name{font-size:14px;font-weight:600;color:#f0f6fc}
.pawn-status{font-size:11px;color:#8b949e}
.pawn-mood{font-size:12px;font-weight:500;min-width:50px;text-align:right}
.pawn-mood.bad{color:#da3633}
.pawn-mood.ok{color:#d29922}
.pawn-mood.good{color:#3fb950}
.event-item{padding:6px 0;border-bottom:1px solid #21262d;font-size:12px;line-height:1.4}
.event-item:last-child{border-bottom:0}
.event-time{color:#8b949e;font-size:10px}
.event-crit{color:#da3633;font-weight:600}
.event-warn{color:#d29922}
.event-info{color:#58a6ff}
.loading{color:#8b949e;text-align:center;padding:20px;font-size:13px}
.error{color:#da3633;text-align:center;padding:10px;font-size:12px}
.last-update{text-align:right;font-size:10px;color:#484f58;margin-bottom:8px}
.refresh-btn{background:transparent;border:1px solid #30363d;color:#8b949e;padding:4px 10px;border-radius:4px;cursor:pointer;font-size:11px}
.refresh-btn:hover{color:#58a6ff;border-color:#58a6ff}
</style>
</head>
<body>
<div style=""display:flex;justify-content:space-between;align-items:center;margin-bottom:8px"">
<h1>🌍 RimWorld Colony</h1>
<button class=""refresh-btn"" onclick=""fetchAll()"">↻ Refresh</button>
</div>

<div class=""controls"" id=""timeControls"">
<button onclick=""setTime('pause')"" id=""btnPause"" class=""pause-btn paused"">⏸ Pause</button>
<button onclick=""setTime('play')"" id=""btnPlay"">▶ 1×</button>
<button onclick=""setTime('fast')"" id=""btnFast"">▶▶ 2×</button>
<button onclick=""setTime('superfast')"" id=""btnSuper"">▶▶▶ 3×</button>
</div>

<div class=""grid"" id=""statsGrid""></div>

<div class=""section-title"">🧑 Pawns</div>
<div id=""pawnList""></div>

<div class=""section-title"">📡 Recent Events</div>
<div id=""eventList""></div>

<div class=""last-update"" id=""lastUpdate""></div>

<script>
let isPaused = true;
let currentSpeed = 0;

function shortName(name) {
    return name.length > 14 ? name.substring(0, 12) + '…' : name;
}

async function fetchAll() {
    await Promise.all([fetchStats(), fetchPawns(), fetchEvents(), fetchTime()]);
    document.getElementById('lastUpdate').textContent = 'Updated: ' + new Date().toLocaleTimeString();
}

async function fetchTime() {
    try {
        let r = await fetch('/api/colony/paused');
        let d = await r.json();
        if (!d.success) return;
        isPaused = d.data.paused === 'true';
        currentSpeed = parseInt(d.data.speed) || 0;
        updateTimeButtons();
    } catch(e) {}
}

function updateTimeButtons() {
    document.getElementById('btnPause').className = 'pause-btn' + (isPaused ? ' paused' : '');
    document.getElementById('btnPause').textContent = isPaused ? '⏸ Paused' : '⏸ Pause';
    document.getElementById('btnPlay').classList.toggle('active', !isPaused && currentSpeed === 1);
    document.getElementById('btnFast').classList.toggle('active', currentSpeed === 2);
    document.getElementById('btnSuper').classList.toggle('active', currentSpeed >= 3);
}

async function setTime(action) {
    try {
        let r = await fetch('/api/colony/time', {method:'POST', headers:{'Content-Type':'application/json'}, body:JSON.stringify({action})});
        let d = await r.json();
        if (d.success) {
            isPaused = action === 'pause';
            currentSpeed = d.data.speed || 0;
            updateTimeButtons();
        }
    } catch(e) {}
}

async function fetchStats() {
    try {
        let r = await fetch('/api/colony/overview');
        let d = await r.json();
        if (!d.success) { document.getElementById('statsGrid').innerHTML='<div class=""card full error"">Could not load colony data</div>'; return; }
        let o = d.data;
        document.getElementById('statsGrid').innerHTML = `
            <div class=""card""><h3>Colonists</h3><div class=""val"">${o.colonists}</div><div class=""sub"">${o.prisoners} prisoners</div></div>
            <div class=""card""><h3>Wealth</h3><div class=""val"">${(parseInt(o.wealth_total)/1000).toFixed(1)}K</div><div class=""sub"">${o.storyteller} · ${o.difficulty}</div></div>
            <div class=""card""><h3>Day</h3><div class=""val"">${o.day_of_season}</div><div class=""sub"">${o.season} · Year ${o.year}</div></div>
            <div class=""card""><h3>Weather</h3><div class=""val"" style=""font-size:14px"">${o.weather}</div><div class=""sub"">${o.biome}</div></div>
        `;
    } catch(e) { document.getElementById('statsGrid').innerHTML=`<div class=""card full error"">Error: ${e.message}</div>`; }
}

async function fetchPawns() {
    try {
        let r = await fetch('/api/pawns');
        let d = await r.json();
        if (!d.success) { document.getElementById('pawnList').innerHTML='<div class=""loading"">No pawns available</div>'; return; }
        let pawns = d.data;
        if (!Array.isArray(pawns) || pawns.length === 0) { document.getElementById('pawnList').innerHTML='<div class=""loading"">No colonists</div>'; return; }
        document.getElementById('pawnList').innerHTML = pawns.map(p => {
            let moodPct = p.mood !== undefined ? parseFloat(p.mood) * 100 : null;
            let moodClass = moodPct === null ? '' : moodPct < 30 ? 'bad' : moodPct < 60 ? 'ok' : 'good';
            let moodText = moodPct === null ? '' : Math.round(moodPct) + '%';
            let drafted = p.drafted === 'true' || p.drafted === true;
            let job = p.current_job || 'Idle';
            let healthPct = p.health !== undefined ? Math.round(parseFloat(p.health) * 100) : '';
            return `<div class=""pawn-card"">
                <div class=""pawn-info"">
                    <div class=""pawn-name"">${shortName(p.name || 'Unknown')} ${drafted ? '⚔️' : ''}</div>
                    <div class=""pawn-status"">${job} · ❤️ ${healthPct}%</div>
                </div>
                <div class=""pawn-mood ${moodClass}"">${moodText || ''}</div>
            </div>`;
        }).join('');
    } catch(e) { document.getElementById('pawnList').innerHTML=`<div class=""error"">Error loading pawns</div>`; }
}

async function fetchEvents() {
    try {
        let r = await fetch('/api/events/history');
        let d = await r.json();
        if (!d.success) { document.getElementById('eventList').innerHTML='<div class=""loading"">No events</div>'; return; }
        let evts = d.data;
        if (!Array.isArray(evts) || evts.length === 0) { document.getElementById('eventList').innerHTML='<div class=""loading"">No events recorded</div>'; return; }
        let recent = evts.slice(-20).reverse();
        document.getElementById('eventList').innerHTML = recent.map(e => {
            let sev = e.severity || 'info';
            let cls = sev === 'critical' ? 'event-crit' : sev === 'warning' ? 'event-warn' : 'event-info';
            let icon = sev === 'critical' ? '🔴' : sev === 'warning' ? '🟡' : '🔵';
            return `<div class=""event-item""><span class=""${cls}"">${icon} ${e.description || e.type || 'Unknown'}</span> <span class=""event-time"">${e.timestamp || ''}</span></div>`;
        }).join('');
    } catch(e) { document.getElementById('eventList').innerHTML=`<div class=""error"">Error loading events</div>`; }
}

// Auto-refresh
setInterval(fetchAll, 10000);
fetchAll();
</script>
</body>
</html>";
    }
}
