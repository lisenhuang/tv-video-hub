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
  dbsetup: 'db-setup-view',
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
  wireDbSetup();
  wireSetup();
  wireLogin();
  wireTabs();
  wireVideoForm();
  wireSettings();

  await refreshState();
}

let lastSetupState = null;

async function refreshState() {
  banner('');
  try {
    const { res, data } = await apiJson('/api/admin/setup-state', 'GET');
    if (!res.ok) throw new Error('Could not reach the server.');
    lastSetupState = data;
    // Strict wizard order: database → admin → (login) → dashboard.
    if (data.needsDatabase) {
      setAuthedChrome(false);
      await loadDbSetup();
      showView('dbsetup');
    } else if (data.needsAdmin || data.needsSetup) {
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

// ---- Step 1: Database setup (bootstrap) ------------------------------------
const DB_CONN_HELP = {
  sqlite: 'e.g. Data Source=App_Data/mediahub.db',
  postgres: 'e.g. Host=localhost;Database=mediahub;Username=postgres;Password=…',
  mysql: 'e.g. Server=localhost;Database=mediahub;User=root;Password=… (provider not bundled — see README)',
  sqlserver: 'e.g. Server=localhost;Database=mediahub;User Id=sa;Password=…;TrustServerCertificate=true',
};

function applyDbSetupProvider() {
  const p = $('db-provider').value;
  show($('db-d1'), p === 'd1');
  show($('db-sql'), p === 'sqlite' || p === 'postgres' || p === 'mysql' || p === 'sqlserver');
  $('db-conn-help').textContent = DB_CONN_HELP[p] || '';
}

async function loadDbSetup() {
  const { res, data } = await apiJson('/api/admin/db-config', 'GET');
  if (res.ok && data) {
    $('db-provider').value = data.databaseProvider || '';
    $('db-account').value = data.accountId || '';
    $('db-dbid').value = data.d1DatabaseId || '';
    $('db-token-hint').textContent = secretHint(data.d1ApiToken);
    $('db-conn-hint').textContent = secretHint(data.databaseConnectionString);
  }
  applyDbSetupProvider();
}

function wireDbSetup() {
  $('db-provider').addEventListener('change', applyDbSetupProvider);
  $('db-setup-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    const provider = $('db-provider').value;
    if (!provider) { banner('Choose a database provider.', 'error'); return; }
    const body = {
      databaseProvider: provider,
      accountId: $('db-account').value,
      d1DatabaseId: $('db-dbid').value,
      d1ApiToken: $('db-token').value,
      databaseConnectionString: $('db-conn').value,
    };
    const { res, data } = await apiJson('/api/admin/db-config', 'PUT', body);
    const out = $('db-setup-result');
    out.hidden = false;
    if (!res.ok) {
      out.innerHTML = '';
      out.appendChild(testLine('Database', { ok: false, message: (data && data.error) || 'save failed' }));
      return;
    }
    if (data.connects) {
      banner('');
      // DB connects — advance to the next required step.
      await refreshState();
    } else {
      out.innerHTML = '';
      out.appendChild(testLine('Database', { ok: false, message: 'Saved, but could not connect. Check the values.' }));
    }
  });
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

  // Refresh setup-state so we can guide first-run configuration.
  const { res, data } = await apiJson('/api/admin/setup-state', 'GET');
  if (res.ok) lastSetupState = data;

  await loadSettings();

  const needsDb = lastSetupState && lastSetupState.needsDatabase;
  const needsStorage = lastSetupState && lastSetupState.needsStorage;
  if (needsDb || needsStorage) {
    selectTab('settings');
    const what = [needsDb ? 'a database' : null, needsStorage ? 'object storage' : null]
      .filter(Boolean).join(' and ');
    banner(`Next: configure ${what} below, then save.`, 'success');
  } else {
    selectTab('videos');
    await loadVideos();
  }
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
  ['videos', 'settings'].forEach((n) => {
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
  if (res.status === 503) {
    // Database not configured yet — guide the user to settings.
    show($('videos-empty'), false);
    banner((data && data.error) || 'Configure the database in Settings first.', 'error');
    return;
  }
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

// ---- Settings --------------------------------------------------------------
const CONN_HELP = {
  sqlite: 'e.g. Data Source=App_Data/mediahub.db',
  postgres: 'e.g. Host=localhost;Database=mediahub;Username=postgres;Password=…',
  mysql: 'e.g. Server=localhost;Database=mediahub;User=root;Password=… (provider not bundled — see README)',
  sqlserver: 'e.g. Server=localhost;Database=mediahub;User Id=sa;Password=…;TrustServerCertificate=true',
};

function applyDbProvider() {
  const p = $('s-dbprovider').value;
  show($('s-db-d1'), p === 'd1');
  show($('s-db-sql'), p === 'sqlite' || p === 'postgres' || p === 'mysql' || p === 'sqlserver');
  $('s-conn-help').textContent = CONN_HELP[p] || '';
}

function applyStorageProvider() {
  const local = $('s-provider').value === 'local';
  show($('s-local-fields'), local);
  show($('s-s3-fields'), !local);
}

function wireSettings() {
  $('s-dbprovider').addEventListener('change', applyDbProvider);
  $('s-provider').addEventListener('change', applyStorageProvider);

  // One-click Cloudflare R2 defaults: region=auto + the three toggles R2 requires.
  // Only fills the R2-specific knobs; the endpoint, keys and buckets are still yours.
  const r2Preset = $('s-r2-preset');
  if (r2Preset) r2Preset.addEventListener('click', () => {
    $('s-provider').value = 's3';
    applyStorageProvider();
    if (!$('s-region').value.trim()) $('s-region').value = 'auto';
    $('s-forcepath').checked = true;
    $('s-disablesign').checked = true;
    $('s-checksum').checked = true;
    banner('R2 defaults applied. Now fill in Service URL, Access keys & buckets, then Save settings.', 'success');
  });

  $('settings-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    const provider = $('s-provider').value === 'local' ? 'local' : 's3';
    const local = provider === 'local';
    // Video bucket + TTL are shared config keys; read from whichever provider's inputs are shown.
    // (APKs ship bundled in the backend image, so there is no APK bucket / release key here.)
    const videoBucket = local ? $('s-vbucket-local').value : $('s-vbucket').value;
    const ttlRaw = local ? $('s-ttl-local').value : $('s-ttl').value;
    const body = {
      // Database (pluggable)
      databaseProvider: $('s-dbprovider').value,
      accountId: $('s-account').value,
      d1DatabaseId: $('s-dbid').value,
      d1ApiToken: $('s-token').value, // blank => unchanged
      databaseConnectionString: $('s-conn').value, // blank => unchanged
      // Object storage
      storageProvider: provider,
      storageLocalBasePath: $('s-localpath').value,
      storageServiceUrl: $('s-serviceurl').value,
      storageRegion: $('s-region').value,
      storageAccessKeyId: $('s-akid').value, // blank => unchanged
      storageSecretAccessKey: $('s-secret').value, // blank => unchanged
      storageVideoBucket: videoBucket,
      storageForcePathStyle: $('s-forcepath').checked,
      storagePresignTtlMinutes: ttlRaw ? Number(ttlRaw) : null,
      storageDisablePayloadSigning: $('s-disablesign').checked,
      storageUseChecksumWhenRequired: $('s-checksum').checked,
    };
    const { res, data } = await apiJson('/api/admin/settings', 'PUT', body);
    if (res.ok) {
      banner('Settings saved.', 'success');
      fillSettings(data);
      // Reflect any newly-satisfied setup requirements.
      const st = await apiJson('/api/admin/setup-state', 'GET');
      if (st.res.ok) lastSetupState = st.data;
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
      out.appendChild(testLine('Database', data.database));
      out.appendChild(testLine('Object storage', data.storage));
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
  // Database (pluggable)
  $('s-dbprovider').value = s.databaseProvider || '';
  $('s-account').value = s.accountId || '';
  $('s-dbid').value = s.d1DatabaseId || '';
  applyDbProvider();
  $('s-db-status').textContent = s.databaseConfigured ? '✓ configured' : '(not configured)';
  // Object storage — provider selector + per-provider fields.
  $('s-provider').value = s.storageProvider === 'local' ? 'local' : 's3';
  applyStorageProvider();
  $('s-localpath').value = s.storageLocalBasePath || '';
  // Video bucket + TTL are shared keys; mirror into both the s3 and local inputs.
  $('s-vbucket').value = s.storageVideoBucket || '';
  $('s-ttl').value = s.storagePresignTtlMinutes || '';
  $('s-vbucket-local').value = s.storageVideoBucket || '';
  $('s-ttl-local').value = s.storagePresignTtlMinutes || '';
  // S3-specific fields.
  $('s-serviceurl').value = s.storageServiceUrl || '';
  $('s-region').value = s.storageRegion || '';
  $('s-forcepath').checked = !!s.storageForcePathStyle;
  $('s-disablesign').checked = !!s.storageDisablePayloadSigning;
  $('s-checksum').checked = !!s.storageUseChecksumWhenRequired;
  // Secrets: never populated; show a hint about the stored value, keep field blank.
  $('s-token').value = '';
  $('s-conn').value = '';
  $('s-akid').value = '';
  $('s-secret').value = '';
  $('s-token-hint').textContent = secretHint(s.d1ApiToken);
  $('s-conn-hint').textContent = secretHint(s.databaseConnectionString);
  $('s-akid-hint').textContent = secretHint(s.storageAccessKeyId);
  $('s-secret-hint').textContent = secretHint(s.storageSecretAccessKey);
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
