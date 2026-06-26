'use strict';

/* MediaHub Admin dashboard — vanilla JS, no framework, no CDN. */

// ---- Theme (System -> Light -> Dark), persisted in localStorage ------------
const THEME_KEY = 'mediahub.theme';
const THEME_ORDER = ['system', 'light', 'dark'];
const THEME_ICON = { system: '🖥️', light: '☀️', dark: '🌙' };

function getStoredTheme() {
  const t = localStorage.getItem(THEME_KEY);
  return THEME_ORDER.includes(t) ? t : 'system';
}

function applyTheme(theme) {
  const html = document.documentElement;
  if (theme === 'system') {
    html.removeAttribute('data-theme'); // let prefers-color-scheme decide
  } else {
    html.setAttribute('data-theme', theme);
  }
  const btn = document.getElementById('theme-toggle');
  if (btn) {
    btn.textContent = THEME_ICON[theme];
    btn.title = `Theme: ${theme} (click to change)`;
    btn.setAttribute('aria-label', `Theme: ${theme}. Click to change.`);
  }
}

function cycleTheme() {
  const current = getStoredTheme();
  const next = THEME_ORDER[(THEME_ORDER.indexOf(current) + 1) % THEME_ORDER.length];
  localStorage.setItem(THEME_KEY, next);
  applyTheme(next);
}

// Apply as early as possible to minimize flash.
applyTheme(getStoredTheme());

// ---- API helpers -----------------------------------------------------------
async function api(path, options = {}) {
  const opts = Object.assign({ credentials: 'include' }, options);
  const res = await fetch(path, opts);
  return res;
}

async function apiJson(path, method, body) {
  const res = await api(path, {
    method,
    headers: { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  let data = null;
  try { data = await res.json(); } catch (_) { /* may be empty */ }
  return { res, data };
}

// ---- UI helpers ------------------------------------------------------------
const $ = (id) => document.getElementById(id);

function show(el, visible) {
  if (el) el.hidden = !visible;
}

function banner(message, kind) {
  const el = $('banner');
  if (!message) { el.hidden = true; return; }
  el.textContent = message;
  el.className = 'banner ' + (kind || '');
  el.hidden = false;
  if (kind === 'success') setTimeout(() => { el.hidden = true; }, 3500);
}

function fmtDate(iso) {
  if (!iso) return '';
  const d = new Date(iso);
  return isNaN(d) ? iso : d.toLocaleString();
}

function fmtDuration(sec) {
  if (sec == null) return '—';
  const s = Number(sec);
  const m = Math.floor(s / 60);
  const r = s % 60;
  return `${m}:${String(r).padStart(2, '0')}`;
}

function fmtBytes(n) {
  if (n == null) return '—';
  const units = ['B', 'KB', 'MB', 'GB'];
  let v = Number(n), i = 0;
  while (v >= 1024 && i < units.length - 1) { v /= 1024; i++; }
  return `${v.toFixed(i === 0 ? 0 : 1)} ${units[i]}`;
}

// ---- App state -------------------------------------------------------------
const views = {
  setup: 'setup-view',
  login: 'login-view',
  dash: 'dash-view',
};

function showView(name) {
  for (const [k, id] of Object.entries(views)) show($(id), k === name);
}

// ---- Bootstrap -------------------------------------------------------------
async function init() {
  $('theme-toggle').addEventListener('click', cycleTheme);
  $('logout-btn').addEventListener('click', doLogout);
  wireSetup();
  wireLogin();
  wireTabs();
  wireVideoForm();
  wireSettings();

  await refreshState();
}

async function refreshState() {
  banner('');
  try {
    const { res, data } = await apiJson('/api/admin/setup-state', 'GET');
    if (!res.ok) throw new Error('Could not reach the server.');
    if (data.needsSetup) {
      setAuthedChrome(false);
      showView('setup');
    } else if (data.authenticated) {
      await enterDashboard();
    } else {
      setAuthedChrome(false);
      showView('login');
    }
  } catch (e) {
    banner(e.message || 'Failed to load.', 'error');
    setAuthedChrome(false);
    showView('login');
  }
}

function setAuthedChrome(authed, username) {
  show($('logout-btn'), authed);
  const who = $('who');
  if (authed && username) { who.textContent = username; show(who, true); }
  else show(who, false);
}

// ---- Setup -----------------------------------------------------------------
function wireSetup() {
  $('setup-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    const username = $('setup-username').value.trim();
    const password = $('setup-password').value;
    const confirm = $('setup-confirm').value;
    if (password !== confirm) { banner('Passwords do not match.', 'error'); return; }
    const { res, data } = await apiJson('/api/admin/setup', 'POST', { username, password });
    if (res.ok) {
      banner('');
      await enterDashboard(data && data.username);
    } else if (res.status === 409) {
      banner('An admin already exists. Please sign in.', 'error');
      showView('login');
    } else {
      banner((data && data.error) || 'Setup failed.', 'error');
    }
  });
}

// ---- Login -----------------------------------------------------------------
function wireLogin() {
  $('login-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    const username = $('login-username').value.trim();
    const password = $('login-password').value;
    const { res, data } = await apiJson('/api/admin/login', 'POST', { username, password });
    if (res.ok) {
      banner('');
      $('login-password').value = '';
      await enterDashboard(data && data.username);
    } else {
      banner('Invalid username or password.', 'error');
    }
  });
}

async function doLogout() {
  await apiJson('/api/admin/logout', 'POST');
  setAuthedChrome(false);
  showView('login');
}

// ---- Dashboard -------------------------------------------------------------
async function enterDashboard(username) {
  if (!username) {
    const { res, data } = await apiJson('/api/admin/me', 'GET');
    username = res.ok && data ? data.username : '';
  }
  setAuthedChrome(true, username);
  showView('dash');
  selectTab('videos');
  await Promise.all([loadVideos(), loadReleases(), loadSettings()]);
}

function wireTabs() {
  document.querySelectorAll('.tab').forEach((tab) => {
    tab.addEventListener('click', () => selectTab(tab.dataset.tab));
  });
}

function selectTab(name) {
  document.querySelectorAll('.tab').forEach((t) => {
    t.setAttribute('aria-selected', String(t.dataset.tab === name));
  });
  ['videos', 'releases', 'settings'].forEach((n) => {
    show($('panel-' + n), n === name);
  });
}

// ---- Videos ----------------------------------------------------------------
function wireVideoForm() {
  // Toggle field visibility by selected source mode.
  document.querySelectorAll('input[name="vmode"]').forEach((r) => {
    r.addEventListener('change', () => applyVideoMode());
  });
  applyVideoMode();

  $('upload-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    const mode = document.querySelector('input[name="vmode"]:checked').value;
    let res, data;

    if (mode === 'upload') {
      const file = $('v-file').files[0];
      if (!file) { banner('Choose a file to upload.', 'error'); return; }
      const fd = new FormData();
      fd.append('file', file);
      fd.append('title', $('v-title').value);
      fd.append('description', $('v-desc').value);
      if ($('v-thumb').value) fd.append('thumbnailUrl', $('v-thumb').value);
      if ($('v-duration').value) fd.append('durationSeconds', $('v-duration').value);
      const r = await api('/api/admin/videos', { method: 'POST', body: fd });
      res = r; try { data = await r.json(); } catch (_) {}
    } else {
      const body = {
        title: $('v-title').value,
        description: $('v-desc').value || null,
        objectKey: $('v-objectkey').value,
        thumbnailUrl: $('v-thumb').value || null,
        durationSeconds: $('v-duration').value ? Number($('v-duration').value) : null,
        mimeType: $('v-mime').value || null,
      };
      const r = await apiJson('/api/admin/videos', 'POST', body);
      res = r.res; data = r.data;
    }

    if (res.ok) {
      banner('Video added.', 'success');
      $('upload-form').reset();
      applyVideoMode();
      await loadVideos();
    } else if (res.status === 401) {
      await refreshState();
    } else {
      banner((data && data.error) || 'Failed to add video.', 'error');
    }
  });
}

