const BASE = '';
let currentPage = 1;
let totalItems = 0;
const perPage = 50;
let filterTimer = null;

async function loadRoutes() {
  document.getElementById('search').value = '';
  document.getElementById('filterType').value = '';
  currentPage = 1;
  await loadPage(1);
}

function scheduleFilter() {
  clearTimeout(filterTimer);
  filterTimer = setTimeout(() => filterRoutes(), 250);
}

async function loadPage(page) {
  currentPage = page;
  document.getElementById('loading').classList.remove('d-none');
  document.getElementById('error').classList.add('d-none');
  document.getElementById('content').classList.add('d-none');
  try {
    const q = document.getElementById('search').value.trim();
    const type = document.getElementById('filterType').value;
    const params = new URLSearchParams({ page: page, perPage: perPage });
    if (q) params.set('q', q);
    if (type) params.set('routeType', type);
    const r = await fetch(BASE + '/routes?' + params.toString());
    if (!r.ok) { showError('Failed to load: ' + (r.statusText || 'Unknown error')); return; }
    const j = await r.json();
    totalItems = j.total || 0;
    renderRoutes(j.data || []);
    renderPagination();
    document.getElementById('loading').classList.add('d-none');
    document.getElementById('content').classList.remove('d-none');
  } catch(e) {
    document.getElementById('loading').classList.add('d-none');
    showError('Failed to load routes: ' + e.message);
  }
}

function filterRoutes() {
  loadPage(1);
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
  </ul><small class="text-muted d-block text-center">Page ${currentPage} of ${totalPages} (${totalItems} items)</small></nav>`;
}

function renderRoutes(list) {
  if (!list.length) {
    document.getElementById('content').innerHTML = '<div class="alert alert-info">No routes found.</div>';
    return;
  }
  const rows = list.map(r => `<tr>
    <td>${esc(r.name)}</td>
    <td>${esc(r.shortName||'-')}</td>
    <td>${rtBadge(r.routeType)}</td>
    <td>${esc(r.operatorName||'-')}</td>
    <td><code>${esc(r.onestopId)}</code></td>
  </tr>`).join('');
  document.getElementById('content').innerHTML = `<div class="table-responsive"><table class="table table-striped table-hover"><thead class="table-dark"><tr><th>Name</th><th>Short Name</th><th>Type</th><th>Operator</th><th>Global ID</th></tr></thead><tbody>${rows}</tbody></table></div><small class="text-muted">${list.length} route(s) shown</small>`;
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

loadRoutes();
