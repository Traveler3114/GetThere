const BASE = '';
// The page paginates client-side, so it needs the whole list in one request. Asking without
// perPage took the server's default of 50 — fine at today's 42 countries, silently dropping the
// rest (and reporting a wrong total) the moment a 51st is added. 500 is the endpoint's ceiling.
const COUNTRY_FETCH_LIMIT = 500;
let allCountries = [];

async function loadCountries() {
  document.getElementById('loading').classList.remove('d-none');
  document.getElementById('error').classList.add('d-none');
  document.getElementById('content').classList.add('d-none');
  try {
    const r = await fetch(BASE + '/countries?perPage=' + COUNTRY_FETCH_LIMIT);
    if (!r.ok) { showError('Failed to load: ' + (r.statusText || 'Unknown error')); return; }
    const j = await r.json();
    allCountries = j.data || [];
    paginatedItems = allCountries;
    showPage(1);
    document.getElementById('loading').classList.add('d-none');
    document.getElementById('content').classList.remove('d-none');
  } catch(e) {
    document.getElementById('loading').classList.add('d-none');
    showError('Failed to load countries: ' + e.message);
  }
}

let currentPage = 1;
const pageSize = 50;
let paginatedItems = [];

function showPage(page) {
  currentPage = page;
  const start = (currentPage - 1) * pageSize;
  const pageData = paginatedItems.slice(start, start + pageSize);
  const totalPages = Math.ceil(paginatedItems.length / pageSize) || 1;
  renderCountries(pageData);
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

function renderCountries(list) {
  if (!list.length) {
    document.getElementById('content').innerHTML = '<div class="alert alert-info">No countries configured.</div>';
    return;
  }
  const rows = list.map(c => `<tr>
    <td>${c.id}</td>
    <td>${esc(c.name)}</td>
    <td><code>${esc(c.isoCode)}</code></td>
    <td>${esc(c.continent||'-')}</td>
  </tr>`).join('');
  document.getElementById('content').innerHTML = `<div class="table-responsive"><table class="table table-striped table-hover"><thead class="table-dark"><tr><th>ID</th><th>Name</th><th>ISO Code</th><th>Continent</th></tr></thead><tbody>${rows}</tbody></table></div><small class="text-muted">${paginatedItems.length} country(ies)</small>`;
}

function showAddModal() {
  const html = `<div class="modal fade" id="addModal" tabindex="-1"><div class="modal-dialog"><div class="modal-content">
    <div class="modal-header"><h5 class="modal-title">Add Country</h5><button type="button" class="btn-close" data-bs-dismiss="modal"></button></div>
    <div class="modal-body">
      <div class="mb-3">
        <label class="form-label">Name</label>
        <input type="text" id="addName" class="form-control form-control-sm" placeholder="e.g. Croatia">
      </div>
      <div class="mb-3">
        <label class="form-label">ISO Code</label>
        <input type="text" id="addIsoCode" class="form-control form-control-sm" placeholder="e.g. HR" maxlength="2">
      </div>
      <div class="mb-3">
        <label class="form-label">Continent</label>
        <input type="text" id="addContinent" class="form-control form-control-sm" placeholder="e.g. Europe">
      </div>
    </div>
    <div class="modal-footer">
      <button type="button" class="btn btn-secondary" data-bs-dismiss="modal">Cancel</button>
      <button type="button" class="btn btn-primary" onclick="addCountry()">Add Country</button>
    </div>
  </div></div></div>`;
  const existing = document.getElementById('addModal');
  if (existing) existing.remove();
  document.body.insertAdjacentHTML('beforeend', html);
  const modal = new bootstrap.Modal(document.getElementById('addModal'));
  modal.show();
  document.getElementById('addModal').addEventListener('hidden.bs.modal', () => document.getElementById('addModal').remove());
}

async function addCountry() {
  const btn = document.querySelector('#addModal .btn-primary');
  btn.disabled = true;
  const name = document.getElementById('addName').value.trim();
  const isoCode = document.getElementById('addIsoCode').value.trim().toUpperCase();
  const continent = document.getElementById('addContinent').value.trim();
  if (!name || !isoCode) { alert('Name and ISO code are required.'); btn.disabled = false; return; }
  try {
    const r = await fetch(BASE + '/countries', {
      method: 'POST',
      headers: {'Content-Type':'application/json'},
      body: JSON.stringify({name, isoCode, continent})
    });
    if (r.ok) {
      bootstrap.Modal.getInstance(document.getElementById('addModal')).hide();
      loadCountries();
    } else {
      const j = await r.json().catch(() => ({}));
      alert(j.title || j.message || 'Failed to add country.');
    }
  } catch(e) { alert('Error: ' + e.message); }
  btn.disabled = false;
}

function showError(msg) {
  // Hides the spinner too. Every loader bails out of its if (!r.ok) ... return path before
  // reaching its own hide, so a failed request used to leave the page showing a spinner and an
  // error message at the same time.
  document.getElementById('loading')?.classList.add('d-none');
  const e = document.getElementById('error'); e.textContent = msg; e.classList.remove('d-none');
}
function esc(s) { if (s === null || s === undefined) return ''; return String(s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]); }

loadCountries();
