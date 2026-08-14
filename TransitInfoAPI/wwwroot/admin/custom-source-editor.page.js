const BASE = '';
const SOURCE_ID = new URLSearchParams(window.location.search).get('id');

const SECTIONS = ['Stops', 'Routes', 'Trips', 'StopTimes', 'Calendar', 'CalendarDates', 'Agencies'];
const FORMATS = ['Json', 'Csv', 'Xml', 'Html', 'Pdf', 'Xlsx'];
const MAPPING_KINDS = ['Direct', 'Static', 'Expression', 'TimeExtract', 'DateExtract'];

// The fields each section can actually accept, so the editor offers real targets instead of a free
// text box that only fails at run time.
const TARGET_FIELDS = {
  Stops: ['StopId', 'StopName', 'StopLat', 'StopLon', 'StopCode', 'StopDesc', 'ZoneId', 'PlatformCode', 'LocationType', 'ParentStation', 'WheelchairBoarding'],
  Routes: ['RouteId', 'RouteShortName', 'RouteLongName', 'RouteType', 'RouteColor', 'RouteTextColor', 'AgencyId'],
  Trips: ['TripId', 'RouteId', 'ServiceId', 'TripHeadsign', 'TripShortName', 'DirectionId', 'ShapeId'],
  StopTimes: ['TripId', 'StopId', 'ArrivalTime', 'DepartureTime', 'StopSequence', 'StopHeadsign', 'PickupType', 'DropOffType'],
  Calendar: ['ServiceId', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday', 'StartDate', 'EndDate'],
  CalendarDates: ['ServiceId', 'Date', 'ExceptionType'],
  Agencies: ['AgencyId', 'AgencyName', 'AgencyUrl', 'AgencyTimezone', 'AgencyLang', 'AgencyPhone']
};

let model = null;

function esc(v) { return Shell.esc(v == null ? '' : String(v)); }
function opt(list, selected) {
  return list.map(function (v) {
    return '<option value="' + esc(v) + '"' + (v === selected ? ' selected' : '') + '>' + esc(v) + '</option>';
  }).join('');
}

function fail(msg) {
  const el = document.getElementById('error');
  el.textContent = msg;
  el.classList.remove('d-none');
  document.getElementById('loading').classList.add('d-none');
}

function status(html, tone) {
  document.getElementById('saveStatus').innerHTML =
    '<div class="alert alert-' + (tone || 'info') + ' py-2 small">' + html + '</div>';
}

async function load() {
  if (!SOURCE_ID) { fail('No source id in the URL.'); return; }
  const r = await fetch(BASE + '/custom-sources/' + SOURCE_ID);
  if (!r.ok) { fail('Failed to load source: ' + (r.statusText || r.status)); return; }
  model = await r.json();

  document.getElementById('sourceName').textContent = model.name;
  document.getElementById('sourceMeta').innerHTML =
    esc(model.operatorName) + ' &middot; ' + esc(model.kind) + ' &middot; source #' + model.id;

  document.getElementById('fName').value = model.name || '';
  document.getElementById('fKind').value = model.kind || 'Http';
  document.getElementById('fRefresh').value = model.refreshIntervalSeconds || 3600;
  document.getElementById('fWindowStart').value = model.serviceWindowStart || '';
  document.getElementById('fWindowEnd').value = model.serviceWindowEnd || '';
  document.getElementById('fExtractor').value = model.extractorKey || '';
  // Deliberately not populated from the model: the server no longer returns the credential, only
  // whether one exists. Blank means "keep what is stored" on save.
  document.getElementById('fAuth').value = '';
  document.getElementById('fAuthState').textContent = model.hasAuth
    ? 'A credential is stored. Leave blank to keep it, or type a new one to replace it.'
    : 'No credential stored.';
  document.getElementById('fActive').checked = !!model.isActive;

  renderRequests();
  renderDiscoverTargets();
  loadUploads();

  document.getElementById('loading').classList.add('d-none');
  document.getElementById('editor').classList.remove('d-none');
}

function renderRequests() {
  if (!model.requests.length) {
    document.getElementById('requests').innerHTML =
      '<div class="alert alert-warning small">No requests yet. Add one per section of the network this source publishes.</div>';
    return;
  }

  document.getElementById('requests').innerHTML = model.requests.map(function (req, i) {
    const fields = TARGET_FIELDS[req.targetSection] || [];
    const mappings = (req.mappings || []).map(function (m, j) {
      return '<tr class="mapping-row">' +
        '<td><input class="form-control form-control-sm font-monospace" value="' + esc(m.sourceExpression) + '" ' +
          'onchange="setMapping(' + i + ',' + j + ',\'sourceExpression\',this.value)" placeholder="field in the row"></td>' +
        '<td><select class="form-select form-select-sm" onchange="setMapping(' + i + ',' + j + ',\'targetField\',this.value)">' +
          opt(fields, m.targetField) + '</select></td>' +
        '<td><select class="form-select form-select-sm" onchange="setMapping(' + i + ',' + j + ',\'kind\',this.value)">' +
          opt(MAPPING_KINDS, m.kind) + '</select></td>' +
        '<td><button class="btn btn-sm btn-outline-danger" onclick="removeMapping(' + i + ',' + j + ')"><i class="bi bi-x"></i></button></td>' +
        '</tr>';
    }).join('');

    return '<div class="card mb-2 request-card">' +
      '<div class="card-body py-2">' +
        '<div class="row g-2 align-items-end mb-2">' +
          '<div class="col-md-3"><label class="form-label small mb-0">Section</label>' +
            '<select class="form-select form-select-sm" onchange="setRequest(' + i + ',\'targetSection\',this.value)">' + opt(SECTIONS, req.targetSection) + '</select></div>' +
          '<div class="col-md-2"><label class="form-label small mb-0">Format</label>' +
            '<select class="form-select form-select-sm" onchange="setRequest(' + i + ',\'format\',this.value)">' + opt(FORMATS, req.format) + '</select></div>' +
          '<div class="col-md-5"><label class="form-label small mb-0">URL</label>' +
            '<input class="form-control form-control-sm font-monospace" value="' + esc(req.url) + '" onchange="setRequest(' + i + ',\'url\',this.value)"></div>' +
          '<div class="col-md-2 text-end">' +
            '<button class="btn btn-sm btn-outline-info" onclick="discover(' + i + ')" title="Fetch a sample"><i class="bi bi-search"></i></button> ' +
            '<button class="btn btn-sm btn-outline-danger" onclick="removeRequest(' + i + ')" title="Remove"><i class="bi bi-trash"></i></button>' +
          '</div>' +
        '</div>' +
        '<div class="row g-2 mb-2">' +
          '<div class="col-md-4"><label class="form-label small mb-0">Data path</label>' +
            '<input class="form-control form-control-sm font-monospace" id="dataPath' + i + '" value="' + esc(req.dataPath) + '" ' +
            'onchange="setRequest(' + i + ',\'dataPath\',this.value)" placeholder="e.g. data.stops"></div>' +
          '<div class="col-md-3"><label class="form-label small mb-0">Distinct by</label>' +
            '<input class="form-control form-control-sm font-monospace" value="' + esc(req.distinctBy || '') + '" onchange="setRequest(' + i + ',\'distinctBy\',this.value)"></div>' +
          '<div class="col-md-5"><label class="form-label small mb-0">Pagination (JSON)</label>' +
            '<input class="form-control form-control-sm font-monospace" value="' + esc(req.paginationConfig || '') + '" ' +
            'onchange="setRequest(' + i + ',\'paginationConfig\',this.value)" placeholder=\'{"kind":"Page","pageParam":"page","pageSize":100}\'></div>' +
        '</div>' +
        '<table class="table table-sm mb-1"><thead><tr>' +
          '<th class="small">Source field</th><th class="small">Target</th><th class="small">Kind</th><th></th>' +
        '</tr></thead><tbody>' + mappings + '</tbody></table>' +
        '<button class="btn btn-sm btn-outline-success" onclick="addMapping(' + i + ')"><i class="bi bi-plus"></i> Add mapping</button>' +
      '</div></div>';
  }).join('');
}

function setRequest(i, field, value) { model.requests[i][field] = value; if (field === 'targetSection') renderRequests(); renderDiscoverTargets(); }
function setMapping(i, j, field, value) { model.requests[i].mappings[j][field] = value; }
function addMapping(i) {
  const fields = TARGET_FIELDS[model.requests[i].targetSection] || [''];
  model.requests[i].mappings.push({ sourceExpression: '', targetField: fields[0], kind: 'Direct' });
  renderRequests();
}
function removeMapping(i, j) { model.requests[i].mappings.splice(j, 1); renderRequests(); }
function removeRequest(i) { model.requests.splice(i, 1); renderRequests(); renderDiscoverTargets(); }
function addRequest() {
  model.requests.push({ targetSection: 'Stops', format: 'Json', url: '', httpMethod: 'GET', dataPath: '', mappings: [] });
  renderRequests();
  renderDiscoverTargets();
}

function renderDiscoverTargets() {
  document.getElementById('discoverTarget').innerHTML = model.requests.map(function (r, i) {
    return '<button class="btn btn-sm btn-outline-secondary me-1 mb-1" onclick="discover(' + i + ')">' +
      '<i class="bi bi-search"></i> ' + esc(r.targetSection) + '</button>';
  }).join('') || '<span class="text-muted small">Add a request first.</span>';
}

function renderNode(node, depth) {
  const pad = 'padding-left:' + (depth * 14) + 'px';
  const isLeaf = !node.children || node.children.length === 0;
  const clickable = isLeaf || node.type === 'array';
  const sample = node.sample ? ' <span class="sample">= ' + esc(node.sample.slice(0, 60)) + '</span>' : '';
  const count = node.arrayItemCount != null ? ' <span class="kind">[' + node.arrayItemCount + ']</span>' : '';

  let html = '<div class="node ' + (clickable ? 'pickable' : '') + '" style="' + pad + '"' +
    (clickable ? ' onclick="pickPath(\'' + esc(node.path).replace(/'/g, "\\'") + '\')" title="Click to copy this path"' : '') + '>' +
    '<strong>' + esc(node.name || '(root)') + '</strong> <span class="kind">' + esc(node.type) + '</span>' + count + sample +
    '</div>';

  (node.children || []).forEach(function (c) { html += renderNode(c, depth + 1); });
  return html;
}

function pickPath(path) {
  navigator.clipboard && navigator.clipboard.writeText(path);
  status('Copied <code>' + esc(path) + '</code> — paste it into a Data path or Source field box.', 'secondary');
}

async function discover(i) {
  const req = model.requests[i];
  if (!req.url) { status('Give the request a URL first.', 'warning'); return; }
  document.getElementById('discoverOut').innerHTML = '<span class="text-muted"><span class="spinner-border spinner-border-sm"></span> Fetching sample...</span>';

  // Discovery reads the stored request, so what is on screen has to be persisted first — otherwise
  // it samples the previous URL and silently shows the wrong shape.
  const save = await fetch(BASE + '/custom-sources/' + SOURCE_ID, {
    method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(collect())
  });
  if (save.ok) model = await save.json();

  const r = await fetch(BASE + '/custom-sources/' + SOURCE_ID + '/discover?requestIndex=' + i, { method: 'POST' });

  const j = await r.json().catch(function () { return null; });
  if (!r.ok) {
    document.getElementById('discoverOut').innerHTML = '<span class="text-danger">' + esc((j && (j.detail || j.title)) || r.statusText) + '</span>';
    return;
  }

  let html = '';
  if (j.arrayPaths && j.arrayPaths.length) {
    html += '<div class="mb-2 pb-2 border-bottom"><div class="text-muted mb-1">Array paths — candidates for Data path:</div>' +
      j.arrayPaths.map(function (p) {
        return '<button class="btn btn-sm btn-outline-primary me-1 mb-1" onclick="useDataPath(' + i + ',\'' + esc(p).replace(/'/g, "\\'") + '\')">' + esc(p || '(root)') + '</button>';
      }).join('') + '</div>';
  }
  html += j.structure ? renderNode(j.structure, 0) : '<span class="text-muted">Empty response.</span>';
  if (j.logLines && j.logLines.length)
    html += '<div class="mt-2 pt-2 border-top text-muted">' + j.logLines.map(esc).join('<br>') + '</div>';

  document.getElementById('discoverOut').innerHTML = html;
}

function useDataPath(i, path) {
  model.requests[i].dataPath = path;
  const box = document.getElementById('dataPath' + i);
  if (box) box.value = path;
  status('Data path for <strong>' + esc(model.requests[i].targetSection) + '</strong> set to <code>' + esc(path || '(root)') + '</code>.', 'success');
}

async function loadUploads() {
  const r = await fetch(BASE + '/custom-sources/' + SOURCE_ID + '/uploads');
  if (!r.ok) { document.getElementById('uploadList').innerHTML = ''; return; }
  const files = await r.json();
  document.getElementById('uploadList').innerHTML = files.length
    ? '<table class="table table-sm small mb-0">' + files.map(function (f) {
        return '<tr><td><code>' + esc(f.url) + '</code></td>' +
          '<td class="text-end text-muted">' + Shell.num(f.sizeBytes) + ' B</td>' +
          '<td class="text-end"><button class="btn btn-sm btn-outline-secondary py-0" onclick="copyUrl(\'' + esc(f.url).replace(/'/g, "\\'") + '\')">copy</button></td></tr>';
      }).join('') + '</table>'
    : '<span class="text-muted small">No files uploaded.</span>';
}

function copyUrl(url) {
  navigator.clipboard && navigator.clipboard.writeText(url);
  status('Copied <code>' + esc(url) + '</code> — paste it into a request URL.', 'secondary');
}

async function uploadFile() {
  const input = document.getElementById('uploadFile');
  if (!input.files || !input.files[0]) { status('Choose a file first.', 'warning'); return; }

  const form = new FormData();
  form.append('file', input.files[0]);

  // No Content-Type header: the browser has to set the multipart boundary itself.
  const r = await fetch(BASE + '/custom-sources/' + SOURCE_ID + '/upload', { method: 'POST', body: form });
  const j = await r.json().catch(function () { return null; });
  if (!r.ok) { status('Upload failed: ' + esc((j && (j.detail || j.title)) || r.statusText), 'danger'); return; }

  status('Uploaded. Use <code>' + esc(j.url) + '</code> as a request URL.', 'success');
  input.value = '';
  loadUploads();
}

function collect() {
  return {
    name: document.getElementById('fName').value.trim(),
    kind: document.getElementById('fKind').value,
    refreshIntervalSeconds: parseInt(document.getElementById('fRefresh').value, 10) || 3600,
    serviceWindowStart: document.getElementById('fWindowStart').value || null,
    serviceWindowEnd: document.getElementById('fWindowEnd').value || null,
    extractorKey: document.getElementById('fExtractor').value.trim() || null,
    // `|| null` collapses the two states the API distinguishes into one. The server treats null as
    // "keep the stored credential" and an empty string as "clear it" — Protect passes blank through
    // unchanged and HasAuth is `!IsNullOrWhiteSpace`, so "" genuinely removes it. Because a blank
    // box becomes null here, the editor can replace a credential but has no way to remove one,
    // which is exactly what rotating away from an integration needs. Adding it means a deliberate
    // control ("Clear stored credential") rather than overloading the blank box, since blank
    // already means keep and the field's own help text says so.
    authConfig: document.getElementById('fAuth').value.trim() || null,
    isActive: document.getElementById('fActive').checked,
    requests: model.requests.map(function (r) {
      return {
        targetSection: r.targetSection,
        url: r.url,
        httpMethod: r.httpMethod || 'GET',
        format: r.format,
        dataPath: r.dataPath || '',
        paginationConfig: r.paginationConfig || null,
        distinctBy: r.distinctBy || null,
        mappings: (r.mappings || []).map(function (m) {
          return { sourceExpression: m.sourceExpression, targetField: m.targetField, kind: m.kind };
        })
      };
    })
  };
}

async function saveSource() {
  status('<span class="spinner-border spinner-border-sm"></span> Saving...', 'secondary');
  const r = await fetch(BASE + '/custom-sources/' + SOURCE_ID, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(collect())
  });
  const j = await r.json().catch(function () { return null; });
  if (!r.ok) { status('Save failed: ' + esc((j && (j.detail || j.title)) || r.statusText), 'danger'); return; }
  model = j;
  renderRequests();
  renderDiscoverTargets();
  status('Saved.', 'success');
}

async function previewSource() {
  document.getElementById('previewOut').innerHTML = '<span class="text-muted small"><span class="spinner-border spinner-border-sm"></span> Running extraction...</span>';
  // Saved first so the preview reflects what is on screen rather than what was last stored.
  const save = await fetch(BASE + '/custom-sources/' + SOURCE_ID, {
    method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(collect())
  });
  if (save.ok) model = await save.json();

  const r = await fetch(BASE + '/custom-sources/' + SOURCE_ID + '/preview', { method: 'POST' });
  const j = await r.json().catch(function () { return null; });
  if (!r.ok) {
    document.getElementById('previewOut').innerHTML = '<div class="alert alert-danger py-2 small mb-0">' + esc((j && (j.detail || j.title)) || r.statusText) + '</div>';
    return;
  }

  let html = '<div class="mb-2"><span class="badge bg-primary">' + esc(j.completeness) + '</span> ';
  html += (j.sectionsPresent || []).map(function (s) {
    return '<span class="badge bg-light text-dark border ms-1">' + esc(s) + ' ' + (j.rowCounts[s] || 0) + '</span>';
  }).join('');
  if (j.sectionsSynthesized && j.sectionsSynthesized.length)
    html += '<div class="small text-muted mt-1">synthesized: ' + j.sectionsSynthesized.map(esc).join(', ') + '</div>';
  html += '</div>';

  (j.warnings || []).forEach(function (w) {
    html += '<div class="alert alert-warning py-1 px-2 small mb-1">' + esc(w) + '</div>';
  });

  Object.keys(j.samples || {}).forEach(function (section) {
    const rows = j.samples[section];
    if (!rows.length) return;
    const cols = Object.keys(rows[0]);
    html += '<div class="mt-2"><div class="small fw-bold">' + esc(section) + '</div>' +
      '<div class="table-responsive"><table class="table table-sm table-bordered small mb-0"><thead><tr>' +
      cols.map(function (c) { return '<th>' + esc(c) + '</th>'; }).join('') + '</tr></thead><tbody>' +
      rows.map(function (row) {
        return '<tr>' + cols.map(function (c) { return '<td>' + esc(row[c]) + '</td>'; }).join('') + '</tr>';
      }).join('') + '</tbody></table></div></div>';
  });

  if (j.logLines && j.logLines.length)
    html += '<div class="mt-2 pt-2 border-top small text-muted">' + j.logLines.map(esc).join('<br>') + '</div>';

  document.getElementById('previewOut').innerHTML = html;
}

load();
