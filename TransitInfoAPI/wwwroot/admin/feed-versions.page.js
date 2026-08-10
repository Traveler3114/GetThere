const BASE = '';
const urlParams = new URLSearchParams(window.location.search);
const feedIdParam = urlParams.get('feedId');
let allVersions = [];

function statusBadge(status) {
  const map = { Success: 'badge-success', Importing: 'badge-importing', Failed: 'badge-failed', Pending: 'badge-pending', Skipped: 'badge-skipped', ReconciliationPending: 'badge-failed' };
  // Spelled out: the raw enum name reads like a routine queue state, when it actually means
  // the feed imported but resolves to no stations until reconciliation is re-run.
  const label = { ReconciliationPending: 'NEEDS RECONCILE' };
  if (label[status]) return `<span class="badge ${map[status]}" title="Imported, but its stops are not linked to stations. Re-run reconciliation to repair.">${label[status]}</span>`;
  return `<span class="badge ${map[status] || 'bg-secondary'}">${status}</span>`;
}

async function loadVersions() {
  document.getElementById('loading').classList.remove('d-none');
  document.getElementById('error').classList.add('d-none');
  document.getElementById('content').classList.add('d-none');
  try {
    const params = new URLSearchParams({ perPage: 200 });
    if (feedIdParam) params.set('feedId', feedIdParam);
    const r = await fetch(BASE + '/feed-versions?' + params.toString());
    if (!r.ok) { showError('Failed to load: ' + (r.statusText || 'Unknown error')); return; }
    const j = await r.json();
    allVersions = j.data || [];
    // filterVersions() already applies the feed/status filters and renders; re-assigning
    // paginatedItems afterwards discarded that, so a reload silently showed every version while
    // the filter controls still displayed a selection.
    filterVersions();
    document.getElementById('loading').classList.add('d-none');
    document.getElementById('content').classList.remove('d-none');
  } catch(e) {
    document.getElementById('loading').classList.add('d-none');
    showError('Failed to load versions: ' + e.message);
  }
}

function filterVersions() {
  const feedQ = document.getElementById('feedFilter').value.toLowerCase();
  const status = document.getElementById('statusFilter').value;
  let filtered = allVersions;
  if (feedQ) filtered = filtered.filter(v => v.feedId.toString().includes(feedQ));
  if (status) filtered = filtered.filter(v => v.importStatus === status);
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
  renderVersions(pageData);
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

function renderVersions(list) {
  if (!list.length) {
    document.getElementById('content').innerHTML = '<div class="alert alert-info">No feed versions found.</div>';
    return;
  }
  const rows = list.map(v => `<tr class="${v.isActive ? 'table-success' : ''}">
    <td><strong>#${v.feedId}</strong></td>
    <td><code class="small" title="${esc(v.sha1)}">${esc(v.sha1.substring(0, 12))}...</code></td>
    <td>${statusBadge(v.importStatus)}</td>
    <td>${v.isActive ? '<span class="badge bg-success">Active</span>' : ''}</td>
    <td class="small">${v.fetchedAt ? new Date(v.fetchedAt).toLocaleString() : '-'}</td>
    <td class="small">${v.importedAt ? new Date(v.importedAt).toLocaleString() : '-'}</td>
    <td class="small text-muted">${v.serviceLevelStart || '-'} &ndash; ${v.serviceLevelEnd || '-'}</td>
    <td class="small text-muted">${v.stopCount || 0} / ${v.routeCount || 0} / ${v.tripCount || 0}</td>
    <td>${v.importError ? '<span class="text-danger small" title="'+esc(v.importError)+'">Error</span>' : '-'}</td>
    <td>${v.importStatus === 'ReconciliationPending'
      ? `<button class="btn btn-sm btn-warning" data-reconcile="${v.id}">Reconcile</button>`
      : ''}</td>
  </tr>`).join('');
  document.getElementById('content').innerHTML = `<div class="table-responsive"><table class="table table-striped table-hover"><thead class="table-dark"><tr><th>Feed</th><th>SHA1</th><th>Status</th><th>Active</th><th>Fetched</th><th>Imported</th><th>Service Period</th><th>S/R/T</th><th>Error</th><th>Repair</th></tr></thead><tbody>${rows}</tbody></table></div><small class="text-muted">${list.length} version(s)</small>`;

  // Bound after render rather than as an inline onclick — these buttons are created from data, and
  // an inline handler would be the one thing standing between this console and dropping
  // 'unsafe-inline' from its script-src.
  document.querySelectorAll('[data-reconcile]').forEach(function (btn) {
    btn.addEventListener('click', function () { reconcileVersion(btn.getAttribute('data-reconcile'), btn); });
  });
}

/**
 * Re-runs reconciliation for a version that imported but never linked its stops to stations.
 * Idempotent, so a mis-click on an already-healthy version is harmless.
 */
async function reconcileVersion(versionId, btn) {
  btn.disabled = true;
  const original = btn.textContent;
  btn.textContent = 'Reconciling...';

  try {
    // No Authorization header here on purpose: admin-auth.js wraps window.fetch and attaches the
    // bearer token to every request from this console. This page does not load admin-shell.js, so
    // Shell.headers() is not available to it.
    const r = await fetch(BASE + '/feeds/versions/' + encodeURIComponent(versionId) + '/reconcile', {
      method: 'POST'
    });

    if (!r.ok) {
      const body = await r.json().catch(() => ({}));
      throw new Error(body.message || ('Reconcile failed (' + r.status + ')'));
    }

    await loadVersions();
  } catch (e) {
    showError(e.message);
    btn.disabled = false;
    btn.textContent = original;
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

loadVersions();