function applyVideoMode() {
  const mode = document.querySelector('input[name="vmode"]:checked').value;
  document.querySelectorAll('[data-mode]').forEach((el) => {
    show(el, el.dataset.mode === mode);
  });
}

async function loadVideos() {
  const { res, data } = await apiJson('/api/admin/videos', 'GET');
  if (res.status === 401) { await refreshState(); return; }
  const tbody = $('videos-table').querySelector('tbody');
  tbody.innerHTML = '';
  const list = (data && data.videos) || [];
  show($('videos-empty'), list.length === 0);
  for (const v of list) {
    const tr = document.createElement('tr');
    tr.appendChild(td(v.title));
    tr.appendChild(td(fmtDuration(v.durationSeconds)));
    tr.appendChild(td(fmtDate(v.createdAt)));
    const actions = document.createElement('td');
    const del = document.createElement('button');
    del.className = 'btn danger';
    del.textContent = 'Delete';
    del.addEventListener('click', () => deleteVideo(v.id, v.title));
    actions.appendChild(del);
    tr.appendChild(actions);
    tbody.appendChild(tr);
  }
}

async function deleteVideo(id, title) {
  if (!confirm(`Delete "${title}"? This removes the catalog row and the R2 object.`)) return;
  const res = await api('/api/admin/videos/' + encodeURIComponent(id), { method: 'DELETE' });
  if (res.ok) {
    banner('Video deleted.', 'success');
    await loadVideos();
  } else if (res.status === 401) {
    await refreshState();
  } else {
    banner('Failed to delete video.', 'error');
  }
}

