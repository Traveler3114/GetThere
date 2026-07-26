/* GetThereAPI admin shell — rail, topbar and the fetch/format helpers every
   admin page shares. Loaded before the page script; call Admin.mount() first. */
(function () {
  'use strict';

  var NAV = [
    {
      label: 'MONITOR',
      items: [
        { key: 'overview', text: 'Overview', href: 'index.html' },
        { key: 'audit', text: 'Audit log', href: 'audit.html' }
      ]
    },
    {
      label: 'IDENTITY',
      items: [
        { key: 'users', text: 'Users', href: 'users.html', count: 'totalUsers' }
      ]
    },
    {
      label: 'TICKETING',
      items: [
        { key: 'tickets', text: 'Tickets', href: 'tickets.html', count: 'totalTickets' },
        { key: 'adapters', text: 'Adapters', href: 'adapters.html', badge: 'adaptersDegraded' }
      ]
    }
  ];

  var Admin = {

    /* ------------------------------------------------------------- auth --- */

    token: function () {
      return sessionStorage.getItem('auth_token') || '';
    },

    headers: function () {
      var h = { 'Content-Type': 'application/json' };
      var t = Admin.token();
      if (t) h.Authorization = 'Bearer ' + t;
      return h;
    },

    requireAuth: function () {
      if (Admin.token()) return true;
      window.location.href = 'login.html';
      return false;
    },

    logout: function () {
      sessionStorage.removeItem('auth_token');
      sessionStorage.removeItem('refresh_token');
      window.location.href = 'login.html';
    },

    /** Claims from the access token, or null when it cannot be read. */
    claims: function () {
      var t = Admin.token();
      if (!t) return null;
      try {
        var part = t.split('.')[1].replace(/-/g, '+').replace(/_/g, '/');
        return JSON.parse(decodeURIComponent(escape(atob(part))));
      } catch (e) {
        return null;
      }
    },

    /* -------------------------------------------------------------- api --- */

    /** GET a JSON endpoint. Redirects to login on 401, throws otherwise. */
    api: async function (path, options) {
      var res = await fetch(path, Object.assign({ headers: Admin.headers() }, options || {}));
      if (res.status === 401) {
        Admin.logout();
        throw new Error('Session expired.');
      }
      if (!res.ok) {
        var detail = '';
        try {
          var problem = await res.json();
          detail = problem.detail || problem.title || '';
        } catch (e) { /* not a problem+json body */ }
        throw new Error(detail || 'Request failed (' + res.status + ')');
      }
      if (res.status === 204) return null;
      return res.json();
    },

    /* ----------------------------------------------------------- format --- */

    esc: function (value) {
      if (value === null || value === undefined) return '';
      return String(value).replace(/[&<>"']/g, function (c) {
        return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
      });
    },

    num: function (value) {
      if (value === null || value === undefined) return '—';
      return Number(value).toLocaleString('en-GB').replace(/,/g, ' ');
    },

    money: function (value, currency) {
      if (value === null || value === undefined) return '—';
      try {
        // Thin-space grouping, matching the design's "€4 812.50".
        return Number(value).toLocaleString('en-GB', {
          style: 'currency', currency: currency || 'EUR', minimumFractionDigits: 2
        }).replace(/,/g, ' ');
      } catch (e) {
        return Number(value).toFixed(2) + ' ' + (currency || 'EUR');
      }
    },

    percent: function (value, digits) {
      if (value === null || value === undefined) return '—';
      return Number(value).toFixed(digits === undefined ? 1 : digits) + '%';
    },

    date: function (value) {
      if (!value) return '—';
      var d = new Date(value);
      return isNaN(d) ? '—' : d.toLocaleDateString('en-GB');
    },

    dateTime: function (value) {
      if (!value) return '—';
      var d = new Date(value);
      return isNaN(d) ? '—' : d.toLocaleString('en-GB', { dateStyle: 'short', timeStyle: 'short' });
    },

    time: function (value) {
      if (!value) return '—';
      var d = new Date(value);
      return isNaN(d) ? '—' : d.toLocaleTimeString('en-GB', { hour12: false });
    },

    /** "3 min ago" / "in 4 h" for anything within a few days, else a date. */
    ago: function (value) {
      if (!value) return '—';
      var then = new Date(value).getTime();
      if (isNaN(then)) return '—';
      var minutes = Math.round((Date.now() - then) / 60000);
      var abs = Math.abs(minutes);
      var suffix = minutes < 0 ? '' : ' ago';
      var prefix = minutes < 0 ? 'in ' : '';
      if (abs < 1) return 'just now';
      if (abs < 60) return prefix + abs + ' min' + suffix;
      if (abs < 60 * 36) return prefix + Math.round(abs / 60) + ' h' + suffix;
      return Admin.date(value);
    },

    delta: function (value) {
      if (value === null || value === undefined || !isFinite(value)) return '';
      var cls = value > 0 ? 'is-up' : (value < 0 ? 'is-down' : 'is-flat');
      var sign = value > 0 ? '+' : '';
      return '<span class="kpi-delta ' + cls + '">' + sign + Number(value).toFixed(1) + '%</span>';
    },

    initials: function (text) {
      if (!text) return '··';
      var clean = String(text).split('@')[0].replace(/[._-]+/g, ' ').trim();
      var parts = clean.split(/\s+/).filter(Boolean);
      if (!parts.length) return '··';
      var out = parts.length === 1 ? parts[0].slice(0, 2) : parts[0][0] + parts[1][0];
      return out.toUpperCase();
    },

    /** Maps a payment/ticket/adapter status onto a badge modifier. */
    statusClass: function (status) {
      switch (String(status || '').toLowerCase()) {
        case 'completed':
        case 'active':
        case 'ok':
          return 'is-ok';
        case 'pending':
        case 'degraded':
        case 'unregistered':
          return 'is-warn';
        case 'failed':
        case 'failing':
        case 'expired':
          return 'is-danger';
        case 'refunded':
          return 'is-accent';
        default:
          return 'is-neutral';
      }
    },

    /* ------------------------------------------------------------ shell --- */

    /**
     * Renders the rail and topbar into #shell.
     * @param {{active:string, title:string, meta?:string, crumb?:string, actions?:string}} page
     */
    mount: function (page) {
      if (!Admin.requireAuth()) return false;

      var host = document.getElementById('shell');
      if (!host) return false;

      var claims = Admin.claims() || {};
      var who = claims.email || claims.name || claims.sub || '';
      var env = /^(localhost|127\.0\.0\.1|\[::1\])$/.test(window.location.hostname) ? 'LOCAL' : 'PRODUCTION';

      host.className = 'shell';
      host.innerHTML =
        '<aside class="rail">' +
          '<div class="rail-head">' +
            '<div class="rail-mark"><span class="icon icon-logo"></span></div>' +
            '<div class="rail-name"><b>GetThereAPI</b><span>AppDB · private</span></div>' +
          '</div>' +
          '<nav class="rail-nav">' + NAV.map(renderGroup(page.active)).join('') + '</nav>' +
          '<div class="rail-foot">' +
            '<div style="display:flex;align-items:center;gap:8px">' +
              '<span class="dot is-idle" id="railTransitDot"></span>' +
              '<span id="railTransitText">TransitInfoAPI — checking</span>' +
            '</div>' +
            '<div class="mono">one-way · GlobalId reference</div>' +
          '</div>' +
        '</aside>' +
        '<div class="main">' +
          '<header class="topbar">' +
            (page.crumb
              ? '<span class="crumb">' + Admin.esc(page.crumb) + '</span><span class="sep">/</span>'
              : '') +
            '<h1>' + Admin.esc(page.title) + '</h1>' +
            (page.meta ? '<div class="topbar-meta">' + Admin.esc(page.meta) + '</div>' : '') +
            '<div class="spacer"></div>' +
            (page.actions || '') +
            '<div class="env-badge">' + env + '</div>' +
            '<div class="avatar" id="railAvatar" title="' + Admin.esc(who) + ' — sign out">' +
              Admin.esc(Admin.initials(who)) +
            '</div>' +
          '</header>' +
          '<div class="content" id="content"></div>' +
        '</div>';

      document.getElementById('railAvatar').addEventListener('click', function () {
        if (window.confirm('Sign out of the admin console?')) Admin.logout();
      });

      Admin.probeTransitInfo();
      return true;
    },

    /**
     * Fills the rail counters once stats are known.
     * @param {object} stats /admin/stats payload
     */
    applyCounts: function (stats) {
      if (!stats) return;
      Object.keys(stats).forEach(function (key) {
        document.querySelectorAll('[data-count="' + key + '"]').forEach(function (el) {
          var value = stats[key];
          if (el.dataset.countStyle === 'badge') {
            if (!value) { el.classList.add('hidden'); return; }
            el.classList.remove('hidden');
            el.textContent = value;
          } else {
            el.textContent = Admin.num(value);
          }
        });
      });
    },

    /** One-way reachability probe: any successful proxy call means TransitInfoAPI answered. */
    probeTransitInfo: async function () {
      var dot = document.getElementById('railTransitDot');
      var text = document.getElementById('railTransitText');
      if (!dot || !text) return;
      try {
        await Admin.api('/api/map/transport-types');
        dot.className = 'dot is-live';
        text.textContent = 'TransitInfoAPI reachable';
      } catch (e) {
        dot.className = 'dot is-danger';
        text.textContent = 'TransitInfoAPI unreachable';
      }
    },

    /** Replaces #content with a one-off error panel. */
    fail: function (message) {
      var el = document.getElementById('content');
      if (el) el.innerHTML = '<div class="alert">' + Admin.esc(message) + '</div>';
    }
  };

  function renderGroup(active) {
    return function (group) {
      return '<div class="rail-group">' +
        '<div class="rail-label">' + group.label + '</div>' +
        group.items.map(function (item) {
          var right = '';
          if (item.count) {
            right = '<span class="nav-count" data-count="' + item.count + '">—</span>';
          } else if (item.badge) {
            right = '<span class="badge is-warn hidden" data-count="' + item.badge +
              '" data-count-style="badge"></span>';
          }
          return '<a class="nav-item' + (item.key === active ? ' is-active' : '') + '" href="' + item.href + '">' +
            '<span><span class="nav-dot"></span>' + item.text + '</span>' + right +
          '</a>';
        }).join('') +
      '</div>';
    };
  }

  window.Admin = Admin;
})();
