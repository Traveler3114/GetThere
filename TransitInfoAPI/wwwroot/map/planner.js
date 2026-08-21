// ════════════════════════════════════════════════════════════════════════════
//  Trip planner
//
//  Turns the browse map into a door-to-door journey planner. Sits on top of public.js:
//  it reuses the shared map (window.gtMap) and the global esc()/showMapError()/getRouteColor()
//  helpers, and mirrors the debounce/abort autocomplete and addSource/addLayer/setData patterns.
//  The page's CSP has no 'unsafe-inline' in script-src, so every handler is wired with
//  addEventListener and nothing is an inline attribute.
// ════════════════════════════════════════════════════════════════════════════
(function () {
  const map = window.gtMap;
  if (!map) return;

  const lang = (document.documentElement.lang || 'en').split('-')[0];
  const S = {
    en: { open: 'Plan a route', title: 'Plan a route', start: 'Start', dest: 'Destination', go: 'Go',
          planning: 'Planning…', unavailable: 'Planner unavailable — is the routing engine running?',
          noRoute: 'No route found.', pickHint: 'Tap the map to set the point.', myLoc: 'My location', walk: 'Walk', bike: 'Bike', live: 'live',
          getTickets: 'Get tickets' },
    hr: { open: 'Planiraj put', title: 'Planiraj put', start: 'Polazište', dest: 'Odredište', go: 'Kreni',
          planning: 'Planiranje…', unavailable: 'Planer nedostupan — radi li usmjeravanje?',
          noRoute: 'Ruta nije pronađena.', pickHint: 'Dodirni kartu za odabir točke.', myLoc: 'Moja lokacija', walk: 'Pješice', bike: 'Bicikl', live: 'uživo',
          getTickets: 'Kupi karte' }
  };
  const t = (k) => (S[lang] && S[lang][k]) || S.en[k] || k;

  // A rented-bike leg comes back as a non-transit leg with mode BICYCLE — distinguish it from walking.
  const isBike = (leg) => !leg.isTransit && /BICYCLE|BIKE/i.test(leg.mode || '');

  // ── Elements ────────────────────────────────────────────────────────────────
  const chrome = document.getElementById('chrome');
  const planner = document.getElementById('planner');
  const btnOpen = document.getElementById('btnDirections');
  const btnClose = document.getElementById('plannerClose');
  const fromInput = document.getElementById('fromInput');
  const toInput = document.getElementById('toInput');
  const fromResults = document.getElementById('fromResults');
  const toResults = document.getElementById('toResults');
  const pickFrom = document.getElementById('pickFrom');
  const pickTo = document.getElementById('pickTo');
  const useLocation = document.getElementById('useLocation');
  const swapEnds = document.getElementById('swapEnds');
  const departAt = document.getElementById('departAt');
  const planBtn = document.getElementById('planBtn');
  const planResults = document.getElementById('planResults');

  // ── State ─────────────────────────────────────────────────────────────────
  const state = { from: null, to: null, pickTarget: null, itineraries: [], selected: 0, preset: 'fastest' };

  // Presets that work in this anonymous, fare-blind planner (routing-only). "cheapest" is omitted
  // here on purpose — it ranks by the GetThereAPI journey total, which only the authenticated app can
  // fetch; the map WebView never talks to GetThereAPI. What the page *does* offer instead is a
  // "Get tickets" button that hands the selected itinerary to the app through the gtapp://journey
  // scheme, and the app prices it natively — routing stays fare-blind here, pricing stays in the app.
  const PRESETS = [
    { key: 'fastest', en: 'Fastest', hr: 'Najbrže' },
    { key: 'balanced', en: 'Balanced', hr: 'Uravnoteženo' },
    { key: 'greener', en: 'Greener', hr: 'Zelenije' },
    { key: 'fewest-transfers', en: 'Fewest transfers', hr: 'Manje presjedanja' },
    { key: 'no-trains', en: 'No trains', hr: 'Bez vlakova' },
  ];

  // Static labels (kept out of the HTML so there is one place to add a language, as public.js does).
  btnOpen.textContent = t('open');
  document.getElementById('plannerTitle').textContent = t('title');
  fromInput.placeholder = t('start');
  toInput.placeholder = t('dest');
  planBtn.textContent = t('go');

  // ── Encoded-polyline decoder (Google precision-5, what OTP returns) ──────────
  function decodePolyline(encoded) {
    let index = 0, lat = 0, lng = 0;
    const coords = [], factor = 1e5;
    while (index < encoded.length) {
      let result = 0, shift = 0, byte;
      do { byte = encoded.charCodeAt(index++) - 63; result |= (byte & 0x1f) << shift; shift += 5; } while (byte >= 0x20);
      lat += (result & 1) ? ~(result >> 1) : (result >> 1);
      result = 0; shift = 0;
      do { byte = encoded.charCodeAt(index++) - 63; result |= (byte & 0x1f) << shift; shift += 5; } while (byte >= 0x20);
      lng += (result & 1) ? ~(result >> 1) : (result >> 1);
      coords.push([lng / factor, lat / factor]); // [lon, lat] for GeoJSON
    }
    return coords;
  }

  // ── Map layers for the drawn itinerary ──────────────────────────────────────
  const emptyFC = () => ({ type: 'FeatureCollection', features: [] });

  function ensureLayers() {
    if (map.getSource('plan-legs')) return;
    map.addSource('plan-legs', { type: 'geojson', data: emptyFC() });
    map.addSource('plan-points', { type: 'geojson', data: emptyFC() });
    // Walk legs: dashed grey. Transit legs: solid, coloured per leg.
    map.addLayer({
      id: 'plan-walk', type: 'line', source: 'plan-legs', filter: ['==', ['get', 'walk'], true],
      layout: { 'line-cap': 'round' },
      paint: { 'line-color': '#64748b', 'line-width': 4, 'line-dasharray': [1, 1.5], 'line-opacity': 0.85 }
    });
    map.addLayer({
      id: 'plan-transit', type: 'line', source: 'plan-legs', filter: ['==', ['get', 'walk'], false],
      layout: { 'line-cap': 'round', 'line-join': 'round' },
      paint: { 'line-color': ['get', 'color'], 'line-width': 6, 'line-opacity': 0.95 }
    });
    map.addLayer({
      id: 'plan-points-circle', type: 'circle', source: 'plan-points',
      paint: {
        'circle-radius': ['get', 'r'], 'circle-color': ['get', 'color'],
        'circle-stroke-width': 2, 'circle-stroke-color': '#fff'
      }
    });
  }
  // isStyleLoaded() can read false even once the map is usable, and 'load' may have already fired
  // before this sibling script attached — so fall back to 'idle', which fires reliably on the next
  // render settle. ensureLayers is idempotent regardless (drawItinerary also calls it lazily).
  if (map.isStyleLoaded()) ensureLayers(); else map.once('idle', ensureLayers);

  function legColor(leg) {
    if (isBike(leg)) return '#0EA5E9';
    if (!leg.isTransit) return '#64748b';
    const modeToType = {
      TRAM: 'Tram', SUBWAY: 'Subway', METRO: 'Subway', RAIL: 'Train', TRAIN: 'Train', BUS: 'Bus',
      FERRY: 'Ferry', FUNICULAR: 'Funicular', GONDOLA: 'CableCar', CABLE_CAR: 'CableTram',
      TROLLEYBUS: 'Trolleybus', MONORAIL: 'Monorail', BICYCLE: 'Bicycle'
    };
    const type = modeToType[(leg.mode || '').toUpperCase()] || 'Bus';
    try { return (typeof getRouteColor === 'function') ? getRouteColor(type) : '#126400'; }
    catch (_) { return '#126400'; }
  }

  function legCoords(leg) {
    if (leg.geometry) { try { return decodePolyline(leg.geometry); } catch (_) { /* fall through */ } }
    return [[leg.from.lon, leg.from.lat], [leg.to.lon, leg.to.lat]];
  }

  function drawItinerary(itin) {
    ensureLayers();
    const legFeatures = [];
    const pointFeatures = [];
    const bounds = new maplibregl.LngLatBounds();

    itin.legs.forEach((leg, i) => {
      const coords = legCoords(leg);
      coords.forEach(c => bounds.extend(c));
      legFeatures.push({
        type: 'Feature',
        geometry: { type: 'LineString', coordinates: coords },
        properties: { walk: !leg.isTransit && !isBike(leg), color: legColor(leg) }
      });
      // Board/alight dots for transit legs.
      if (leg.isTransit) {
        pointFeatures.push(pt(leg.from, '#0F172A', 5));
        pointFeatures.push(pt(leg.to, '#0F172A', 5));
      }
    });

    // Origin (green) and destination (red) on top.
    const first = itin.legs[0], last = itin.legs[itin.legs.length - 1];
    pointFeatures.push(pt(first.from, '#10B981', 7));
    pointFeatures.push(pt(last.to, '#EF4444', 7));

    map.getSource('plan-legs').setData({ type: 'FeatureCollection', features: legFeatures });
    map.getSource('plan-points').setData({ type: 'FeatureCollection', features: pointFeatures });
    if (!bounds.isEmpty()) map.fitBounds(bounds, { padding: { top: 80, bottom: 60, left: 440, right: 60 }, maxZoom: 16, duration: 600 });
  }

  function pt(place, color, r) {
    return { type: 'Feature', geometry: { type: 'Point', coordinates: [place.lon, place.lat] }, properties: { color, r } };
  }

  function clearMap() {
    if (map.getSource('plan-legs')) map.getSource('plan-legs').setData(emptyFC());
    if (map.getSource('plan-points')) map.getSource('plan-points').setData(emptyFC());
  }

  // ── Autocomplete: stations + addresses, merged ──────────────────────────────
  async function searchPlaces(query, signal) {
    const [stops, geo] = await Promise.allSettled([
      fetch(`/stations/search?q=${encodeURIComponent(query)}&perPage=6`, { signal }).then(r => r.ok ? r.json() : null),
      fetch(`/geocode?q=${encodeURIComponent(query)}`, { signal }).then(r => r.ok ? r.json() : null)
    ]);
    const items = [];
    if (stops.status === 'fulfilled' && stops.value && Array.isArray(stops.value.data)) {
      for (const s of stops.value.data) {
        items.push({ kind: 'stop', label: s.name || '', sub: [s.cityName, s.countryName].filter(Boolean).join(', '), lat: s.latitude, lon: s.longitude });
      }
    }
    // /geocode returns 503 until the Azure Maps key is set — r.ok is false, value is null, so we
    // simply fall back to stops-only without surfacing an error.
    if (geo.status === 'fulfilled' && Array.isArray(geo.value)) {
      for (const g of geo.value) items.push({ kind: 'addr', label: g.label || '', sub: '', lat: g.lat, lon: g.lon });
    }
    return items;
  }

  function hideList(listEl, input) {
    listEl.hidden = true;
    listEl.replaceChildren();
    input.setAttribute('aria-expanded', 'false');
  }

  function renderList(listEl, input, items, onPick) {
    if (items.length === 0) {
      const li = document.createElement('li');
      li.className = 'empty';
      li.textContent = t('noRoute') === 'No route found.' ? 'No matches' : 'Nema rezultata';
      listEl.replaceChildren(li);
    } else {
      listEl.replaceChildren(...items.map(it => {
        const li = document.createElement('li');
        li.setAttribute('role', 'option');
        li.textContent = it.label;
        const tag = document.createElement('span');
        tag.className = 'tag';
        tag.textContent = it.kind === 'stop' ? '◉' : '⌖';
        li.appendChild(tag);
        if (it.sub) {
          const sub = document.createElement('span');
          sub.className = 'sub';
          sub.textContent = it.sub;
          li.appendChild(sub);
        }
        li.addEventListener('click', () => { onPick(it); input.value = it.label; hideList(listEl, input); });
        return li;
      }));
    }
    listEl.hidden = false;
    input.setAttribute('aria-expanded', 'true');
  }

  function wireField(input, listEl, which) {
    let timer = null, abort = null;
    input.addEventListener('input', () => {
      clearTimeout(timer);
      const q = input.value.trim();
      if (q.length < 2) { if (abort) abort.abort(); hideList(listEl, input); return; }
      timer = setTimeout(() => {
        if (abort) abort.abort();
        abort = new AbortController();
        searchPlaces(q, abort.signal)
          .then(items => renderList(listEl, input, items, it => setPoint(which, { lat: it.lat, lon: it.lon, label: it.label })))
          .catch(err => { if (err.name !== 'AbortError') hideList(listEl, input); });
      }, 250);
    });
    input.addEventListener('keydown', e => { if (e.key === 'Escape') { hideList(listEl, input); input.blur(); } });
  }

  function setPoint(which, point) {
    state[which] = point;
    planBtn.disabled = !(state.from && state.to);
  }

  // ── Map-pick ────────────────────────────────────────────────────────────────
  function startPick(which) {
    state.pickTarget = which;
    pickFrom.classList.toggle('active', which === 'from');
    pickTo.classList.toggle('active', which === 'to');
    map.getCanvas().style.cursor = 'crosshair';
    showMapError(t('pickHint'));
  }
  function clearPick() {
    state.pickTarget = null;
    pickFrom.classList.remove('active');
    pickTo.classList.remove('active');
    map.getCanvas().style.cursor = '';
  }
  pickFrom.addEventListener('click', () => (state.pickTarget === 'from' ? clearPick() : startPick('from')));
  pickTo.addEventListener('click', () => (state.pickTarget === 'to' ? clearPick() : startPick('to')));

  // A general click handler; it only acts while a pick is armed, so it never interferes with the
  // station/route layer clicks public.js registers.
  map.on('click', (e) => {
    if (!state.pickTarget) return;
    const lat = +e.lngLat.lat.toFixed(6), lon = +e.lngLat.lng.toFixed(6);
    const label = `${lat}, ${lon}`;
    (state.pickTarget === 'from' ? fromInput : toInput).value = label;
    setPoint(state.pickTarget, { lat, lon, label });
    clearPick();
  });

  // ── Geolocation ──────────────────────────────────────────────────────────────
  useLocation.addEventListener('click', () => {
    if (!navigator.geolocation) { showMapError('Geolocation unavailable'); return; }
    navigator.geolocation.getCurrentPosition(
      pos => {
        const lat = +pos.coords.latitude.toFixed(6), lon = +pos.coords.longitude.toFixed(6);
        fromInput.value = t('myLoc');
        setPoint('from', { lat, lon, label: t('myLoc') });
        map.easeTo({ center: [lon, lat], zoom: 14 });
      },
      () => showMapError('Could not get your location'),
      { enableHighAccuracy: true, timeout: 8000, maximumAge: 60000 }
    );
  });

  // ── Swap ─────────────────────────────────────────────────────────────────────
  swapEnds.addEventListener('click', () => {
    [state.from, state.to] = [state.to, state.from];
    [fromInput.value, toInput.value] = [toInput.value, fromInput.value];
    planBtn.disabled = !(state.from && state.to);
    if (state.itineraries.length) submitPlan();
  });

  // ── Plan ─────────────────────────────────────────────────────────────────────
  // A datetime-local value carries no offset; append the browser's so DateTimeOffset on the server
  // keeps the wall-clock time the user picked rather than shifting it.
  function departIso() {
    const v = departAt.value;
    if (!v) return null;
    const d = new Date(v);
    if (isNaN(d.getTime())) return null;
    const off = -d.getTimezoneOffset();
    const sign = off >= 0 ? '+' : '-', abs = Math.abs(off);
    const hh = String(Math.floor(abs / 60)).padStart(2, '0'), mm = String(abs % 60).padStart(2, '0');
    return `${v}:00${sign}${hh}:${mm}`;
  }

  let planAbort = null;
  function submitPlan() {
    if (!(state.from && state.to)) return;
    if (planAbort) planAbort.abort();
    planAbort = new AbortController();

    planResults.innerHTML = `<div class="plan-msg">${t('planning')}</div>`;
    const body = {
      fromLat: state.from.lat, fromLon: state.from.lon,
      toLat: state.to.lat, toLon: state.to.lon,
      numItineraries: 3,
      preset: state.preset
    };
    const iso = departIso();
    if (iso) body.departAt = iso;

    fetch('/plan', {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body), signal: planAbort.signal
    })
      .then(r => { if (!r.ok) throw new Error('plan ' + r.status); return r.json(); })
      .then(itins => renderItineraries(itins || []))
      .catch(err => {
        if (err.name === 'AbortError') return;
        clearMap();
        planResults.innerHTML = `<div class="plan-msg">${t('unavailable')}</div>`;
      });
  }
  planBtn.addEventListener('click', submitPlan);

  function fmtTime(iso) {
    return iso ? new Date(iso).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }) : '';
  }
  function fmtDur(sec) { return Math.max(1, Math.round(sec / 60)) + ' min'; }

  function renderItineraries(itins) {
    state.itineraries = itins;
    state.selected = 0;
    if (itins.length === 0) { clearMap(); planResults.innerHTML = `<div class="plan-msg">${t('noRoute')}</div>`; return; }

    planResults.replaceChildren(...itins.map((itin, idx) => {
      const card = document.createElement('div');
      card.className = 'itin' + (idx === 0 ? ' selected' : '');

      const head = document.createElement('div');
      head.className = 'itin-head';
      head.innerHTML = `<span class="itin-dur">${fmtDur(itin.durationSeconds)}</span>` +
        `<span class="itin-time">${esc(fmtTime(itin.startTime))} – ${esc(fmtTime(itin.endTime))}</span>`;
      card.appendChild(head);

      const modes = document.createElement('div');
      modes.className = 'itin-modes';
      itin.legs.forEach(leg => {
        const b = document.createElement('span');
        if (isBike(leg)) { b.className = 'leg-badge'; b.style.background = legColor(leg); b.textContent = '🚲'; }
        else if (!leg.isTransit) { b.className = 'leg-badge walk'; b.textContent = '🚶'; }
        else {
          b.className = 'leg-badge';
          b.style.background = legColor(leg);
          b.textContent = leg.routeShortName || leg.mode || '?';
        }
        modes.appendChild(b);
      });
      card.appendChild(modes);

      const legs = document.createElement('div');
      legs.className = 'itin-legs';
      legs.hidden = idx !== 0;
      itin.legs.forEach(leg => legs.appendChild(legLine(leg)));
      card.appendChild(legs);

      card.addEventListener('click', () => selectItinerary(idx));
      return card;
    }));

    // One-way handoff to the app: the button below the list acts on the selected itinerary and
    // carries it out through a custom scheme — the page itself never calls GetThereAPI.
    const getTickets = document.createElement('button');
    getTickets.type = 'button';
    getTickets.className = 'get-tickets';
    getTickets.textContent = t('getTickets');
    getTickets.addEventListener('click', handOffToNative);
    planResults.appendChild(getTickets);

    drawItinerary(itins[0]);
  }

  // Transfers the selected itinerary to the MAUI app through the gtapp://journey scheme. Only the
  // fields the quote API needs survive the trip — coordinates and times, no geometry, no names —
  // which also keeps the URL short.
  function handOffToNative() {
    const itin = state.itineraries[state.selected];
    if (!itin || !itin.legs || itin.legs.length === 0) return;

    const compactLegs = itin.legs.map(leg => ({
      operatorGlobalId: leg.operatorGlobalId || null,
      mode: leg.mode || '',
      isTransit: !!leg.isTransit,
      startTime: leg.startTime,
      endTime: leg.endTime,
      fromLat: leg.from ? leg.from.lat : null,
      fromLon: leg.from ? leg.from.lon : null,
      toLat: leg.to ? leg.to.lat : null,
      toLon: leg.to ? leg.to.lon : null
    }));

    window.location.href = 'gtapp://journey?legs=' + encodeURIComponent(JSON.stringify(compactLegs));
  }

  function legLine(leg) {
    const row = document.createElement('div');
    row.className = 'leg-line';
    if (!leg.isTransit) {
      const icon = isBike(leg) ? '🚲' : '🚶';
      const label = isBike(leg) ? t('bike') : t('walk');
      row.innerHTML = `${icon} ${esc(label)} <span class="lt">${fmtDur((new Date(leg.endTime) - new Date(leg.startTime)) / 1000)}</span>`;
      return row;
    }
    const name = leg.routeShortName || leg.mode || '';
    const rt = leg.realtimeState ? `<span class="rt-badge">${esc(t('live'))}</span>` : '';
    row.innerHTML = `<strong>${esc(name)}</strong> ${esc(leg.from.name || '')} → ${esc(leg.to.name || '')} ` +
      `<span class="lt">${esc(fmtTime(leg.startTime))}–${esc(fmtTime(leg.endTime))}</span>${rt}`;
    return row;
  }

  function selectItinerary(idx) {
    state.selected = idx;
    [...planResults.children].forEach((c, i) => {
      c.classList.toggle('selected', i === idx);
      const legs = c.querySelector('.itin-legs');
      if (legs) legs.hidden = i !== idx;
    });
    drawItinerary(state.itineraries[idx]);
  }

  // ── Open / close ──────────────────────────────────────────────────────────────
  function openPlanner() {
    chrome.style.display = 'none';
    planner.hidden = false;
    if (!departAt.value) {
      // Prefill "now" (local) so a plan always carries a time — the server's no-time default is UTC.
      const n = new Date(Date.now() - new Date().getTimezoneOffset() * 60000);
      departAt.value = n.toISOString().slice(0, 16);
    }
    fromInput.focus();
  }
  function closePlanner() {
    planner.hidden = true;
    chrome.style.display = '';
    clearPick();
    clearMap();
  }
  btnOpen.addEventListener('click', openPlanner);
  btnClose.addEventListener('click', closePlanner);

  // Close either autocomplete list when tapping outside the planner.
  document.addEventListener('click', e => {
    if (e.target.closest('#planner')) return;
    if (!fromResults.hidden) hideList(fromResults, fromInput);
    if (!toResults.hidden) hideList(toResults, toInput);
  });

  wireField(fromInput, fromResults, 'from');
  wireField(toInput, toResults, 'to');

  // ── Preset filter chips ───────────────────────────────────────────────────
  const presetHost = document.getElementById('planPresets');
  function renderPresets() {
    presetHost.replaceChildren(...PRESETS.map(p => {
      const btn = document.createElement('button');
      btn.type = 'button';
      btn.className = 'preset' + (state.preset === p.key ? ' selected' : '');
      btn.dataset.preset = p.key;
      btn.textContent = p[lang] || p.en;
      btn.setAttribute('aria-pressed', state.preset === p.key ? 'true' : 'false');
      return btn;
    }));
  }
  presetHost.addEventListener('click', e => {
    const chip = e.target.closest('.preset');
    if (!chip) return;
    state.preset = chip.dataset.preset;
    renderPresets();
    if (state.from && state.to) submitPlan(); // re-plan with the new filter
  });
  renderPresets();

  planBtn.disabled = true;
})();
