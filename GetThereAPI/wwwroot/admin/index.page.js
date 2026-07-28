'use strict';

var PAGE_SIZE = 7;
var state = { status: '', page: 1, stats: null };

var STATUS_FILTERS = [
  { key: '', text: 'All' },
  { key: 'Completed', text: 'Completed' },
  { key: 'Pending', text: 'Pending' },
  { key: 'Failed', text: 'Failed' },
  { key: 'Refunded', text: 'Refunded' }
];

var timeZone = 'local';
try { timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone || 'local'; } catch (e) { /* keep default */ }

if (Admin.mount({ active: 'overview', title: 'Overview', meta: 'last 24h · ' + timeZone })) {
  renderSkeleton();
  load();
}

function renderSkeleton() {
  document.getElementById('content').innerHTML =
    '<div class="kpis" id="kpis">' + repeat(4,
      '<div class="kpi"><div class="skeleton" style="width:60%"></div>' +
      '<div class="skeleton" style="height:26px;width:45%"></div>' +
      '<div class="skeleton" style="width:80%"></div></div>') +
    '</div>' +
    '<div class="split">' +
      '<div class="card">' +
        '<div class="card-head">' +
          '<div class="card-title">Recent purchases</div>' +
          '<div class="pills" id="purchaseFilters" style="margin-left:auto"></div>' +
        '</div>' +
        '<div id="purchases"><div class="loading">Loading purchases</div></div>' +
      '</div>' +
      '<div class="stack">' +
        '<div class="card">' +
          '<div class="card-head">' +
            '<div class="card-title">Adapter health</div>' +
            '<span class="dot is-idle" id="adapterPulse"></span>' +
          '</div>' +
          '<div id="adapters"><div class="loading">Loading adapters</div></div>' +
        '</div>' +
        '<div class="card is-flush is-grow">' +
          '<div class="card-title">Needs attention</div>' +
          '<div id="attention" class="stack" style="gap:12px"></div>' +
        '</div>' +
      '</div>' +
    '</div>';

  renderFilters();
}

function repeat(n, html) {
  var out = '';
  for (var i = 0; i < n; i++) out += html;
  return out;
}

async function load() {
  try {
    state.stats = await Admin.api('/admin/stats');
    Admin.applyCounts(state.stats);
    renderKpis(state.stats);
  } catch (e) {
    document.getElementById('kpis').innerHTML = '<div class="alert" style="grid-column:1/-1">' +
      Admin.esc(e.message) + '</div>';
  }

  loadPurchases();
  loadAdapters();
}

/* ------------------------------------------------------------------ kpis --- */

function renderKpis(s) {
  var spark = s.ticketsSoldDaily || [];
  var peak = Math.max.apply(null, spark.concat([1]));
  var successClass = s.purchaseSuccessRate >= 97 ? '' : (s.purchaseSuccessRate >= 90 ? ' is-warn' : ' is-danger');

  document.getElementById('kpis').innerHTML =
    kpi('TICKETS SOLD',
      '<div class="kpi-row"><span class="kpi-value">' + Admin.num(s.ticketsSold) + '</span>' +
        Admin.delta(s.ticketsSoldChangePercent) + '</div>' +
      '<div class="spark">' + spark.map(function (v) {
        return '<i style="height:' + Math.max(4, Math.round(v / peak * 100)) + '%"></i>';
      }).join('') + '</div>') +

    kpi('GROSS VOLUME',
      '<div class="kpi-row"><span class="kpi-value">' + Admin.money(s.grossVolume, s.currency) + '</span>' +
        Admin.delta(s.grossVolumeChangePercent) + '</div>' +
      '<div class="kpi-sub">avg basket ' + Admin.money(s.averageBasket, s.currency) +
        ' · ' + Admin.num(s.refunds) + ' refunds</div>') +

    kpi('WALLET FLOAT',
      '<div class="kpi-value">' + Admin.money(s.walletFloat, s.currency) + '</div>' +
      '<div class="kpi-sub">top-ups ' + Admin.money(s.topUps, s.currency) +
        ' <span class="faint">/</span> spend ' + Admin.money(s.spend, s.currency) + '</div>') +

    kpi('PURCHASE SUCCESS',
      '<div class="kpi-row"><span class="kpi-value">' + Admin.percent(s.purchaseSuccessRate) + '</span>' +
        Admin.delta(s.purchaseSuccessRateChangePercent) + '</div>' +
      '<div class="meter' + successClass + '"><i style="width:' +
        Math.max(0, Math.min(100, s.purchaseSuccessRate)) + '%"></i></div>');
}

