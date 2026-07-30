const BASE = '';

// Must not exceed the server's [Range(1, 500)] on StationsController.Search. This page asked for
// 10 000, which failed that validation on every load — so it has never actually shown a station,
// only "Failed to load: Unknown error". Capping it here is what makes the page work at all; the
// multi-megabyte client-side filter it was reaching for was never viable anyway.
const STATION_FETCH_LIMIT = 500;
let allStations = [];
let searchTimer = null;

function scheduleSearch() {
  clearTimeout(searchTimer);
  searchTimer = setTimeout(applyFilters, 250);
}

async function loadCountriesFilter() {
  try {
    const r = await fetch(BASE + '/countries?perPage=100');
    if (!r.ok) return;
    const j = await r.json();
    const names = (j.data || []).map(c => c.name).sort();
    document.getElementById('filterCountry').innerHTML = '<option value="">All Countries</option>' + names.map(n => `<option value="${esc(n)}">${esc(n)}</option>`).join('');
  } catch(_) { /* non-critical */ }
}

async function loadStations() {
  document.getElementById('search').value = '';
  document.getElementById('filterRouteType').value = '';
  document.getElementById('filterType').value = '';
  document.getElementById('filterCountry').value = '';
  document.getElementById('loading').classList.remove('d-none');
  document.getElementById('error').classList.add('d-none');
  document.getElementById('content').classList.add('d-none');
  document.getElementById('statusBar').classList.add('d-none');
  try {
    // 10 000 was the previous request size: with 6 200 stations today that is a multi-megabyte
    // payload on every page load, parsed and filtered entirely in the browser. Capped to something
    // a table can actually show; the server already supports q/routeType/country filters if this
    // page ever needs to search beyond the first page.
    const r = await fetch(BASE + '/stations/search?perPage=' + STATION_FETCH_LIMIT);
    if (!r.ok) { showError('Failed to load: ' + (r.statusText || 'Unknown error')); return; }
    const j = await r.json();
    allStations = j.data || [];
    loadCountriesFilter();
    applyFilters();
    document.getElementById('loading').classList.add('d-none');
    document.getElementById('content').classList.remove('d-none');
    document.getElementById('statusBar').classList.remove('d-none');
    document.getElementById('statusBar').textContent = allStations.length >= STATION_FETCH_LIMIT
      ? `Showing the first ${allStations.length} stations — narrow the filters to see others`
      : `Loaded ${allStations.length} station(s)`;
  } catch(e) {
    document.getElementById('loading').classList.add('d-none');
    showError('Failed to load stations: ' + e.message);
  }
}

function applyFilters() {
  const q = document.getElementById('search').value.toLowerCase();
  const rt = document.getElementById('filterRouteType').value;
  const stationType = document.getElementById('filterType').value;
  const country = document.getElementById('filterCountry').value;
  let filtered = allStations;
  if (q) filtered = filtered.filter(s => s.name.toLowerCase().includes(q));
  if (rt) filtered = filtered.filter(s => s.primaryRouteType === rt);
  if (stationType) filtered = filtered.filter(s => s.stationType === stationType);
  if (country) filtered = filtered.filter(s => s.countryName === country);
  paginatedItems = filtered;
  showPage(1);
}

let currentPage = 1;
const pageSize = 50;
let paginatedItems = [];

function showPage(page) {
  currentPage = page;
  const start = (currentPage - 1) * pageSize;
  const pageData = paginatedItems.slice(start, start + pageSize);
  const totalPages = Math.ceil(paginatedItems.length / pageSize) || 1;
  renderStations(pageData);
  renderPagination(totalPages);
}

function renderPagination(totalPages) {
  const el = document.getElementById('pagination');
  if (totalPages <= 1 && paginatedItems.length <= pageSize) { el.classList.add('d-none'); return; }
  el.classList.remove('d-none');
  let pages = [];
  for (let i = Math.max(1, currentPage - 2); i <= Math.min(totalPages, currentPage + 2); i++) pages.push(i);
  el.innerHTML = `<nav><ul class="pagination pagination-sm justify-content-center mt-2">
    <li class="page-item ${currentPage <= 1 ? 'disabled' : ''}"><button class="page-link" onclick="showPage(${currentPage - 1})">Previous</button></li>
    ${currentPage > 3 ? '<li class="page-item disabled"><span class="page-link">...</span></li>' : ''}
    ${pages.map(p => `<li class="page-item ${p === currentPage ? 'active' : ''}"><button class="page-link" onclick="showPage(${p})">${p}</button></li>`).join('')}
    ${currentPage < totalPages - 2 ? '<li class="page-item disabled"><span class="page-link">...</span></li>' : ''}
    <li class="page-item ${currentPage >= totalPages ? 'disabled' : ''}"><button class="page-link" onclick="showPage(${currentPage + 1})">Next</button></li>
  </ul><small class="text-muted d-block text-center">Page ${currentPage} of ${totalPages} (${paginatedItems.length} items)</small></nav>`;
  document.getElementById('statusBar').textContent = `Filtered: ${paginatedItems.length} station(s)`;
}

