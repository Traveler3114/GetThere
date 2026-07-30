const BASE = '';
let allAgencies = [];

async function loadAgencies() {
  document.getElementById('loading').classList.remove('d-none');
  document.getElementById('error').classList.add('d-none');
  document.getElementById('content').classList.add('d-none');
  try {
    const r = await fetch(BASE + '/agencies?perPage=200');
    if (!r.ok) { showError('Failed to load: ' + (r.statusText || 'Unknown error')); return; }
    const j = await r.json();
    allAgencies = j.data || [];
    filterAgencies();
    paginatedItems = allAgencies;
    showPage(1);
    document.getElementById('loading').classList.add('d-none');
    document.getElementById('content').classList.remove('d-none');
  } catch(e) {
    document.getElementById('loading').classList.add('d-none');
    showError('Failed to load agencies: ' + e.message);
  }
}

function filterAgencies() {
  const q = document.getElementById('search').value.toLowerCase();
  const feedQ = document.getElementById('feedSearch').value.toLowerCase();
  let filtered = allAgencies;
  if (q) filtered = filtered.filter(a => a.name.toLowerCase().includes(q));
  if (feedQ) filtered = filtered.filter(a => (a.agencyId || '').toLowerCase().includes(feedQ));
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
  renderAgencies(pageData);
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
}

function renderAgencies(list) {
  if (!list.length) {
    document.getElementById('content').innerHTML = '<div class="alert alert-info">No agencies found.</div>';
    return;
  }
  const rows = list.map(a => `<tr>
    <td>${esc(a.name)}</td>
    <td><code>${esc(a.agencyId)}</code></td>
    <td>${a.operatorName ? '<span class="badge bg-info text-dark">' + esc(a.operatorName) + '</span>' : '<span class="text-muted">Unmatched</span>'}</td>
    <td>${esc(a.timezone) || '-'}</td>
    <td>${esc(a.phone) || '-'}</td>
    <td class="small text-muted">v${a.feedVersionId}</td>
  </tr>`).join('');
  document.getElementById('content').innerHTML = `<div class="table-responsive"><table class="table table-striped table-hover"><thead class="table-dark"><tr><th>Name</th><th>Agency ID</th><th>Operator</th><th>Timezone</th><th>Phone</th><th>Version</th></tr></thead><tbody>${rows}</tbody></table></div><small class="text-muted">${list.length} agency(ies)</small>`;
}

function showError(msg) { const e = document.getElementById('error'); e.textContent = msg; e.classList.remove('d-none'); }
function esc(s) { if (s === null || s === undefined) return ''; return String(s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]); }

loadAgencies();