function kpi(label, body) {
  return '<div class="kpi"><div class="kpi-label">' + label + '</div>' + body + '</div>';
}

/* ------------------------------------------------------------- purchases --- */

function renderFilters() {
  document.getElementById('purchaseFilters').innerHTML = STATUS_FILTERS.map(function (f) {
    return '<button type="button" class="pill' + (f.key === state.status ? ' is-active' : '') +
      '" data-status="' + f.key + '">' + f.text + '</button>';
  }).join('');

  document.querySelectorAll('#purchaseFilters .pill').forEach(function (btn) {
    btn.addEventListener('click', function () {
      state.status = btn.dataset.status;
      state.page = 1;
      renderFilters();
      loadPurchases();
    });
  });
}

var PURCHASE_COLS = '132px minmax(0,1fr) 120px 92px 92px 84px';

async function loadPurchases() {
  var host = document.getElementById('purchases');
  host.innerHTML = '<div class="loading">Loading purchases</div>';

  var query = '/admin/purchases?page=' + state.page + '&pageSize=' + PAGE_SIZE +
    (state.status ? '&status=' + encodeURIComponent(state.status) : '');

  try {
    var result = await Admin.api(query);
    var rows = result.data || [];

    if (!rows.length) {
      host.innerHTML = '<div class="empty">No purchases' +
        (state.status ? ' with status ' + Admin.esc(state.status) : '') + ' yet.</div>';
      return;
    }

    host.innerHTML =
      '<div class="table-scroll"><div class="table" style="--cols:' + PURCHASE_COLS + '">' +
        '<div class="thead"><div>Ticket id</div><div>User</div><div>Operator</div>' +
          '<div>Adapter</div><div>Amount</div><div>Status</div></div>' +
        rows.map(purchaseRow).join('') +
      '</div></div>' +
      pageFooter(result);

    wirePager(result, function (page) { state.page = page; loadPurchases(); });
  } catch (e) {
    host.innerHTML = '<div class="alert" style="margin:18px">' + Admin.esc(e.message) + '</div>';
  }
}

function purchaseRow(p) {
  var ref = p.externalTicketId || (p.ticketId ? 'TKT-' + p.ticketId : 'PUR-' + p.id);
  var status = p.paymentStatus === 'Completed' && p.ticketStatus ? p.ticketStatus : p.paymentStatus;
  return '<div class="trow' + (p.paymentStatus === 'Failed' ? ' is-flagged' : '') + '"' +
      ' title="' + Admin.esc(p.optionName + ' · ' + Admin.dateTime(p.purchasedAt) +
        (p.failureReason ? ' · ' + p.failureReason : '')) + '">' +
    '<div class="id">' + Admin.esc(ref) + '</div>' +
    '<div>' + Admin.esc(p.userEmail || '—') + '</div>' +
    '<div>' + Admin.esc(p.operatorName) + '</div>' +
    '<div class="sub">' + Admin.esc(p.adapterType) + '</div>' +
    '<div class="num">' + Admin.money(p.amount, p.currency) + '</div>' +
    '<div><span class="badge ' + Admin.statusClass(status) + '">' +
      Admin.esc(String(status).toUpperCase()) + '</span></div>' +
  '</div>';
}

function pageFooter(result) {
  var from = result.total === 0 ? 0 : (result.page - 1) * result.pageSize + 1;
  var to = Math.min(result.total, result.page * result.pageSize);
  return '<div class="card-foot">' +
    '<span class="mono">' + from + '–' + to + ' of ' + Admin.num(result.total) + '</span>' +
    '<span class="pager">' +
      '<button type="button" class="btn" data-page="prev"' +
        (result.hasPreviousPage ? '' : ' disabled') + '>Prev</button>' +
      '<button type="button" class="btn is-accent" data-page="next"' +
        (result.hasNextPage ? '' : ' disabled') + '>Next</button>' +
    '</span></div>';
}