function renderStations(list) {
  if (!list.length) {
    document.getElementById('content').innerHTML = '<div class="alert alert-info">No stations found.</div>';
    return;
  }
  const rows = list.map(s => `<tr class="table-clickable" onclick="toggleDetail(this, '${esc(s.onestopId)}')">
    <td>${esc(s.name)}</td>
    <td><code class="small">${esc(s.onestopId)}</code></td>
    <td><span class="badge bg-secondary">${formatEnumName(s.stationType)}</span></td>
    <td>${rtBadge(s.primaryRouteType)}</td>
    <td>${esc(s.countryName||'-')}</td>
    <td>${esc(s.cityName||'-')}</td>
    <td class="small text-nowrap">${typeof s.latitude === 'number' && typeof s.longitude === 'number'
      ? s.latitude.toFixed(4) + ', ' + s.longitude.toFixed(4) : '-'}</td>
  </tr><tr class="detail-row" id="detail-${esc(s.onestopId)}"><td colspan="7"><div class="detail-inner" id="detailInner-${esc(s.onestopId)}"><i class="bi bi-hourglass-split"></i> Loading...</div></td></tr>`).join('');
  document.getElementById('content').innerHTML = `<div class="table-responsive"><table class="table table-striped table-hover" id="stationTable"><thead class="table-dark"><tr><th>Name</th><th>ID</th><th>Type</th><th>Route Type</th><th>Country</th><th>City</th><th>Location</th></tr></thead><tbody>${rows}</tbody></table></div><small class="text-muted">${list.length} station(s) shown</small>`;
}

let detailLoadingInFlight = new Set();
let detailCache = {};

async function toggleDetail(row, onestopId) {
  const detailRow = document.getElementById('detail-' + onestopId);
  if (!detailRow) return;
  const isShown = detailRow.classList.contains('show');
  document.querySelectorAll('.detail-row.show').forEach(el => el.classList.remove('show'));
  if (isShown) return;
  detailRow.classList.add('show');
  if (!detailLoadingInFlight.has(onestopId) && !detailCache[onestopId]) {
    detailLoadingInFlight.add(onestopId);
    try {
      const station = allStations.find(s => s.onestopId === onestopId);
      if (!station) { document.getElementById('detailInner-' + onestopId).innerHTML = '<p class="text-danger small">Station not found.</p>'; return; }
      const [oR, dR] = await Promise.all([
        fetch(BASE + '/stations/' + station.id + '/operators').then(r=>r.json()),
        fetch(BASE + '/stations/' + station.id + '/departures').then(r=>r.json())
      ]);
      const operators = oR || [];
      const departures = dR || [];
      const oHtml = operators.length ? '<h6>Operators</h6><div class="table-responsive"><table class="table table-sm table-bordered"><thead><tr><th>Name</th><th>Type</th></tr></thead><tbody>' + operators.map(op => `<tr><td>${esc(op.name)}</td><td>${esc(op.operatorType)}</td></tr>`).join('') + '</tbody></table></div>' : '<p class="text-muted small">No operators at this station.</p>';
      const dHtml = departures.length ? '<h6>Departures</h6><div class="table-responsive"><table class="table table-sm table-bordered"><thead><tr><th>Route</th><th>Headsign</th><th>Scheduled</th><th>Estimated</th><th>Delay</th></tr></thead><tbody>' + departures.map(dp => {
        const delay = dp.delaySeconds != null ? (dp.delaySeconds > 0 ? '<span class="text-danger">+' + Math.round(dp.delaySeconds/60) + 'm</span>' : '<span class="text-success">On time</span>') : '-';
        return `<tr><td>${esc(dp.routeName)}</td><td>${esc(dp.headsign)}</td><td>${dp.scheduledDeparture ? new Date(dp.scheduledDeparture).toLocaleTimeString() : '-'}</td><td>${dp.estimatedDeparture ? new Date(dp.estimatedDeparture).toLocaleTimeString() : '-'}</td><td>${delay}</td></tr>`;
      }).join('') + '</tbody></table></div>' : '<p class="text-muted small">No upcoming departures.</p>';
      detailCache[onestopId] = oHtml + dHtml;
      document.getElementById('detailInner-' + onestopId).innerHTML = oHtml + dHtml + '<a href="/map/" target="_blank" class="btn btn-sm btn-outline-success mt-2"><i class="bi bi-map"></i> View on Map</a>';
    } catch(e) {
      document.getElementById('detailInner-' + onestopId).innerHTML = '<p class="text-danger small">Error loading details.</p>';
    } finally {
      detailLoadingInFlight.delete(onestopId);
    }
  } else if (detailCache[onestopId]) {
    document.getElementById('detailInner-' + onestopId).innerHTML = detailCache[onestopId] + '<a href="/map/" target="_blank" class="btn btn-sm btn-outline-success mt-2"><i class="bi bi-map"></i> View on Map</a>';
  }
  setTimeout(() => detailRow.scrollIntoView({behavior:'smooth', block:'center'}), 100);
}

function showError(msg) {
  // Hides the spinner too. Every loader bails out of its if (!r.ok) ... return path before
  // reaching its own hide, so a failed request used to leave the page showing a spinner and an
  // error message at the same time.
  document.getElementById('loading')?.classList.add('d-none');
  const e = document.getElementById('error'); e.textContent = msg; e.classList.remove('d-none');
}
function esc(s) { if (s === null || s === undefined) return ''; return String(s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]); }

loadStations();
