let allSources = [];

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
    const r = await fetch('/alert-sources?perPage=200', { headers: Shell.headers() });
    if (!r.ok) { showError('Failed to load: ' + (r.statusText || r.status)); return; }
    const j = await r.json();
    allSources = j.data || j.items || [];
    // API returns Paginated with data/total — keep compatible
    if (j.data) allSources = j.data;
    else if (j.items) allSources = j.items;
    render();
    document.getElementById('loading').classList.add('d-none');
    document.getElementById('content').classList.remove('d-none');
  } catch (e) {
    showError('Failed to load: ' + e.message);
  }
}

function render() {
  if (allSources.length === 0) {
    document.getElementById('content').innerHTML =
      '<div class="alert alert-info">No alert sources yet.</div>';
    return;
  }

  const rows = allSources.map(function (s) {
    const warningClass = s.lastItemCount === 0 ? ' class="table-warning"' : '';
    const countCell = s.lastItemCount === 0
      ? '<span class="text-danger fw-bold">' + esc(s.lastItemCount) + '</span>'
      : esc(s.lastItemCount != null ? s.lastItemCount : '-');
    const errorRow = s.lastError ? '<div class="small text-danger">' + esc(s.lastError) + '</div>' : '';
    return '<tr' + warningClass + '>' +
      '<td>' + esc(s.sourceKey) + errorRow + '</td>' +
      '<td class="small">' + esc(s.operatorName) + '</td>' +
      '<td><span class="badge bg-light text-dark border">' + esc(s.kind) + '</span></td>' +
      '<td><span class="badge bg-light text-dark border">' + esc(s.format) + '</span></td>' +
      '<td class="small">' + esc(s.intervalMinutes) + ' min</td>' +
      '<td class="small">' + (s.lastRunAt ? esc(Shell.ago(s.lastRunAt)) : '-') + '</td>' +
      '<td class="small">' + countCell + '</td>' +
      '<td class="text-nowrap">' +
        '<button class="btn btn-sm btn-outline-info me-1" onclick="previewSource(' + s.id + ')" title="Preview"><i class="bi bi-eye"></i> Preview</button> ' +
        '<button class="btn btn-sm btn-outline-primary me-1" onclick="showEditModal(' + s.id + ')" title="Edit"><i class="bi bi-pencil"></i></button> ' +
        '<button class="btn btn-sm btn-outline-danger" onclick="deleteSource(' + s.id + ')" title="Delete"><i class="bi bi-trash"></i></button>' +
      '</td></tr>';
  }).join('');

  document.getElementById('content').innerHTML =
    '<div class="table-responsive"><table class="table table-striped table-hover">' +
    '<thead class="table-dark"><tr><th>SourceKey</th><th>Operator</th><th>Kind</th><th>Format</th><th>Interval</th><th>Last run</th><th>Items</th><th>Actions</th></tr></thead>' +
    '<tbody>' + rows + '</tbody></table></div>';
}

async function previewSource(id) {
  try {
    const r = await fetch('/alert-sources/' + id + '/preview', { method: 'POST', headers: Shell.headers() });
    const j = await r.json().catch(function () { return null; });
    if (!r.ok) {
      alert('Preview failed: ' + ((j && (j.detail || j.title)) || r.statusText));
      return;
    }
    const warningsEl = document.getElementById('previewWarnings');
    if (j.warnings && j.warnings.length > 0) {
      warningsEl.textContent = j.warnings.join('; ');
      warningsEl.classList.remove('d-none');
    } else {
      warningsEl.classList.add('d-none');
    }
    const content = document.getElementById('previewContent');
    if (!j.items || j.items.length === 0) {
      content.innerHTML = '<div class="alert alert-info">No items extracted (count: ' + esc(j.itemCount) + ').</div>';
    } else {
      const itemsHtml = j.items.map(function (it) {
        return '<div class="border rounded p-2 mb-2">' +
          '<div><strong>' + esc(it.title) + '</strong></div>' +
          (it.description ? '<div class="small text-muted">' + esc(it.description) + '</div>' : '') +
          (it.link ? '<div class="small"><a href="' + Shell.safeUrl(it.link) + '" target="_blank">' + esc(it.link) + '</a></div>' : '') +
          (it.date ? '<div class="small text-muted">' + esc(it.date) + '</div>' : '') +
          (it.category ? '<div><span class="badge bg-secondary">' + esc(it.category) + '</span></div>' : '') +
        '</div>';
      }).join('');
      content.innerHTML = '<div class="mb-2 small text-muted">Showing ' + j.items.length + ' of ' + j.itemCount + ' items</div>' + itemsHtml;
    }
    new bootstrap.Modal(document.getElementById('previewModal')).show();
  } catch (e) {
    alert('Preview failed: ' + e.message);
  }
}

