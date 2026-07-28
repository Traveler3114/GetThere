'use strict';

var form = document.getElementById('loginForm');
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

form.addEventListener('submit', async function (e) {
  e.preventDefault();
  errorEl.classList.add('hidden');
  submitEl.disabled = true;
  submitEl.textContent = 'Signing in…';

  try {
    var res = await fetch('/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        email: document.getElementById('email').value,
        password: passwordEl.value
      })
    });

    if (!res.ok) {
      fail(res.status === 401 || res.status === 400 ? 'Invalid credentials.' : 'Sign-in failed (' + res.status + ').');
      return;
    }

    var data = await res.json();
    sessionStorage.setItem('auth_token', data.accessToken);
    sessionStorage.setItem('refresh_token', data.refreshToken);
    window.location.href = 'index.html';
  } catch (ex) {
    fail('Network error — is the API running?');
  }
});

function fail(message) {
  errorEl.textContent = message;
  errorEl.classList.remove('hidden');
  submitEl.disabled = false;
  submitEl.textContent = 'Sign in';
}
