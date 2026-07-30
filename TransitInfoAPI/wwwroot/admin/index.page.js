'use strict';

var FEED_FILTERS = [
  { key: 'all', text: 'All' },
  { key: 'static', text: 'Static' },
  { key: 'realtime', text: 'Realtime' },
  { key: 'problems', text: 'Problems' }
];

var state = { filter: 'all', feeds: [], versions: {} };

if (Shell.mount({
  active: 'overview',
  title: 'Overview',
  meta: 'canonical network · data health',
  actions: '<a class="btn is-primary" href="/admin/feeds.html">Manage feeds</a>'
})) {
  document.getElementById('content').innerHTML =
    '<div class="kpis" id="kpis">' + skeletonKpis() + '</div>' +
    '<div class="split is-wide">' +
      '<div class="card">' +
        '<div class="card-head">' +
          '<div class="card-title">Feeds</div>' +
          '<div class="pills" id="feedFilters" style="margin-left:auto"></div>' +
        '</div>' +
        '<div id="feeds"><div class="loading">Loading feeds</div></div>' +
      '</div>' +
      '<div class="stack">' +
        '<div class="card">' +
          '<div class="card-head">' +
            '<div class="card-title">Reconciliation queue</div>' +
            '<div class="spacer"></div>' +
            '<a href="/admin/reconciliation.html" style="font-size:11.5px;font-weight:600">Review all →</a>' +
          '</div>' +
          '<div id="queue"><div class="loading">Loading queue</div></div>' +
        '</div>' +
        '<div class="card is-flush is-grow">' +
          '<div style="display:flex;align-items:center;justify-content:space-between">' +
            '<div class="card-title">Service alerts</div>' +
            '<span class="card-meta">GTFS-RT</span>' +
          '</div>' +
          '<div id="alerts" class="stack" style="gap:12px"><div class="loading">Loading alerts</div></div>' +
        '</div>' +
      '</div>' +
    '</div>';

  renderFilters();
  load();
}

function skeletonKpis() {
  var one = '<div class="kpi"><div class="skeleton" style="width:60%"></div>' +
    '<div class="skeleton" style="height:26px;width:45%"></div>' +
    '<div class="skeleton" style="width:80%"></div></div>';
  return one + one + one + one;
}

async function load() {
  var results = await Promise.all([
    Shell.tryApi('/stations?perPage=1'),
    Shell.tryApi('/feeds?perPage=500'),
    Shell.tryApi('/reconciliation/pending?perPage=1'),
    Shell.tryApi('/reconciliation/auto-merged?perPage=1'),
    Shell.tryApi('/realtime/vehicles'),
    Shell.tryApi('/realtime/alerts'),
    Shell.tryApi('/routes?perPage=1')
  ]);

  var stations = results[0];
  var feeds = results[1];
  var pending = results[2];
  var autoMerged = results[3];
  var vehicles = results[4];
  var alerts = results[5];
  var routes = results[6];

  state.feeds = (feeds && (feeds.data || feeds)) || [];

  Shell.applyCounts({
    stations: stations ? stations.total : null,
    feeds: state.feeds.length,
    pending: pending ? pending.total : 0,
    alerts: alerts ? alerts.length : 0
  });

  await loadVersions();
  renderKpis({
    stations: stations ? stations.total : null,
    routes: routes ? routes.total : null,
    pending: pending ? pending.total : 0,
    autoMerged: autoMerged ? autoMerged.total : 0,
    vehicles: vehicles ? (vehicles.length || 0) : null
  });

  renderFeeds();
  renderQueue(pending);
  renderAlerts(alerts);
}

/** Latest version per static feed, so the table can show import status. */
async function loadVersions() {
  var statics = state.feeds.filter(function (f) { return f.feedType === 'GTFSStatic'; }).slice(0, 20);
  await Promise.all(statics.map(async function (f) {
    var res = await Shell.tryApi('/feeds/' + f.id + '/versions?perPage=1');
    var list = res && (res.data || res);
    if (list && list.length) state.versions[f.id] = list[0];
  }));
}

/* ------------------------------------------------------------------ kpis --- */