// ---- Releases --------------------------------------------------------------
async function loadReleases() {
  // Latest (public endpoint) — 204 means none yet.
  const latestEl = $('latest-release');
  try {
    const r = await api('/api/app/latest');
    if (r.status === 204) {
      latestEl.textContent = 'No release published yet.';
    } else if (r.ok) {
      const l = await r.json();
      latestEl.innerHTML = '';
      addKv(latestEl, 'Version', `${l.versionName} (code ${l.versionCode})`);
      addKv(latestEl, 'Size', fmtBytes(l.sizeBytes));
      addKv(latestEl, 'min SDK', l.minSdk);
      addKv(latestEl, 'Published', fmtDate(l.publishedAt));
      if (l.notes) addKv(latestEl, 'Notes', l.notes);
    }
  } catch (_) {
    latestEl.textContent = 'Could not load latest release.';
  }

  // Full list (admin endpoint).
  const { res, data } = await apiJson('/api/admin/releases', 'GET');
  if (res.status === 401) { await refreshState(); return; }
  const tbody = $('releases-table').querySelector('tbody');
  tbody.innerHTML = '';
  const list = (data && data.releases) || [];
  show($('releases-empty'), list.length === 0);
  for (const r of list) {
    const tr = document.createElement('tr');
    tr.appendChild(td(String(r.versionCode)));
    tr.appendChild(td(r.versionName));
    tr.appendChild(td(fmtBytes(r.sizeBytes)));
    tr.appendChild(td(String(r.minSdk)));
    tr.appendChild(td(fmtDate(r.publishedAt)));
    tbody.appendChild(tr);
  }
}

// ---- Settings --------------------------------------------------------------
function wireSettings() {
  $('settings-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    const body = {
      accountId: $('s-account').value,
      d1DatabaseId: $('s-dbid').value,
      d1ApiToken: $('s-token').value, // blank => unchanged (server-side)
      r2AccessKeyId: $('s-akid').value,
      r2SecretAccessKey: $('s-secret').value,
      r2VideoBucket: $('s-vbucket').value,
      r2ApkBucket: $('s-abucket').value,
      r2ServiceUrl: $('s-serviceurl').value,
      r2PresignTtlMinutes: $('s-ttl').value ? Number($('s-ttl').value) : null,
    };
    const { res, data } = await apiJson('/api/admin/settings', 'PUT', body);
    if (res.ok) {
      banner('Settings saved.', 'success');
      fillSettings(data);
    } else if (res.status === 401) {
      await refreshState();
    } else {
      banner((data && data.error) || 'Failed to save settings.', 'error');
    }
  });

  $('test-btn').addEventListener('click', async () => {
    const btn = $('test-btn');
    btn.disabled = true;
    const out = $('test-result');
    out.hidden = false;
    out.innerHTML = '<div class="muted">Testing…</div>';
    try {
      const { res, data } = await apiJson('/api/admin/settings/test', 'POST');
      if (res.status === 401) { await refreshState(); return; }
      out.innerHTML = '';
      out.appendChild(testLine('D1', data.d1));
      out.appendChild(testLine('R2', data.r2));
    } catch (_) {
      out.innerHTML = '<div class="banner error">Test request failed.</div>';
    } finally {
      btn.disabled = false;
    }
  });
}

function testLine(label, result) {
  const div = document.createElement('div');
  div.className = 'test-line ' + (result && result.ok ? 'ok' : 'bad');
  const dot = document.createElement('span');
  dot.className = 'dot';
  div.appendChild(dot);
  const text = document.createElement('span');
  text.textContent = `${label}: ${result ? result.message : 'no result'}`;
  div.appendChild(text);
  return div;
}

async function loadSettings() {
  const { res, data } = await apiJson('/api/admin/settings', 'GET');
  if (res.status === 401) { await refreshState(); return; }
  if (res.ok) fillSettings(data);
}

function fillSettings(s) {
  if (!s) return;
  $('s-account').value = s.accountId || '';
  $('s-dbid').value = s.d1DatabaseId || '';
  $('s-vbucket').value = s.r2VideoBucket || '';
  $('s-abucket').value = s.r2ApkBucket || '';
  $('s-serviceurl').value = s.r2ServiceUrl || '';
  $('s-ttl').value = s.r2PresignTtlMinutes || '';
  // Secrets: never populated; show a hint about the stored value, keep field blank.
  $('s-token').value = '';
  $('s-akid').value = '';
  $('s-secret').value = '';
  $('s-token-hint').textContent = secretHint(s.d1ApiToken);
  $('s-akid-hint').textContent = secretHint(s.r2AccessKeyId);
  $('s-secret-hint').textContent = secretHint(s.r2SecretAccessKey);
}

function secretHint(masked) {
  if (!masked || !masked.isSet) return '(not set)';
  return masked.last4 ? `(set · …${masked.last4})` : '(set)';
}

// ---- Small DOM utils -------------------------------------------------------
function td(text) {
  const cell = document.createElement('td');
  cell.textContent = text == null ? '' : String(text);
  return cell;
}

function addKv(container, key, value) {
  const k = document.createElement('div');
  k.className = 'k';
  k.textContent = key;
  const v = document.createElement('div');
  v.textContent = value == null ? '' : String(value);
  container.appendChild(k);
  container.appendChild(v);
}

document.addEventListener('DOMContentLoaded', init);
