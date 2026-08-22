/* TransitInfoAPI admin shell — rail, topbar and shared helpers for the pages
   built on the imported design system. Legacy Bootstrap pages keep using
   admin-auth.js instead. */
(function () {
  'use strict';

  var NAV = [
    {
      label: 'MONITOR',
      items: [
        { key: 'overview', text: 'Overview', href: '/admin/index.html' },
        { key: 'realtime', text: 'Realtime', href: '/admin/realtime.html', live: true },
        { key: 'alerts', text: 'Alerts', href: '/admin/alerts.html', badge: 'alerts' }
      ]
    },
    {
      label: 'IDENTITY',
      items: [
        { key: 'operators', text: 'Operators', href: '/admin/operators.html' },
        { key: 'agencies', text: 'Agencies', href: '/admin/agencies.html' },
        { key: 'countries', text: 'Countries', href: '/admin/countries.html' }
      ]
    },
    {
      label: 'NETWORK',
      items: [
        { key: 'feeds', text: 'Feeds', href: '/admin/feeds.html', count: 'feeds' },
        { key: 'custom-sources', text: 'Custom sources', href: '/admin/custom-sources.html' },
        { key: 'alert-sources', text: 'Alert sources', href: '/admin/alert-sources.html' },
        { key: 'feed-versions', text: 'Feed versions', href: '/admin/feed-versions.html' },
        { key: 'stations', text: 'Stations', href: '/admin/stations.html', count: 'stations' },
        { key: 'routes', text: 'Routes', href: '/admin/routes.html' },
        { key: 'places', text: 'Places', href: '/admin/places.html' },
        { key: 'mobility', text: 'Mobility', href: '/admin/mobility.html' },
        { key: 'reconciliation', text: 'Reconciliation', href: '/admin/reconciliation.html', pill: 'pending' },
        { key: 'admin-map', text: 'Admin map', href: '/admin/map.html' },
        { key: 'map', text: 'Public map', href: '/map/' }
      ]
    }
  ];

  var Shell = {

    token: function () { return sessionStorage.getItem('auth_token') || ''; },

    headers: function () {
      var h = { 'Content-Type': 'application/json' };
      var t = Shell.token();
      if (t) h.Authorization = 'Bearer ' + t;
      return h;
    },

    requireAuth: function () {
      if (Shell.token()) return true;
      sessionStorage.setItem('redirect_after_login', window.location.href);
      window.location.href = '/admin/login.html';
      return false;
    },

    logout: function () {
      sessionStorage.removeItem('auth_token');
      sessionStorage.removeItem('refresh_token');
      window.location.href = '/admin/login.html';
    },

    claims: function () {
      var t = Shell.token();
      if (!t) return null;
      try {
        var part = t.split('.')[1].replace(/-/g, '+').replace(/_/g, '/');
        return JSON.parse(decodeURIComponent(escape(atob(part))));
      } catch (e) {
        return null;
      }
    },

    /**
     * Exchanges the stored refresh token for a new access token.
     * Access tokens live 15 minutes, so without this the console logged the operator out mid-task.
     *
     * Defers to admin-auth.js when it is present, which is every page but index.html. Both files
     * implemented this, each with its own in-flight guard — and two guards serialise nothing, so a
     * 401 inside Shell.api() could start this refresh while the fetch wrapper started its own and
     * both presented the same refresh token. The server now claims that row conditionally, so the
     * loser of that race is read as a replayed token and the operator's whole session family is
     * revoked. One guard, shared, is the fix.
     *
     * The body below stays as the fallback for index.html, which loads no admin-auth.js.
     */
    refresh: function () {
      if (window.AdminAuth && window.AdminAuth.refresh) return window.AdminAuth.refresh();

      if (Shell._refreshInFlight) return Shell._refreshInFlight;

      var refreshToken = sessionStorage.getItem('refresh_token');
      if (!refreshToken) return Promise.resolve(false);

      Shell._refreshInFlight = fetch('/auth/refresh', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ refreshToken: refreshToken })
      }).then(function (res) {
        if (!res.ok) return false;
        return res.json().then(function (data) {
          if (!data || !data.accessToken) return false;
          sessionStorage.setItem('auth_token', data.accessToken);
          if (data.refreshToken) sessionStorage.setItem('refresh_token', data.refreshToken);
          return true;
        });
      }).catch(function () {
        return false;
      }).finally(function () {
        Shell._refreshInFlight = null;
      });

      return Shell._refreshInFlight;
    },

    _refreshInFlight: null,

    /**
     * Builds fetch init with the auth headers merged in rather than replaced. The previous form —
     * Object.assign({ headers: Shell.headers() }, options) — let a caller's own `headers` overwrite
     * the whole object, dropping Authorization and turning an ordinary POST into a 401 and a forced
     * sign-out. No caller passes headers today; this is so none has to know not to.
     */
    _init: function (options) {
      var init = Object.assign({}, options || {});
      init.headers = Object.assign(Shell.headers(), init.headers || {});
      return init;
    },

    api: async function (path, options) {
      var res = await fetch(path, Shell._init(options));

      if (res.status === 401) {
        var refreshed = await Shell.refresh();
        if (refreshed) {
          // Rebuilt, not reused: Shell.headers() has to read the token refresh just stored.
          res = await fetch(path, Shell._init(options));
        }
      }

      if (res.status === 401) {
        Shell.logout();
        throw new Error('Session expired.');
      }
      if (!res.ok) {
        var detail = '';
        try {
          var problem = await res.json();
          detail = problem.detail || problem.title || problem.message || '';
        } catch (e) { /* not a problem+json body */ }
        throw new Error(detail || 'Request failed (' + res.status + ')');
      }
      if (res.status === 204) return null;
      return res.json();
    },

    /** Same as api(), but resolves to null instead of throwing. */
    tryApi: async function (path) {
      try { return await Shell.api(path); } catch (e) { return null; }
    },

    esc: function (value) {
      if (value === null || value === undefined) return '';
      return String(value).replace(/[&<>"']/g, function (c) {
        return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
      });
    },

    // Only http(s) survives. Operator- and feed-supplied URLs are rendered as links on several
    // pages, and esc() alone does not help against a javascript: href -- escaping the quotes keeps
    // the attribute well-formed, and the browser still runs the scheme. Anything else becomes '#'.
    //
    // Lives here for the same reason esc does: it was three identical copies, in feeds, operators
    // and alerts.
    safeUrl: function (value) {
      if (!value) return '#';
      try {
        var parsed = new URL(value, window.location.origin);
        return (parsed.protocol === 'http:' || parsed.protocol === 'https:') ? value : '#';
      } catch (e) {
        return '#';
      }
    },

    num: function (value) {
      if (value === null || value === undefined) return '—';
      return Number(value).toLocaleString('en-GB').replace(/,/g, ' ');
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

    ago: function (value) {
      if (!value) return '—';
      var then = new Date(value).getTime();
      if (isNaN(then)) return '—';
      var minutes = Math.round((Date.now() - then) / 60000);
      if (minutes < 1) return 'just now';
      if (minutes < 60) return minutes + ' min ago';
      if (minutes < 60 * 36) return Math.round(minutes / 60) + ' h ago';
      return Shell.date(value);
    },

    initials: function (text) {
      if (!text) return '··';
      var clean = String(text).split('@')[0].replace(/[._-]+/g, ' ').trim();
      var parts = clean.split(/\s+/).filter(Boolean);
      if (!parts.length) return '··';
      var out = parts.length === 1 ? parts[0].slice(0, 2) : parts[0][0] + parts[1][0];
      return out.toUpperCase();
    },

    /** Confidence score → badge/bar tone. */
    scoreTone: function (score) {
      if (score >= 0.85) return 'ok';
      if (score >= 0.6) return 'warn';
      return 'danger';
    },

    mount: function (page) {
      return buildShell(page, '<div class="content" id="content"></div>');
    },

    /**
     * Mounts the rail and topbar around a page that still renders Bootstrap markup, moving that
     * page's existing content inside instead of replacing it.
     * <p>
     * These pages were reachable only by typing a URL or going back to the overview: they carried a
     * `← Home > Admin` text breadcrumb and no navigation at all, so the console had a rail on
     * exactly one of its fifteen screens. Their markup and page scripts are untouched — the content
     * area is filled by relocation, not by a rewrite, which is what makes this safe to apply to all
     * of them at once.
     * <p>
     * The content div deliberately carries **no `id`**. Every legacy page already owns an element
     * with `id="content"` that its own script writes into, and creating a second one here would
     * silently break whichever the script reached first.
     * <p>
     * <b>`#page` is never hidden, and must not be.</b> The first version of this marked it
     * `hidden` and relied on the relocation below to reveal it, which made every failure
     * catastrophic: a stale cached copy of this script, a load error, anything that threw before
     * the move left the attribute in place and the screen entirely blank. Leaving the content
     * visible means the worst case is the page rendering as it did before the shell existed —
     * unstyled chrome, but readable and fully usable. Degrade to the old page, never to nothing.
     *
     * @param page {{active:string, title:string, crumb?:string, meta?:string, actions?:string}}
     */
    mountLegacy: function (page) {
      var body = document.getElementById('page');
      if (!body) return false;
      if (!buildShell(page, '<div class="content is-legacy" data-legacy-content></div>')) return false;

      var target = document.querySelector('[data-legacy-content]');
      // Moved rather than copied via innerHTML: these nodes may already have listeners bound by a
      // script that ran before this call, and re-serialising them would drop every one.
      while (body.firstChild) target.appendChild(body.firstChild);
      body.remove();

      // body.bs pads the top of the page, which is right for a bare container and wrong once the
      // rail owns the full viewport height.
      document.body.classList.add('has-shell');
      return true;
    },

    /** @param {{feeds?:number, stations?:number, pending?:number, alerts?:number}} counts */
    applyCounts: function (counts) {
      Object.keys(counts || {}).forEach(function (key) {
        document.querySelectorAll('[data-count="' + key + '"]').forEach(function (el) {
          var value = counts[key];
          if (el.dataset.countStyle && !value) { el.classList.add('hidden'); return; }
          el.classList.remove('hidden');
          el.textContent = el.dataset.countStyle ? value : Shell.num(value);
        });
      });
    },

    fail: function (message) {
      var el = document.getElementById('content');
      if (el) el.innerHTML = '<div class="alert">' + Shell.esc(message) + '</div>';
    }
  };

  /**
   * Renders the rail and topbar into `#shell` and wires the sign-out avatar.
   * `contentHtml` is what fills the main area — an empty `#content` for design-system pages, a
   * relocation target for legacy ones.
   */
  function buildShell(page, contentHtml) {
    if (!Shell.requireAuth()) return false;

    var host = document.getElementById('shell');
    if (!host) return false;

    var claims = Shell.claims() || {};
    var who = claims.email || claims.name || claims.sub || '';

    host.className = 'shell';
    host.innerHTML =
      '<aside class="rail">' +
        '<div class="rail-head">' +
          '<div class="rail-mark">T</div>' +
          '<div class="rail-name"><b>TransitInfoAPI</b><span>TransitDB · standalone</span></div>' +
        '</div>' +
        '<nav class="rail-nav">' + NAV.map(renderGroup(page.active)).join('') + '</nav>' +
        '<div class="rail-foot">' +
          '<div style="display:flex;align-items:center;gap:8px">' +
            '<span class="dot is-live"></span><span id="railFeedNote">GTFS-RT polling</span>' +
          '</div>' +
          '<div class="mono">knows nothing of GetThereAPI</div>' +
        '</div>' +
      '</aside>' +
      '<div class="main">' +
        '<header class="topbar">' +
          (page.crumb ? '<span class="crumb">' + Shell.esc(page.crumb) + '</span><span class="sep">/</span>' : '') +
          '<h1>' + Shell.esc(page.title) + '</h1>' +
          (page.meta ? '<div class="topbar-meta">' + Shell.esc(page.meta) + '</div>' : '') +
          '<div class="spacer"></div>' +
          (page.actions || '') +
          '<div class="avatar" id="railAvatar" title="' + Shell.esc(who) + ' — sign out">' +
            Shell.esc(Shell.initials(who)) + '</div>' +
        '</header>' +
        contentHtml +
      '</div>';

    document.getElementById('railAvatar').addEventListener('click', function () {
      if (window.confirm('Sign out of the admin console?')) Shell.logout();
    });

    return true;
  }

  function renderGroup(active) {
    return function (group) {
      return '<div class="rail-group">' +
        '<div class="rail-label">' + group.label + '</div>' +
        group.items.map(function (item) {
          var right = '';
          if (item.count) right = '<span class="nav-count" data-count="' + item.count + '">—</span>';
          else if (item.badge) right = '<span class="badge is-warn hidden" data-count="' + item.badge + '" data-count-style="badge"></span>';
          else if (item.pill) right = '<span class="badge is-accent hidden" data-count="' + item.pill + '" data-count-style="badge"></span>';
          else if (item.live) right = '<span class="dot is-live"></span>';

          return '<a class="nav-item' + (item.key === active ? ' is-active' : '') + '" href="' + item.href + '">' +
            '<span><span class="nav-dot"></span>' + item.text + '</span>' + right +
          '</a>';
        }).join('') +
      '</div>';
    };
  }

  window.Shell = Shell;
})();