function renderKpis(d) {
  var health = feedHealth();

  document.getElementById('kpis').innerHTML =
    kpi('CANONICAL STATIONS', '',
      '<div class="kpi-value">' + Shell.num(d.stations) + '</div>' +
      '<div class="kpi-sub">' + Shell.num(d.routes) + ' canonical routes</div>') +

    kpi('FEEDS HEALTHY', '',
      '<div class="kpi-row"><span class="kpi-value">' + health.ok + '</span>' +
        '<span class="kpi-unit">/ ' + health.total + '</span></div>' +
      '<div class="bars">' + health.bars.map(function (tone) {
        return '<i' + (tone ? ' class="is-' + tone + '"' : '') + '></i>';
      }).join('') + '</div>') +

    kpi('VEHICLES LIVE', '<span class="dot ' + (d.vehicles ? 'is-live' : 'is-idle') + '"></span>',
      '<div class="kpi-value">' + Shell.num(d.vehicles) + '</div>' +
      '<div class="kpi-sub">GTFS-RT vehicle positions</div>') +

    '<div class="kpi is-accent"><div class="kpi-label">REVIEW QUEUE</div>' +
      '<div class="kpi-row"><span class="kpi-value">' + Shell.num(d.pending) + '</span>' +
        '<span class="kpi-unit">flagged matches</span></div>' +
      '<div class="kpi-sub">' + Shell.num(d.autoMerged) + ' auto-merged</div></div>';
}

function kpi(label, adornment, body) {
  return '<div class="kpi"><div class="kpi-label">' + label + adornment + '</div>' + body + '</div>';
}

function feedHealth() {
  var bars = state.feeds.slice(0, 20).map(function (f) {
    var status = feedStatus(f);
    if (status.tone === 'ok') return '';
    return status.tone;
  });
  var ok = state.feeds.filter(function (f) { return feedStatus(f).tone === 'ok'; }).length;
  return { ok: ok, total: state.feeds.length, bars: bars.length ? bars : [''] };
}

/** Derives a display status from the feed row plus its latest version. */
function feedStatus(f) {
  if (!f.isActive) return { text: 'INACTIVE', tone: 'warn', badge: 'is-neutral' };

  var version = state.versions[f.id];
  if (!version) {
    return f.feedType === 'GTFSStatic'
      ? { text: 'NO IMPORT', tone: 'warn', badge: 'is-warn' }
      : { text: 'LIVE', tone: 'ok', badge: 'is-ok' };
  }

  switch (String(version.importStatus)) {
    case 'Success': return { text: 'SUCCESS', tone: 'ok', badge: 'is-ok' };
    case 'Failed': return { text: 'FAILED', tone: 'danger', badge: 'is-danger' };
    // The data imported but its stops were never linked to canonical stations, so nothing on this
    // feed resolves on the map. Repairable via POST /feeds/versions/{id}/reconcile — surfaced
    // loudly because it is otherwise a completely silent half-working state.
    case 'ReconciliationPending': return { text: 'NEEDS RECONCILE', tone: 'danger', badge: 'is-danger' };
    case 'Importing':
    case 'Pending': return { text: 'IMPORTING', tone: 'warn', badge: 'is-info' };
    default: return { text: String(version.importStatus || 'UNKNOWN').toUpperCase(), tone: 'warn', badge: 'is-neutral' };
  }
}

/* ----------------------------------------------------------------- feeds --- */

function renderFilters() {
  document.getElementById('feedFilters').innerHTML = FEED_FILTERS.map(function (f) {
    return '<button type="button" class="pill' + (f.key === state.filter ? ' is-active' : '') +
      '" data-filter="' + f.key + '">' + f.text + '</button>';
  }).join('');

  document.querySelectorAll('#feedFilters .pill').forEach(function (btn) {
    btn.addEventListener('click', function () {
      state.filter = btn.dataset.filter;
      renderFilters();
      renderFeeds();
    });
  });
}

var FEED_COLS = '150px minmax(0,1fr) 92px 108px 128px 92px';

