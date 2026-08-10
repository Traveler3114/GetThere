const BASE = '';
let allVehicles = [];
let paused = false;
let timer = null;
let intervalSec = 15;
let abortController = null;

async function loadVehicles() {
  if (paused) return;
  if (abortController) abortController.abort();
  abortController = new AbortController();
  document.getElementById('loading').classList.remove('d-none');
  document.getElementById('error').classList.add('d-none');
  try {
    const r = await fetch(BASE + '/realtime/vehicles', { signal: abortController.signal });
    if (!r.ok) { showError('Failed to load: ' + (r.statusText || 'Unknown error')); return; }
    const j = await r.json();
    allVehicles = j || [];
    document.getElementById('loading').classList.add('d-none');
    document.getElementById('content').classList.remove('d-none');
    document.getElementById('statusBar').classList.remove('d-none');
    filterVehicles();
    document.getElementById('statusBar').textContent = `Updated: ${new Date().toLocaleTimeString()} | ${allVehicles.length} vehicle(s) total`;
  } catch(e) {
    if (e.name === 'AbortError') return;
    document.getElementById('loading').classList.add('d-none');
    showError('Failed to load vehicles: ' + e.message);
  }
}

function filterVehicles() {
  const q = document.getElementById('filterOperator').value.toLowerCase();
  let filtered = allVehicles;
  if (q) filtered = filtered.filter(v => (v.routeId||'').toLowerCase().includes(q) || (v.tripId||'').toLowerCase().includes(q) || (v.vehicleId||'').toLowerCase().includes(q));
  renderVehicles(filtered);
}

function renderVehicles(list) {
  if (!list.length) {
    document.getElementById('content').innerHTML = '<div class="alert alert-info">No vehicles currently active.</div>';
    return;
  }
  const rows = list.map(v => `<tr>
    <td><code>${esc(v.vehicleId)}</code></td>
    <td>${esc(v.routeId||'-')}</td>
    <td>${esc(v.tripId||'-')}</td>
    <td class="small">${typeof v.latitude === 'number' && typeof v.longitude === 'number'
      ? v.latitude.toFixed(4) + ', ' + v.longitude.toFixed(4) : '-'}</td>
    <td>${v.bearing != null ? v.bearing.toFixed(0) + '&deg;' : '-'}</td>
    <td class="small">${v.lastUpdated ? new Date(v.lastUpdated).toLocaleTimeString() : '-'}</td>
  </tr>`).join('');
  document.getElementById('content').innerHTML = `<div class="table-responsive"><table class="table table-striped table-hover table-sm"><thead class="table-dark"><tr><th>Vehicle ID</th><th>Route</th><th>Trip</th><th>Location</th><th>Bearing</th><th>Last Updated</th></tr></thead><tbody>${rows}</tbody></table></div><small class="text-muted">${list.length} vehicle(s) shown</small>`;
}

function togglePause() {
  paused = !paused;
  document.getElementById('pauseBtn').innerHTML = paused ? '<i class="bi bi-play-fill"></i> Resume' : '<i class="bi bi-pause-fill"></i> Pause';
  document.getElementById('liveIndicator').style.opacity = paused ? '0.5' : '1';
  if (!paused) loadVehicles();
}

function updateInterval() {
  // Radix and a floor: a NaN interval makes setInterval fire as fast as the browser allows, which
  // on this page means hammering /realtime/vehicles.
  const parsed = parseInt(document.getElementById('refreshInterval').value, 10);
  intervalSec = Number.isFinite(parsed) && parsed > 0 ? parsed : 15;
  startTimer();
}

function startTimer() {
  if (timer) clearInterval(timer);
  timer = setInterval(loadVehicles, intervalSec * 1000);
}

function showError(msg) {
  // Hides the spinner too. The loader bails out of its `if (!r.ok) ... return` path before reaching
  // its own hide, so a failed request used to leave the page showing a spinner and an error at once.
  document.getElementById('loading')?.classList.add('d-none');
  const e = document.getElementById('error');
  if (e) { e.textContent = msg; e.classList.remove('d-none'); }
}
// Delegates to Shell.esc rather than redefining it. This exact function was copy-pasted into a
// dozen page scripts, which means an escaping fix has to be found in a dozen places -- not what you
// want of the control that stops feed- and operator-supplied text becoming script. admin-shell.js
// is loaded before every page script, so Shell is always defined here.
function esc(s) { return Shell.esc(s); }

loadVehicles();
startTimer();
window.addEventListener('pagehide', () => { if (timer) clearInterval(timer); });