function wirePager(result, go) {
  var prev = document.querySelector('[data-page="prev"]');
  var next = document.querySelector('[data-page="next"]');
  if (prev) prev.addEventListener('click', function () { go(result.page - 1); });
  if (next) next.addEventListener('click', function () { go(result.page + 1); });
}

/* -------------------------------------------------------------- adapters --- */

async function loadAdapters() {
  var host = document.getElementById('adapters');
  try {
    var adapters = await Admin.api('/admin/adapters');

    if (!adapters.length) {
      host.innerHTML = '<div class="empty">No ticketing adapters configured.</div>';
    } else {
      host.innerHTML = '<div class="list">' + adapters.map(adapterRow).join('') + '</div>';
    }

    var healthy = adapters.every(function (a) { return a.status === 'Ok' || a.status === 'Idle'; });
    var pulse = document.getElementById('adapterPulse');
    pulse.className = 'dot ' + (adapters.length === 0 ? 'is-idle' : (healthy ? 'is-live' : 'is-warn'));

    renderAttention(adapters);
  } catch (e) {
    host.innerHTML = '<div class="alert" style="margin:18px">' + Admin.esc(e.message) + '</div>';
    renderAttention([]);
  }
}

function adapterRow(a) {
  var tone = a.status === 'Degraded' || a.status === 'Unregistered' ? ' is-warn'
    : (a.status === 'Failing' ? ' is-danger' : '');
  var detail = a.isRegistered
    ? (a.requiredInputs.length ? a.requiredInputs.join(', ') : 'no inputs') +
      ' · ' + Admin.num(a.ticketOptions) + ' options · ' + Admin.num(a.purchases) + ' buys'
    : 'no SDK implementation registered';

  return '<div class="row">' +
    '<div class="tile' + tone + '">' + Admin.esc(Admin.initials(a.name)) + '</div>' +
    '<div class="grow"><b>' + Admin.esc(a.adapterType) + '</b><span>' + Admin.esc(detail) + '</span></div>' +
    '<span class="badge ' + Admin.statusClass(a.status) + '">' +
      Admin.esc(a.status.toUpperCase()) + '</span>' +
  '</div>';
}

/* ------------------------------------------------------------- attention --- */

function renderAttention(adapters) {
  var s = state.stats || {};
  var notes = [];

  if (s.pendingPurchases) {
    notes.push(note('is-accent',
      Admin.num(s.pendingPurchases) + ' payment' + (s.pendingPurchases === 1 ? '' : 's') + ' awaiting capture',
      'Oldest held ' + Admin.ago(s.oldestPendingPurchaseAt) + ' — settle or void before the ticket expires.'));
  }

  adapters.filter(function (a) { return a.status === 'Failing' || a.status === 'Degraded'; })
    .forEach(function (a) {
      notes.push(note(a.status === 'Failing' ? 'is-danger' : 'is-warn',
        a.adapterType + ' — ' + a.status.toLowerCase(),
        Admin.num(a.failures) + ' of ' + Admin.num(a.purchases) + ' purchases failed in the last 24h.'));
    });

  adapters.filter(function (a) { return a.isActive && !a.isRegistered; })
    .forEach(function (a) {
      notes.push(note('is-warn', a.name + ' has no adapter implementation',
        'Type "' + a.adapterType + '" is enabled in AppDB but nothing is registered in AdapterRegistry.'));
    });

  adapters.filter(function (a) { return a.isActive && a.isRegistered && a.ticketOptions === 0; })
    .forEach(function (a) {
      notes.push(note('', a.name + ' has no ticket options',
        'Bound to ' + a.transitInfoGlobalId + ' — the buy button stays hidden until options exist.'));
    });

  adapters.filter(function (a) { return a.isActive && a.isRegistered && !a.hasApiKey; })
    .forEach(function (a) {
      notes.push(note('', a.name + ' has no stored credential',
        'Purchases will fail if the upstream API requires a key. See docs/secrets-rotation.md.'));
    });

  document.getElementById('attention').innerHTML = notes.length
    ? notes.slice(0, 6).join('')
    : '<div class="empty">Nothing needs attention.</div>';
}

function note(tone, title, body) {
  return '<div class="note ' + tone + '"><div><b>' + Admin.esc(title) + '</b>' +
    '<span>' + Admin.esc(body) + '</span></div></div>';
}