function renderFeeds() {
  var host = document.getElementById('feeds');
  var rows = state.feeds.filter(function (f) {
    switch (state.filter) {
      case 'static': return f.feedType === 'GTFSStatic';
      case 'realtime': return f.feedType === 'GTFSRealtime' || f.feedType === 'GBFS';
      case 'problems': return feedStatus(f).tone !== 'ok';
      default: return true;
    }
  });

  if (!rows.length) {
    host.innerHTML = '<div class="empty">No feeds match this filter.</div>';
    return;
  }

  host.innerHTML = '<div class="table-scroll"><div class="table" style="--cols:' + FEED_COLS + '">' +
    '<div class="thead"><div>Feed id</div><div>Operator</div><div>Type</div>' +
      '<div>Version</div><div>Last import</div><div>Status</div></div>' +
    rows.slice(0, 40).map(function (f) {
      var version = state.versions[f.id];
      var status = feedStatus(f);
      return '<div class="trow' + (status.tone === 'danger' ? ' is-flagged' : '') + '">' +
        '<div class="id" title="' + Shell.esc(f.onestopId || '') + '">' + Shell.esc(f.feedId) + '</div>' +
        '<div>' + Shell.esc(f.operatorName || '—') +
          (f.isInternal ? ' <span class="dim">— internal</span>' : '') + '</div>' +
        '<div class="muted">' + Shell.esc(feedTypeLabel(f.feedType)) + '</div>' +
        // FeedVersionResponse serialises this as `sha1`; reading `.sha` meant the Version column
        // rendered an em dash for every feed, including the healthy ones.
        '<div class="sub">' + Shell.esc(version && version.sha1 ? String(version.sha1).slice(0, 7) : '—') + '</div>' +
        '<div class="muted">' + (version ? Shell.ago(version.importedAt || version.fetchedAt) :
          Shell.num(f.refreshIntervalSeconds) + 's cycle') + '</div>' +
        '<div><span class="badge ' + status.badge + '">' + status.text + '</span></div>' +
      '</div>';
    }).join('') +
  '</div></div>' +
  '<div class="card-foot"><span class="mono">' +
    Math.min(rows.length, 40) + ' of ' + rows.length + ' shown</span>' +
    '<a href="/admin/feeds.html" style="font-size:11px">Open feeds →</a></div>';
}

function feedTypeLabel(type) {
  switch (type) {
    case 'GTFSStatic': return 'static';
    case 'GTFSRealtime': return 'realtime';
    case 'GBFS': return 'mobility';
    default: return type || '—';
  }
}

/* ----------------------------------------------------------------- queue --- */

function renderQueue(pending) {
  var host = document.getElementById('queue');
  if (!pending) {
    host.innerHTML = '<div class="empty">Reconciliation queue unavailable.</div>';
    return;
  }
  if (!pending.total) {
    host.innerHTML = '<div class="empty">Nothing flagged — every match cleared the auto-merge threshold.</div>';
    return;
  }

  Shell.tryApi('/reconciliation/pending?perPage=4').then(function (page) {
    var items = (page && page.data) || [];
    host.innerHTML = '<div class="list">' + items.map(function (item) {
      var tone = Shell.scoreTone(item.confidenceScore);
      return '<a class="row" href="/admin/reconciliation.html#' + item.id + '">' +
        // suggestedStationDetail lives on ReconciliationDetailResponse, which /reconciliation/pending
        // does not return — so this side of the ↔ was undefined and every row read "canonical".
        // Pairs identifiers with identifiers now; the bold line above carries the names.
        '<div class="grow"><b>' + Shell.esc(item.suggestedStationName || item.rawStopName) + '</b>' +
          '<span>' + Shell.esc(item.rawStopGtfsId || '') + ' ↔ ' +
            (item.suggestedStationId ? '#' + Shell.esc(item.suggestedStationId) : 'new station') +
          '</span></div>' +
        '<span style="font-size:11px;font-weight:700;color:var(--' + tone + '-text)">' +
          Number(item.confidenceScore).toFixed(2) + '</span>' +
      '</a>';
    }).join('') + '</div>';
  });
}

/* ---------------------------------------------------------------- alerts --- */

function renderAlerts(alerts) {
  var host = document.getElementById('alerts');
  if (!alerts) {
    host.innerHTML = '<div class="empty">Alerts unavailable.</div>';
    return;
  }
  if (!alerts.length) {
    host.innerHTML = '<div class="empty">No active service alerts.</div>';
    return;
  }

  host.innerHTML = alerts.slice(0, 4).map(function (a, index) {
    return '<div class="note' + (index === 0 ? ' is-warn' : '') + '"><div>' +
      '<b>' + Shell.esc(a.headerText || 'Untitled alert') + '</b>' +
      '<span>' + Shell.esc(a.descriptionText || (a.cause || '') + ' · ' + (a.effect || '')) + '</span>' +
    '</div></div>';
  }).join('');
}
