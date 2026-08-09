const BASE = '';
const SOURCE_ID = new URLSearchParams(window.location.search).get('id');

function esc(v) { return Shell.esc(v == null ? '' : String(v)); }

function fail(msg) {
  const el = document.getElementById('error');
  el.textContent = msg;
  el.classList.remove('d-none');
  document.getElementById('loading').classList.add('d-none');
}

function tone(status) {
  return status === 'Success' ? 'bg-success' : status === 'Failed' ? 'bg-danger' : 'bg-secondary';
}

function duration(run) {
  if (!run.completedAt) return '-';
  const ms = new Date(run.completedAt) - new Date(run.startedAt);
  return ms < 1000 ? ms + ' ms' : (ms / 1000).toFixed(1) + ' s';
}

async function loadRuns() {
  if (!SOURCE_ID) { fail('No source id in the URL.'); return; }
  document.getElementById('loading').classList.remove('d-none');
  document.getElementById('error').classList.add('d-none');
  document.getElementById('content').classList.add('d-none');

  try {
    const meta = await fetch(BASE + '/custom-sources/' + SOURCE_ID);
    if (meta.ok) {
      const m = await meta.json();
      document.getElementById('sourceMeta').innerHTML =
        '<strong>' + esc(m.name) + '</strong> &middot; ' + esc(m.operatorName) + ' &middot; source #' + m.id;
    }

    const r = await fetch(BASE + '/custom-sources/' + SOURCE_ID + '/runs?perPage=100');
    if (!r.ok) { fail('Failed to load runs: ' + (r.statusText || r.status)); return; }
    const j = await r.json();
    render(j.data || j || []);
    document.getElementById('loading').classList.add('d-none');
    document.getElementById('content').classList.remove('d-none');
  } catch (e) {
    fail('Failed to load: ' + e.message);
  }
}

function render(runs) {
  if (!runs.length) {
    document.getElementById('content').innerHTML =
      '<div class="alert alert-info">This source has not run yet.</div>';
    return;
  }

  const rows = runs.map(function (run) {
    return '<tr>' +
      '<td class="small text-muted">' + run.id + '</td>' +
      '<td><span class="badge ' + tone(run.status) + '">' + esc(run.status) + '</span></td>' +
      '<td class="small">' + esc(Shell.dateTime(run.startedAt)) + '<div class="text-muted">' + esc(Shell.ago(run.startedAt)) + '</div></td>' +
      '<td class="small">' + duration(run) + '</td>' +
      '<td class="small">' + Shell.num(run.recordsProduced) + '</td>' +
      '<td class="small">' + (run.feedVersionId
        ? '<a href="/admin/feed-versions.html">version ' + run.feedVersionId + '</a>'
        : '<span class="text-muted">none</span>') + '</td>' +
      '<td class="small" style="max-width:520px"><code class="small">' + esc(run.logText || '') + '</code></td>' +
      '</tr>';
  }).join('');

  document.getElementById('content').innerHTML =
    '<div class="table-responsive"><table class="table table-striped table-hover">' +
    '<thead class="table-dark"><tr><th>#</th><th>Status</th><th>Started</th><th>Took</th><th>Records</th><th>Version</th><th>Log</th></tr></thead>' +
    '<tbody>' + rows + '</tbody></table></div>';
}

loadRuns();