async function deleteSource(id) {
  const source = allSources.find(function (s) { return s.id === id; });
  if (!confirm('Delete "' + (source ? source.sourceKey : id) + '"? Its feed goes with it.')) return;
  const r = await fetch('/alert-sources/' + id, { method: 'DELETE', headers: Shell.headers() });
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

  const r = await fetch('/operators?perPage=500', { headers: Shell.headers() });
  const j = await r.json();
  const ops = j.data || j.items || [];
  select.innerHTML = ops.map(function (o) {
    return '<option value="' + o.id + '">' + esc(o.name) + '</option>';
  }).join('');
}

async function createSource() {
  const body = {
    operatorId: parseInt(document.getElementById('addOperator').value, 10),
    sourceKey: document.getElementById('addSourceKey').value.trim(),
    kind: document.getElementById('addKind').value,
    format: document.getElementById('addFormat').value,
    url: document.getElementById('addUrl').value.trim(),
    itemSelector: document.getElementById('addItemSelector').value.trim() || null,
    intervalMinutes: parseInt(document.getElementById('addInterval').value, 10) || 15
  };

  if (!body.sourceKey) {
    const el = document.getElementById('addError');
    el.textContent = 'SourceKey is required.';
    el.classList.remove('d-none');
    return;
  }
  if (!body.url) {
    const el = document.getElementById('addError');
    el.textContent = 'URL is required.';
    el.classList.remove('d-none');
    return;
  }

  const r = await fetch('/alert-sources', {
    method: 'POST',
    headers: Shell.headers(),
    body: JSON.stringify(body)
  });

  if (!r.ok) {
    const j = await r.json().catch(function () { return null; });
    const el = document.getElementById('addError');
    el.textContent = (j && (j.detail || j.title)) || r.statusText;
    el.classList.remove('d-none');
    return;
  }

  bootstrap.Modal.getInstance(document.getElementById('addModal')).hide();
  loadSources();
}

async function showEditModal(id) {
  const s = allSources.find(function (x) { return x.id === id; });
  if (!s) return;
  document.getElementById('editError').classList.add('d-none');
  document.getElementById('editId').value = s.id;
  document.getElementById('editKind').value = s.kind;
  document.getElementById('editFormat').value = s.format;
  document.getElementById('editUrl').value = s.url;
  document.getElementById('editItemSelector').value = s.itemSelector || '';
  document.getElementById('editTitleSelector').value = s.titleSelector || '';
  document.getElementById('editDescriptionSelector').value = s.descriptionSelector || '';
  document.getElementById('editDateSelector').value = s.dateSelector || '';
  document.getElementById('editLinkSelector').value = s.linkSelector || '';
  document.getElementById('editCategorySelector').value = s.categorySelector || '';
  document.getElementById('editInterval').value = s.intervalMinutes;
  document.getElementById('editIsActive').value = s.isActive ? 'true' : 'false';
  new bootstrap.Modal(document.getElementById('editModal')).show();
}

async function saveEdit() {
  const id = parseInt(document.getElementById('editId').value, 10);
  const body = {
    kind: document.getElementById('editKind').value,
    format: document.getElementById('editFormat').value,
    url: document.getElementById('editUrl').value.trim(),
    itemSelector: document.getElementById('editItemSelector').value.trim() || null,
    titleSelector: document.getElementById('editTitleSelector').value.trim() || null,
    descriptionSelector: document.getElementById('editDescriptionSelector').value.trim() || null,
    dateSelector: document.getElementById('editDateSelector').value.trim() || null,
    linkSelector: document.getElementById('editLinkSelector').value.trim() || null,
    categorySelector: document.getElementById('editCategorySelector').value.trim() || null,
    intervalMinutes: parseInt(document.getElementById('editInterval').value, 10) || 15,
    isActive: document.getElementById('editIsActive').value === 'true'
  };

  const r = await fetch('/alert-sources/' + id, {
    method: 'PUT',
    headers: Shell.headers(),
    body: JSON.stringify(body)
  });

  if (!r.ok) {
    const j = await r.json().catch(function () { return null; });
    const el = document.getElementById('editError');
    el.textContent = (j && (j.detail || j.title)) || r.statusText;
    el.classList.remove('d-none');
    return;
  }

  bootstrap.Modal.getInstance(document.getElementById('editModal')).hide();
  loadSources();
}

loadSources();
