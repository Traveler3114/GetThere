(function () {
  var token = sessionStorage.getItem('auth_token') || localStorage.getItem('auth_token');
  if (!token) {
    var cookie = document.cookie.split('; ').find(function (r) { return r.startsWith('auth_token='); });
    if (cookie) token = cookie.split('=')[1];
  }
  if (!token && !window.location.pathname.endsWith('/login.html')) {
    sessionStorage.setItem('redirect_after_login', window.location.href);
    window.location.href = '/admin/login.html';
    return;
  }
  if (token) {
    var origFetch = window.fetch;
    window.fetch = function (url, opts) {
      opts = opts || {};
      opts.headers = opts.headers || {};
      opts.headers['Authorization'] = 'Bearer ' + token;
      return origFetch.call(this, url, opts);
    };
  }
})();
