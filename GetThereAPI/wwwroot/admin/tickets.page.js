'use strict';

var TABS = [
  { key: 'options', text: 'Ticket options' },
  { key: 'tickets', text: 'Issued tickets' }
];

var state = { tab: 'options' };

if (Admin.mount({
  active: 'tickets',
  title: 'Tickets',
  meta: 'options come from the bound adapter',
  actions: '<button type="button" class="btn" id="refresh">Refresh</button>'
})) {
  document.getElementById('refresh').addEventListener('click', function () { load(); });
  document.getElementById('content').innerHTML =
    '<div class="card is-grow">' +
      '<div class="card-head">' +
        '<div class="pills" id="tabs"></div>' +
        '<div class="spacer"></div>' +
        '<div class="card-meta" id="meta">—</div>' +
      '</div>' +
      '<div id="list"><div class="loading">Loading</div></div>' +
    '</div>';

  renderTabs();
  load();
}

function renderTabs() {
  document.getElementById('tabs').innerHTML = TABS.map(function (t) {
    return '<button type="button" class="pill' + (t.key === state.tab ? ' is-active' : '') +
      '" data-tab="' + t.key + '">' + t.text + '</button>';
  }).join('');

  document.querySelectorAll('#tabs .pill').forEach(function (btn) {
    btn.addEventListener('click', function () {
      state.tab = btn.dataset.tab;
      renderTabs();
      load();
    });
  });
}

async function load() {
  var host = document.getElementById('list');
  host.innerHTML = '<div class="loading">Loading</div>';
  try {
    if (state.tab === 'options') await loadOptions(host);
    else await loadTickets(host);
  } catch (e) {
    host.innerHTML = '<div class="alert" style="margin:18px">' + Admin.esc(e.message) + '</div>';
  }
}

var OPTION_COLS = 'minmax(0,1fr) 140px 110px 96px 96px 92px';

async function loadOptions(host) {
  var options = await Admin.api('/tickets/options');
  document.getElementById('meta').textContent = options.length + ' option(s)';

  if (!options.length) {
    host.innerHTML = '<div class="empty">No ticket options are configured. ' +
      'Options belong to an adapter — check the Adapters screen.</div>';
    return;
  }

  host.innerHTML = '<div class="table-scroll"><div class="table" style="--cols:' + OPTION_COLS + '">' +
    '<div class="thead"><div>Name</div><div>Adapter</div><div>Price</div>' +
      '<div>Format</div><div>Duration</div><div>State</div></div>' +
    options.map(function (o) {
      return '<div class="trow" title="' + Admin.esc(o.description || '') + '">' +
        '<div><b>' + Admin.esc(o.name) + '</b>' +
          (o.description ? ' <span class="dim">· ' + Admin.esc(o.description) + '</span>' : '') + '</div>' +
        '<div class="sub">' + Admin.esc(o.adapterName) + '</div>' +
        '<div class="num">' + Admin.money(o.price, o.currency) + '</div>' +
        '<div><span class="badge is-neutral">' + Admin.esc(o.ticketFormat) + '</span></div>' +
        '<div class="sub">' + (o.durationMinutes ? o.durationMinutes + ' min' : '—') + '</div>' +
        '<div><span class="badge ' + (o.isActive === false ? 'is-neutral' : 'is-ok') + '">' +
          (o.isActive === false ? 'INACTIVE' : 'ACTIVE') + '</span></div>' +
      '</div>';
    }).join('') +
  '</div></div>';
}

var TICKET_COLS = '80px minmax(0,1fr) 96px 150px 150px 96px';

async function loadTickets(host) {
  var tickets = await Admin.api('/tickets');
  document.getElementById('meta').textContent = tickets.length + ' ticket(s) for the signed-in account';

  if (!tickets.length) {
    host.innerHTML = '<div class="empty">No tickets have been issued to this account.</div>';
    return;
  }

  host.innerHTML = '<div class="table-scroll"><div class="table" style="--cols:' + TICKET_COLS + '">' +
    '<div class="thead"><div>Id</div><div>Option</div><div>Format</div>' +
      '<div>Valid from</div><div>Valid to</div><div>Status</div></div>' +
    tickets.map(function (t) {
      return '<div class="trow">' +
        '<div class="id">' + Admin.esc(t.id) + '</div>' +
        '<div>' + Admin.esc(t.option ? t.option.name : '—') + '</div>' +
        '<div><span class="badge is-neutral">' + Admin.esc(t.format) + '</span></div>' +
        '<div class="sub">' + Admin.dateTime(t.validFrom) + '</div>' +
        '<div class="sub">' + Admin.dateTime(t.validTo) + '</div>' +
        '<div><span class="badge ' + Admin.statusClass(t.status) + '">' +
          Admin.esc(String(t.status).toUpperCase()) + '</span></div>' +
      '</div>';
    }).join('') +
  '</div></div>';
}
