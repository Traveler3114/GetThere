const BASE = '';
let allSources = [];

const KIND_ICON = {
  Http: 'bi-plug',
  Html: 'bi-globe',
  Pdf: 'bi-file-earmark-pdf',
  Upload: 'bi-upload'
};

function esc(v) { return Shell.esc(v == null ? '' : String(v)); }

function showError(msg) {
  const el = document.getElementById('error');
  el.textContent = msg;
  el.classList.remove('d-none');
  document.getElementById('loading').classList.add('d-none');
}

async function loadSources() {
  document.getElementById('loading').classList.remove('d-none');
  document.getElementById('error').classList.add('d-none');
  document.getElementById('content').classList.add('d-none');
  try {
    const r = await fetch(BASE + '/custom-sources?perPage=200');
    if (!r.ok) { showError('Failed to load: ' + (r.statusText || r.status)); return; }
    const j = await r.json();
    allSources = j.data || [];
    render();
    document.getElementById('loading').classList.add('d-none');
    document.getElementById('content').classList.remove('d-none');
  } catch (e) {
    showError('Failed to load: ' + e.message);
  }
}

function runBadge(status) {
  if (!status) return '<span class="text-muted small">never run</span>';
  const tone = status === 'Success' ? 'bg-success' : status === 'Failed' ? 'bg-danger' : 'bg-secondary';
  return '<span class="badge ' + tone + '">' + esc(status) + '</span>';
}

function render() {
  if (allSources.length === 0) {
    document.getElementById('content').innerHTML =
      '<div class="alert alert-info">No custom sources yet. Add one to import an operator that does not publish GTFS.</div>';
    return;
  }

  const rows = allSources.map(function (s) {
    const sections = (s.requests || []).map(function (r) {
      return '<span class="badge bg-light text-dark border me-1">' + esc(r.targetSection) + '</span>';
    }).join('') || '<span class="text-muted small">no requests configured</span>';

    return '<tr>' +
      '<td class="small text-muted">' + s.id + '</td>' +
      '<td><i class="bi ' + (KIND_ICON[s.kind] || 'bi-question') + ' me-1"></i>' + esc(s.name) +
        (s.extractorKey ? ' <span class="badge bg-info text-dark" title="Handled by a named C# extractor">' + esc(s.extractorKey) + '</span>' : '') + '</td>' +
      '<td class="small">' + esc(s.operatorName) + '</td>' +
      '<td>' + sections + '</td>' +
      '<td>' + (s.isActive ? '<span class="badge bg-success">Active</span>' : '<span class="badge bg-secondary">Inactive</span>') + '</td>' +
      '<td class="small">' + (s.lastRunAt ? esc(Shell.ago(s.lastRunAt)) : '-') + ' ' + runBadge(s.lastRunStatus) + '</td>' +
      '<td class="text-nowrap">' +
        '<a class="btn btn-sm btn-outline-primary" href="/admin/custom-source-editor.html?id=' + s.id + '" title="Edit"><i class="bi bi-pencil"></i></a> ' +
        '<button class="btn btn-sm btn-outline-success" onclick="runSource(' + s.id + ', this)" title="Run now"><i class="bi bi-play-fill"></i></button> ' +
        '<a class="btn btn-sm btn-outline-secondary" href="/admin/custom-source-runs.html?id=' + s.id + '" title="Run history"><i class="bi bi-clock-history"></i></a> ' +
        '<button class="btn btn-sm btn-outline-danger" onclick="deleteSource(' + s.id + ')" title="Delete"><i class="bi bi-trash"></i></button>' +
      '</td></tr>';
  }).join('');

  document.getElementById('content').innerHTML =
    '<div class="table-responsive"><table class="table table-striped table-hover">' +
    '<thead class="table-dark"><tr><th>#</th><th>Name</th><th>Operator</th><th>Sections</th><th>Status</th><th>Last run</th><th>Actions</th></tr></thead>' +
    '<tbody>' + rows + '</tbody></table></div>';
}

async function runSource(id, btn) {
  const original = btn.innerHTML;
  btn.disabled = true;
  btn.innerHTML = '<span class="spinner-border spinner-border-sm"></span>';
  try {
    const r = await fetch(BASE + '/custom-sources/' + id + '/run', { method: 'POST' });
    const j = await r.json().catch(function () { return null; });
    if (!r.ok) {
      alert('Run failed: ' + ((j && (j.detail || j.title)) || r.statusText));
    } else {
      alert((j.status === 'Success' ? 'Run complete.\n\n' : 'Run finished with errors.\n\n') + (j.logText || ''));
    }
  } catch (e) {
    alert('Run failed: ' + e.message);
  } finally {
    btn.disabled = false;
    btn.innerHTML = original;
    loadSources();
  }
}

async function deleteSource(id) {
  const source = allSources.find(function (s) { return s.id === id; });
  if (!confirm('Delete "' + (source ? source.name : id) + '"? Its requests, mappings and run history go with it.')) return;
  const r = await fetch(BASE + '/custom-sources/' + id, { method: 'DELETE' });
  if (!r.ok) {
    const j = await r.json().catch(function () { return null; });
    alert('Delete failed: ' + ((j && (j.detail || j.title)) || r.statusText));
    return;
  }
  loadSources();
}

async function showAddModal() {
  document.getElementById('addError').classList.add('d-none');
  const select = document.getElementById('addOperator');
  select.innerHTML = '<option>Loading...</option>';
  new bootstrap.Modal(document.getElementById('addModal')).show();

  const r = await fetch(BASE + '/operators?perPage=500');
  const j = await r.json();
  select.innerHTML = (j.data || []).map(function (o) {
    return '<option value="' + o.id + '">' + esc(o.name) + '</option>';
  }).join('');
}

async function createSource() {
  const body = {
    operatorId: parseInt(document.getElementById('addOperator').value, 10),
    name: document.getElementById('addName').value.trim(),
    kind: document.getElementById('addKind').value,
    refreshIntervalSeconds: 3600,
    requests: []
  };
  const start = document.getElementById('addWindowStart').value;
  const end = document.getElementById('addWindowEnd').value;
  if (start) body.serviceWindowStart = start;
  if (end) body.serviceWindowEnd = end;

  if (!body.name) {
    const el = document.getElementById('addError');
    el.textContent = 'Give the source a name.';
    el.classList.remove('d-none');
    return;
  }

  const r = await fetch(BASE + '/custom-sources', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body)
  });

  if (!r.ok) {
    const j = await r.json().catch(function () { return null; });
    const el = document.getElementById('addError');
    el.textContent = (j && (j.detail || j.title)) || r.statusText;
    el.classList.remove('d-none');
    return;
  }

  const created = await r.json();
  // Straight into the editor: a source with no requests cannot do anything yet, and the next step
  // is always to add one.
  window.location.href = '/admin/custom-source-editor.html?id=' + created.id;
}

loadSources();
