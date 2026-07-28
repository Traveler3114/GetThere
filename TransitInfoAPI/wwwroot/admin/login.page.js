'use strict';

var errorEl = document.getElementById('error');
var submitEl = document.getElementById('submit');
var passwordEl = document.getElementById('password');
var revealEl = document.getElementById('reveal');

revealEl.addEventListener('click', function () {
  var show = passwordEl.type === 'password';
  passwordEl.type = show ? 'text' : 'password';
  revealEl.title = show ? 'Hide password' : 'Show password';
  revealEl.firstElementChild.className = 'icon ' + (show ? 'icon-eye-off' : 'icon-eye');
  revealEl.firstElementChild.style.width = '18px';
  revealEl.firstElementChild.style.height = '18px';
});

document.getElementById('loginForm').addEventListener('submit', async function (e) {
  e.preventDefault();
  errorEl.classList.add('hidden');
  submitEl.disabled = true;
  submitEl.textContent = 'Signing in…';

  try {
    var resp = await fetch('/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        email: document.getElementById('username').value,
        password: passwordEl.value
      })
    });

    if (!resp.ok) {
      fail(resp.status === 401 || resp.status === 400 ? 'Invalid credentials.' : 'Sign-in failed (' + resp.status + ').');
      return;
    }

    var data = await resp.json();
    sessionStorage.setItem('auth_token', data.accessToken);
    // Needed by Shell.refresh(): the access token expires after 15 minutes, and without this the
    // console drops the operator back here mid-task.
    if (data.refreshToken) sessionStorage.setItem('refresh_token', data.refreshToken);
    window.location.href = redirectTarget();
  } catch (ex) {
    fail('Network error — is the API running?');
  }
});

/** Only same-origin /admin/ paths are honoured, so a stale value cannot redirect off-site. */
function redirectTarget() {
  var stored = sessionStorage.getItem('redirect_after_login');
  sessionStorage.removeItem('redirect_after_login');
  if (!stored) return '/admin/';
  try {
    var url = new URL(stored, window.location.origin);
    if (url.origin === window.location.origin && url.pathname.indexOf('/admin/') === 0 &&
        !url.pathname.endsWith('/login.html')) {
      return url.pathname + url.search + url.hash;
    }
  } catch (e) { /* fall through to the default */ }
  return '/admin/';
}

function fail(message) {
  errorEl.textContent = message;
  errorEl.classList.remove('hidden');
  submitEl.disabled = false;
  submitEl.textContent = 'Sign in';
}
