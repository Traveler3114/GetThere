(function () {
  'use strict';

  // Attaches the operator's bearer token to same-origin API calls, and renews it when it expires.
  //
  // This used to read the token once at load and bake it into the fetch wrapper. Access tokens live
  // 15 minutes, so every page using this stopped working after 15 minutes: requests 401'd, nothing
  // refreshed, and nothing sent the operator back to the login page either — the redirect below
  // only fires when there is no token *at load*. The refresh token sat unused in sessionStorage the
  // whole time.
  //
  // admin-shell.js already implements this correctly, but it is loaded only by index.html. The same
  // rules are applied here: read the token per request, share one in-flight refresh, retry once.

  var TOKEN_KEY = 'auth_token';
  var REFRESH_KEY = 'refresh_token';

  var origFetch = window.fetch.bind(window);

  function token() { return sessionStorage.getItem(TOKEN_KEY) || ''; }

  function toLogin() {
    sessionStorage.setItem('redirect_after_login', window.location.href);
    window.location.href = '/admin/login.html';
  }

  var refreshInFlight = null;

  // Concurrent callers share one request: the server rotates the refresh token and revokes the old
  // one, so a second parallel refresh would present a replayed token and trip reuse detection,
  // which revokes every session the operator has.
  function refresh() {
    if (refreshInFlight) return refreshInFlight;

    var stored = sessionStorage.getItem(REFRESH_KEY);
    if (!stored) return Promise.resolve(false);

    refreshInFlight = origFetch('/auth/refresh', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ refreshToken: stored })
    }).then(function (res) {
      if (!res.ok) return false;
      return res.json().then(function (data) {
        if (!data || !data.accessToken) return false;
        sessionStorage.setItem(TOKEN_KEY, data.accessToken);
        if (data.refreshToken) sessionStorage.setItem(REFRESH_KEY, data.refreshToken);
        return true;
      });
    }).catch(function () {
      return false;
    }).finally(function () {
      refreshInFlight = null;
    });

    return refreshInFlight;
  }

  /** True for requests this console makes to its own API — the only ones the token belongs on. */
  function isSameOrigin(url) {
    try {
      // fetch() accepts a string, a URL, or a Request as its first argument. A Request has no
      // toString of its own, so it stringifies to "[object Request]" — which new URL() happily
      // resolves as a same-origin *relative path* (…/[object Request]), classifying every Request as
      // same-origin. MapLibre fetches its tiles/glyphs/sprites as cross-origin Request objects
      // (tiles.openfreemap.org); mislabelled same-origin, they got the admin bearer token attached,
      // which both leaks the credential and forces a CORS preflight the tile host rejects — leaving
      // the map blank. Read the real URL off a Request before resolving.
      var href = (url && typeof url === 'object' && typeof url.url === 'string') ? url.url : url;
      return new URL(href, window.location.origin).origin === window.location.origin;
    } catch (e) {
      return false;
    }
  }

  function withAuth(opts) {
    // Copied rather than mutated: callers reuse their options object, and writing the header back
    // into it leaks one request's credentials into the next. The old version also assigned into
    // opts.headers directly, which silently did nothing when a caller passed a Headers instance.
    var next = Object.assign({}, opts || {});
    var headers = new Headers(next.headers || {});
    var t = token();
    if (t) headers.set('Authorization', 'Bearer ' + t);
    next.headers = headers;
    return next;
  }

  window.fetch = function (url, opts) {
    // Never attach the token to a third-party URL. Nothing here does that today, but the wrapper is
    // global and the cost of being wrong is handing an admin credential to another origin.
    if (!isSameOrigin(url)) return origFetch(url, opts);

    return origFetch(url, withAuth(opts)).then(function (res) {
      if (res.status !== 401) return res;

      return refresh().then(function (renewed) {
        if (!renewed) {
          sessionStorage.removeItem(TOKEN_KEY);
          sessionStorage.removeItem(REFRESH_KEY);
          toLogin();
          return res;
        }
        // withAuth re-reads the token, so the retry carries the new one.
        return origFetch(url, withAuth(opts));
      });
    });
  };

  // The single refresh implementation for the console.
  //
  // admin-shell.js had its own, with its own in-flight guard, and the two are loaded together on
  // every page except index.html. Two guards do not serialise anything: a 401 inside Shell.api()
  // could start Shell's refresh while this wrapper started its own, and both would present the same
  // refresh token.
  //
  // That used to be survivable — the server's rotation was a read-modify-write, so both requests
  // "succeeded" and quietly minted two successors. It is not survivable now: rotation claims the row
  // conditionally, so the loser is treated as a replayed token and the operator's entire session
  // family is revoked. Exporting the implementation is what makes Shell able to share this guard
  // instead of racing it.
  window.AdminAuth = {
    token: token,
    refresh: refresh,
    toLogin: toLogin
  };

  // Redirect last, so window.AdminAuth is published even on the way out. Anything that loaded
  // before the navigation completes then still finds a usable object rather than undefined.
  if (!token() && !window.location.pathname.endsWith('/login.html')) {
    toLogin();
  }
})();
