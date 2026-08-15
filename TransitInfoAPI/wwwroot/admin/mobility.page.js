const BASE = '';
let currentPage = 1;
let perPage = 50;
let totalItems = 0;
let pageData = [];

async function loadFilters() {
  try {
    const r = await fetch(BASE + '/mobility/countries');
    if (r.ok) {
      const countries = await r.json();
      const sel = document.getElementById('filterCountry');
      sel.innerHTML = '<option value="">All countries</option>' + countries.map(c => `<option value="${esc(c)}">${esc(c)}</option>`).join('');
    }
  } catch(e) { /* ignore */ }
}

async function loadPage(page) {
  currentPage = page;
  document.getElementById('loading').classList.remove('d-none');
  document.getElementById('error').classList.add('d-none');
  document.getElementById('content').classList.add('d-none');
  document.getElementById('pagination').classList.add('d-none');
  try {
    let url = `${BASE}/mobility/stations?page=${page}&perPage=${perPage}`;
    const country = document.getElementById('filterCountry').value;
    if (country) url += '&countryName=' + encodeURIComponent(country);
    const r = await fetch(url);
    if (!r.ok) { showError('Failed to load: ' + (r.statusText || 'Unknown error')); return; }
    const j = await r.json();
    pageData = j.data || [];
    totalItems = j.total || 0;
    renderMobilityPage();
    renderPagination();
    document.getElementById('loading').classList.add('d-none');
    document.getElementById('content').classList.remove('d-none');
    document.getElementById('statusBar').classList.remove('d-none');
    document.getElementById('statusBar').textContent = `Page ${currentPage} of ${Math.ceil(totalItems / perPage)} (${totalItems} stations)`;
  } catch(e) {
    document.getElementById('loading').classList.add('d-none');
    showError('Failed to load mobility stations: ' + e.message);
  }
}

function renderPagination() {
  const el = document.getElementById('pagination');
  const totalPages = Math.ceil(totalItems / perPage) || 1;
  if (totalPages <= 1) { el.classList.add('d-none'); return; }
  el.classList.remove('d-none');
  let pages = [];
  for (let i = Math.max(1, currentPage - 2); i <= Math.min(totalPages, currentPage + 2); i++) pages.push(i);
  el.innerHTML = `<nav><ul class="pagination pagination-sm justify-content-center mt-2">
    <li class="page-item ${currentPage <= 1 ? 'disabled' : ''}"><button class="page-link" onclick="loadPage(${currentPage - 1})">Previous</button></li>
    ${currentPage > 3 ? '<li class="page-item disabled"><span class="page-link">...</span></li>' : ''}
    ${pages.map(p => `<li class="page-item ${p === currentPage ? 'active' : ''}"><button class="page-link" onclick="loadPage(${p})">${p}</button></li>`).join('')}
    ${currentPage < totalPages - 2 ? '<li class="page-item disabled"><span class="page-link">...</span></li>' : ''}
    <li class="page-item ${currentPage >= totalPages ? 'disabled' : ''}"><button class="page-link" onclick="loadPage(${currentPage + 1})">Next</button></li>
  </ul><small class="text-muted d-block text-center">Page ${currentPage} of ${totalPages} (${totalItems} stations)</small></nav>`;
}

function renderMobilityPage() {
  const list = pageData;
  if (!list.length) {
    document.getElementById('content').innerHTML = '<div class="alert alert-info">No mobility stations found.</div>';
    return;
  }
  const isTable = document.getElementById('viewTable').checked;
  if (isTable) {
    const rows = list.map(s => {
      const cap = s.capacity || s.capacity === 0 ? s.capacity : null;
      const pct = cap > 0 ? Math.round(s.availableVehicles / cap * 100) : null;
      return `<tr>
      <td>${esc(s.name)}</td>
      <td>${esc(s.providerName)}</td>
      <td>${esc(s.countryName || '')}</td>
      <td class="small">${s.latitude.toFixed(4)}, ${s.longitude.toFixed(4)}</td>
      <td>${cap > 0 ? s.availableVehicles + ' / ' + cap : s.availableVehicles + ' avail.'}</td>
      <td>${pct !== null ? `<div class="capacity-bar"><div class="capacity-bar-fill" style="width:${pct}%"></div></div><small class="text-muted">${pct}%</small>` : '<span class="text-muted">N/A</span>'}</td>
      <td><code>${esc(s.stationId)}</code></td>
    </tr>`;
    }).join('');
    document.getElementById('content').innerHTML = `<div class="table-responsive"><table class="table table-striped table-hover"><thead class="table-dark"><tr><th>Name</th><th>Provider</th><th>Country</th><th>Location</th><th>Available</th><th>Utilization</th><th>Station ID</th></tr></thead><tbody>${rows}</tbody></table></div>`;
  } else {
    const cards = list.map(s => {
      const cap = s.capacity || s.capacity === 0 ? s.capacity : null;
      const pct = cap > 0 ? Math.round(s.availableVehicles / cap * 100) : null;
      const loc = s.countryName ? esc(s.countryName) : '';
      return `<div class="col-md-4 col-lg-3 mb-3"><div class="card h-100 shadow-sm">
        <div class="card-body">
          <h6 class="card-title mb-1">${esc(s.name)}</h6>
          <p class="card-text"><small class="text-muted">${esc(s.providerName)}</small></p>
          ${loc ? `<p class="card-text"><small class="text-muted">${loc}</small></p>` : ''}
          ${cap > 0 ? `<div class="d-flex justify-content-between mb-1"><span class="small">${s.availableVehicles} / ${cap} available</span><span class="small fw-bold">${pct}%</span></div><div class="capacity-bar"><div class="capacity-bar-fill" style="width:${pct}%"></div></div>` : `<div class="mb-1"><span class="small">${s.availableVehicles} available</span> <span class="text-muted">(no capacity data)</span></div>`}
          <p class="card-text mt-2"><small class="text-muted">${s.latitude.toFixed(4)}, ${s.longitude.toFixed(4)}</small></p>
        </div>
      </div></div>`;
    }).join('');
    document.getElementById('content').innerHTML = `<div class="row">${cards}</div>`;
  }
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

loadFilters();
loadPage(1);
