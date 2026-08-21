const BASE = '';
let allAlerts = [];
let filterTimer = null;

async function loadAlerts(stopOnestopId, routeOnestopId) {
  document.getElementById('loading').classList.remove('d-none');
  document.getElementById('error').classList.add('d-none');
  document.getElementById('content').classList.add('d-none');
  try {
    const params = new URLSearchParams();
    if (stopOnestopId) params.set('stopOnestopId', stopOnestopId);
    if (routeOnestopId) params.set('routeOnestopId', routeOnestopId);
    const url = BASE + '/realtime/alerts' + (params.toString() ? '?' + params.toString() : '');
    const r = await fetch(url);
    // Without this a failed request parsed the problem+json body into an object, whose .length is
    // undefined, and the page reported "No active alerts" — a server error shown as good news.
    if (!r.ok) { showError('Failed to load alerts: ' + (r.statusText || 'Unknown error')); return; }
    const body = await r.json();
    allAlerts = Array.isArray(body) ? body : [];
    renderAlerts();
    document.getElementById('loading').classList.add('d-none');
    document.getElementById('content').classList.remove('d-none');
  } catch(e) {
    document.getElementById('loading').classList.add('d-none');
    showError('Failed to load alerts: ' + e.message);
  }
}

function scheduleFilter() {
  clearTimeout(filterTimer);
  filterTimer = setTimeout(() => {
    renderAlerts();
  }, 100);
}

function getFilteredAlerts() {
  const kind = (document.getElementById('filterKind')?.value || '').trim();
  const stop = (document.getElementById('filterStop')?.value || '').trim().toLowerCase();
  const route = (document.getElementById('filterRoute')?.value || '').trim().toLowerCase();
  return allAlerts.filter(a => {
    if (kind && (a.kind || '') !== kind) return false;
    if (stop && !((a.affectedStopIds || '').toLowerCase().includes(stop) || (a.headerText || '').toLowerCase().includes(stop))) return false;
    if (route && !((a.affectedRouteIds || '').toLowerCase().includes(route) || (a.matchedRouteIds || '').toLowerCase().includes(route) || (a.headerText || '').toLowerCase().includes(route))) return false;
    return true;
  });
}

function renderAlerts() {
  const filtered = getFilteredAlerts();
  if (!filtered.length) {
    const msg = allAlerts.length ? 'No alerts match the filters.' : 'No active alerts.';
    document.getElementById('content').innerHTML = `<div class="alert alert-info">${msg}</div>`;
    return;
  }
  const cards = filtered.map(a => {
    const periodStart = a.activePeriodStart ? new Date(a.activePeriodStart).toLocaleString() : '-';
    const periodEnd = a.activePeriodEnd ? new Date(a.activePeriodEnd).toLocaleString() : '-';
    const fetched = new Date(a.fetchedAt).toLocaleString();
    return `<div class="card mb-2 shadow-sm">
      <div class="card-body py-2">
        <div class="d-flex justify-content-between align-items-start">
          <h6 class="card-title mb-1">${esc(a.headerText || 'Untitled Alert')}</h6>
          <div>
            ${kindBadge(a.kind)}
            ${causeBadge(a.cause)}
            ${effectBadge(a.effect)}
            ${severityBadge(a.severity)}
          </div>
        </div>
        ${a.descriptionText ? `<p class="card-text small mb-1 text-muted">${esc(a.descriptionText)}</p>` : ''}
        <div class="small text-muted d-flex gap-3 flex-wrap">
          <span><strong>Period:</strong> ${periodStart} &ndash; ${periodEnd}</span>
          ${a.url ? `<span><strong>URL:</strong> <a href="${esc(safeUrl(a.url))}" target="_blank" rel="noopener noreferrer">${esc(a.url)}</a></span>` : ''}
          <span><strong>Fetched:</strong> ${fetched}</span>
          ${a.sourceKey ? `<span><strong>Source:</strong> ${esc(a.sourceKey)}</span>` : ''}
          ${a.matchedRouteIds ? `<span><strong>Matched routes:</strong> ${esc(a.matchedRouteIds)}</span>` : ''}
          ${a.kind === 'Road' && a.geometryGeoJson ? `<span><strong>Geom:</strong> ${esc(a.geometryGeoJson.substring(0,80))}</span>` : ''}
        </div>
      </div>
    </div>`;
  }).join('');
  document.getElementById('content').innerHTML = `<div>${cards}</div><small class="text-muted">${filtered.length} alert(s) (of ${allAlerts.length} total)</small>`;
}

function causeBadge(cause) {
  if (!cause) return '';
  const map = {
    UnknownCause: 'bg-secondary', OtherCause: 'bg-secondary',
    TechnicalProblem: 'bg-danger', Maintenance: 'bg-warning text-dark',
    Construction: 'bg-info text-dark', Accident: 'bg-danger',
    Weather: 'bg-primary', Demolition: 'bg-danger',
    Holiday: 'bg-success'
  };
  return `<span class="badge ${map[cause] || 'bg-secondary'} me-1">${cause}</span>`;
}

function effectBadge(effect) {
  if (!effect) return '';
  const map = {
    UnknownEffect: 'bg-secondary', OtherEffect: 'bg-secondary', OTHER_EFFECT: 'bg-secondary',
    StopMoved: 'bg-warning text-dark', NoService: 'bg-danger', NO_SERVICE: 'bg-danger',
    ReducedService: 'bg-warning text-dark', SignificantDelays: 'bg-danger',
    Detour: 'bg-info text-dark', DETOUR: 'bg-info text-dark', AccessIssue: 'bg-warning text-dark',
    AdditionalService: 'bg-success', ModifiedService: 'bg-info text-dark',
    RouteDeviation: 'bg-warning text-dark'
  };
  return `<span class="badge ${map[effect] || 'bg-secondary'}">${effect}</span>`;
}
function kindBadge(kind) {
  if (!kind) return '';
  const cls = kind === 'Road' ? 'bg-dark' : kind === 'Transit' ? 'bg-primary' : 'bg-secondary';
  return `<span class="badge ${cls} me-1">${esc(kind)}</span>`;
}
function severityBadge(sev) {
  if (!sev) return '';
  const map = { Info: 'bg-info text-dark', Warning: 'bg-warning text-dark', Severe: 'bg-danger' };
  return `<span class="badge ${map[sev] || 'bg-secondary'} ms-1">${esc(sev)}</span>`;
}

function showError(msg) {
  // Hides the spinner too. Every loader bails out of its if (!r.ok) ... return path before
  // reaching its own hide, so a failed request used to leave the page showing a spinner and an
  // error message at the same time.
  document.getElementById('loading')?.classList.add('d-none');
  const e = document.getElementById('error'); e.textContent = msg; e.classList.remove('d-none');
}
// Delegates to Shell.esc rather than redefining it. This exact function was copy-pasted into a
// dozen page scripts, which means an escaping fix has to be found in a dozen places -- not what you
// want of the control that stops feed- and operator-supplied text becoming script. admin-shell.js
// is loaded before every page script, so Shell is always defined here.
function esc(s) { return Shell.esc(s); }
function safeUrl(u) { return Shell.safeUrl(u); }

loadAlerts();
