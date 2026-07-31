let mapErrorTimer = null;
function showMapError(msg) {
  const el = document.getElementById('mapError');
  if (!el) return;
  el.textContent = msg;
  el.classList.remove('d-none');
  if (mapErrorTimer) clearTimeout(mapErrorTimer);
  mapErrorTimer = setTimeout(() => el.classList.add('d-none'), 8000);
}

const map = new maplibregl.Map({
  container: 'map',
  style: '/map/style.json',
  center: [16.0, 45.8],
  zoom: 7
});

map.addControl(new maplibregl.NavigationControl());
map.addControl(new maplibregl.ScaleControl({ unit: 'metric' }));

let debounceTimer = null;
let vehiclesInterval = null;
let sidebarOpen = false;
const stationsSource = 'gt-stops';
const routesSource = 'routes';
const vehiclesSource = 'vehicles';
const mobilitySource = 'mobility-stations';

function loadMapData() {
  const bounds = map.getBounds();
  const center = map.getCenter();
  const sw = bounds.getSouthWest();
  const ne = bounds.getNorthEast();
  const dlat = (ne.lat - sw.lat) / 2;
  const dlon = (ne.lng - sw.lng) / 2;
  const radiusKm = Math.max(
    dlat * 111, dlon * 111 * Math.cos(center.lat * Math.PI / 180)
  ) * 1.5;

  const stationUrl = `/stations?format=geojson&lat=${center.lat}&lon=${center.lng}&radiusKm=${radiusKm}`;
  const routeUrl = `/routes?format=geojson&minLat=${sw.lat}&minLon=${sw.lng}&maxLat=${ne.lat}&maxLon=${ne.lng}`;
  const mobilityUrl = `/mobility/stations?format=geojson&lat=${center.lat}&lon=${center.lng}&radiusKm=${radiusKm}`;

  fetch(stationUrl).then(r => r.json()).then(data => {
    const src = map.getSource(stationsSource);
    if (src && data?.type === 'FeatureCollection') {
      src.setData(data);
    }
  }).catch(() => showMapError('Failed to load stations'));

  fetch(routeUrl).then(r => r.json()).then(data => {
    const src = map.getSource(routesSource);
    if (src && data?.type === 'FeatureCollection') {
      src.setData(data);
    }
  }).catch(() => showMapError('Failed to load routes'));

  fetch(mobilityUrl).then(r => r.json()).then(data => {
    const src = map.getSource(mobilitySource);
    if (src && data?.type === 'FeatureCollection') {
      src.setData(data);
    }
  }).catch(() => showMapError('Failed to load mobility stations'));
}

function loadVehicles() {
  fetch('/realtime/vehicles').then(r => r.json()).then(resp => {
    const vehicles = resp || [];
    const fc = {
      type: 'FeatureCollection',
      features: vehicles.map(v => ({
        type: 'Feature',
        geometry: { type: 'Point', coordinates: [v.longitude, v.latitude] },
        properties: {
          vehicleId: v.vehicleId, routeId: v.routeId, tripId: v.tripId,
          bearing: v.bearing, lastUpdated: v.lastUpdated
        }
      }))
    };
    const src = map.getSource(vehiclesSource);
    if (src) { src.setData(fc); }
  }).catch(() => showMapError('Failed to load vehicles'));
}

map.on('load', () => {
  map.addSource(stationsSource, {
    type: 'geojson',
    data: { type: 'FeatureCollection', features: [] },
    cluster: true,
    clusterMaxZoom: 13,
    clusterRadius: 50
  });
  map.addSource(routesSource, { type: 'geojson', data: { type: 'FeatureCollection', features: [] } });
  map.addSource(vehiclesSource, { type: 'geojson', data: { type: 'FeatureCollection', features: [] } });
  map.addSource(mobilitySource, {
    type: 'geojson',
    data: { type: 'FeatureCollection', features: [] },
    cluster: true,
    clusterMaxZoom: 14,
    clusterRadius: 40
  });

  // Route lines colored by type
  map.addLayer({
    id: 'routes-line',
    type: 'line',
    source: routesSource,
    paint: {
      'line-color': [
        'match', ['get', 'routeType'],
        'Tram', '#1f78b4',
        'Subway', '#e31a1c',
        'Train', '#b15928',
        'Bus', '#126400',
        'Ferry', '#6a3d9a',
        'CableTram', '#fb9a99',
        'CableCar', '#fb9a99',
        'Funicular', '#fdbf6f',
        'Trolleybus', '#33a02c',
        'Monorail', '#cab2d6',
        'Bicycle', '#a6cee3',
        'Scooter', '#ff7f00',
        'Airplane', '#b2df8a',
        '#888'
      ],
      'line-width': 2,
      'line-opacity': 0.7
    }
  });

  // Cluster circles
  map.addLayer({
    id: 'stations-cluster',
    type: 'circle',
    source: stationsSource,
    filter: ['has', 'point_count'],
    paint: {
      'circle-color': '#1f78b4',
      'circle-radius': [
        'step', ['get', 'point_count'],
        15, 10,
        20, 50,
        25
      ],
      'circle-opacity': 0.85,
      'circle-stroke-width': 2,
      'circle-stroke-color': '#fff'
    }
  });

  // Cluster count labels
  map.addLayer({
    id: 'stations-cluster-count',
    type: 'symbol',
    source: stationsSource,
    filter: ['has', 'point_count'],
    layout: {
      'text-field': '{point_count_abbreviated}',
      'text-font': ['Open Sans Regular'],
      'text-size': 12
    },
    paint: { 'text-color': '#fff' }
  });

  // Load stop icons
  const STOP_ICONS = [
    { id: 'stop-bus', file: '/images/bus.png', types: ['Bus', 'Trolleybus'] },
    { id: 'stop-tram', file: '/images/tram.png', types: ['Tram'] },
    { id: 'stop-train', file: '/images/train.png', types: ['Subway', 'Train', 'Ferry', 'CableTram', 'CableCar', 'Funicular', 'Monorail'] },
    { id: 'stop-default', file: '/images/bus.png', types: [] }
  ];

  const PRELOADED = {};
  for (const icon of STOP_ICONS) {
    const img = new Image(); img.crossOrigin = 'anonymous';
    img.src = window.location.origin + icon.file;
    PRELOADED[icon.id] = new Promise((resolve) => { img.onload = () => { const c = document.createElement('canvas'); c.width = img.width; c.height = img.height; c.getContext('2d').drawImage(img, 0, 0); resolve(c.getContext('2d').getImageData(0, 0, img.width, img.height)); }; });
  }

  map.on('styleimagemissing', async (e) => {
    if (!PRELOADED[e.id]) return;
    const data = await PRELOADED[e.id];
    if (!map.hasImage(e.id)) map.addImage(e.id, data);
  });

  const _stationIconExpr = ['case',
    ['in', ['get', 'primaryRouteType'], ['literal', ['Bus', 'Trolleybus']]], 'stop-bus',
    ['in', ['get', 'primaryRouteType'], ['literal', ['Tram']]], 'stop-tram',
    ['in', ['get', 'primaryRouteType'], ['literal', ['Subway', 'Train', 'Ferry', 'CableTram', 'CableCar', 'Funicular', 'Monorail']]], 'stop-train',
    'stop-bus'
  ];

  // Individual stop icons (unclustered)
  map.addLayer({
    id: 'stations-circle',
    type: 'symbol',
    source: stationsSource,
    filter: ['!', ['has', 'point_count']],
    layout: {
      'icon-image': _stationIconExpr,
      'icon-size': 0.35,
      'icon-anchor': 'bottom',
      'icon-allow-overlap': true,
      'icon-ignore-placement': true
    }
  });

  // Vehicle dots
  map.addLayer({
    id: 'vehicles-circle',
    type: 'circle',
    source: vehiclesSource,
    paint: {
      'circle-radius': 6,
      'circle-color': '#7b2d8e',
      'circle-stroke-width': 2,
      'circle-stroke-color': '#fff',
      'circle-opacity': 0.9
    }
  });

  // Vehicle bearing arrows
  const arrowSvg = 'data:image/svg+xml;utf8,<svg xmlns="http://www.w3.org/2000/svg" width="16" height="16"><polygon points="8,2 14,13 2,13" fill="%23fff" stroke="%237b2d8e" stroke-width="1.5"/></svg>';
  const arrowImg = new Image(); arrowImg.src = arrowSvg;
  arrowImg.onload = () => { if (!map.hasImage('vehicle-arrow')) map.addImage('vehicle-arrow', arrowImg); };

  map.addLayer({
    id: 'vehicles-arrow',
    type: 'symbol',
    source: vehiclesSource,
    filter: ['!=', ['get', 'bearing'], null],
    layout: {
      'icon-image': 'vehicle-arrow',
      'icon-rotate': ['get', 'bearing'],
      'icon-rotation-alignment': 'map',
      'icon-size': 0.8,
      'icon-allow-overlap': true,
      'icon-ignore-placement': true
    }
  });

  // Mobility cluster circles
  map.addLayer({
    id: 'mobility-cluster',
    type: 'circle',
    source: mobilitySource,
    filter: ['has', 'point_count'],
    paint: {
      'circle-color': '#ff7f00',
      'circle-radius': ['step', ['get', 'point_count'], 12, 10, 16, 50, 20],
      'circle-opacity': 0.85,
      'circle-stroke-width': 2,
      'circle-stroke-color': '#fff'
    }
  });

  // Mobility cluster count
  map.addLayer({
    id: 'mobility-cluster-count',
    type: 'symbol',
    source: mobilitySource,
    filter: ['has', 'point_count'],
    layout: {
      'text-field': '{point_count_abbreviated}',
      'text-font': ['Open Sans Regular'],
      'text-size': 11
    },
    paint: { 'text-color': '#fff' }
  });

  // Individual mobility station dots
  map.addLayer({
    id: 'mobility-dot',
    type: 'circle',
    source: mobilitySource,
    filter: ['!', ['has', 'point_count']],
    paint: {
      'circle-radius': 5,
      'circle-color': '#ff7f00',
      'circle-stroke-width': 2,
      'circle-stroke-color': '#fff',
      'circle-opacity': 0.8
    }
  });

  loadMapData();
  loadVehicles();
  vehiclesInterval = setInterval(loadVehicles, 30000);

  // The chips render before the style is ready, so their initial selection has nothing to act on
  // until here. Applying it once the layers exist is what makes the page open on Tram rather than
  // showing everything and then snapping.
  applyModeFilter();
});

map.on('moveend', () => {
  clearTimeout(debounceTimer);
  debounceTimer = setTimeout(loadMapData, 500);
});

// Mobility cluster click -> zoom in
map.on('click', 'mobility-cluster', (e) => {
  const features = map.queryRenderedFeatures(e.point, { layers: ['mobility-cluster'] });
  const clusterId = features[0].properties.cluster_id;
  map.getSource(mobilitySource).getClusterExpansionZoom(clusterId, (err, zoom) => {
    if (err) return;
    map.easeTo({ center: features[0].geometry.coordinates, zoom });
  });
});

// Mobility dot click -> show details
map.on('click', 'mobility-dot', (e) => {
  const props = e.features[0].properties;
  showMobilityStationSidebar(props);
});

map.on('mouseenter', 'mobility-cluster', () => { map.getCanvas().style.cursor = 'pointer'; });
map.on('mouseleave', 'mobility-cluster', () => { map.getCanvas().style.cursor = ''; });
map.on('mouseenter', 'mobility-dot', () => { map.getCanvas().style.cursor = 'pointer'; });
map.on('mouseleave', 'mobility-dot', () => { map.getCanvas().style.cursor = ''; });

// Click cluster -> zoom in
map.on('click', 'stations-cluster', (e) => {
  const features = map.queryRenderedFeatures(e.point, { layers: ['stations-cluster'] });
  const clusterId = features[0].properties.cluster_id;
  map.getSource(stationsSource).getClusterExpansionZoom(clusterId, (err, zoom) => {
    if (err) return;
    map.easeTo({ center: features[0].geometry.coordinates, zoom });
  });
});

// Click individual stop -> show details
map.on('click', 'stations-circle', (e) => {
  const props = e.features[0].properties;
  showStationDetails(props.id, props);
});

map.on('mouseenter', 'stations-cluster', () => { map.getCanvas().style.cursor = 'pointer'; });
map.on('mouseleave', 'stations-cluster', () => { map.getCanvas().style.cursor = ''; });
map.on('mouseenter', 'stations-circle', () => { map.getCanvas().style.cursor = 'pointer'; });
map.on('mouseleave', 'stations-circle', () => { map.getCanvas().style.cursor = ''; });

// Route line interaction
map.on('mouseenter', 'routes-line', () => { map.getCanvas().style.cursor = 'pointer'; });
map.on('mouseleave', 'routes-line', () => { map.getCanvas().style.cursor = ''; });
map.on('click', 'routes-line', (e) => {
  const props = e.features[0].properties;
  showRouteDetails(props);
});

function showStationDetails(id, props) {
  const content = document.getElementById('sidebar-content');
  const type = props.stationType || 'unknown';
  const rt = props.primaryRouteType || props.routeType || '';
  const color = getRouteColor(rt);

  let html = `
    <h2>${esc(props.name || 'Unknown')}</h2>
    <div class="info-row"><strong>Type</strong> ${formatEnumName(type)}</div>
    <div class="info-row"><strong>Route type</strong> <span class="route-type-badge" style="background:${color}">${esc(rt) || '-'}</span></div>
    <div class="info-row"><strong>Country</strong> ${esc(props.countryName || '-')}</div>
    <div class="departures"><h3>Next departures</h3><p class="loading">Loading...</p></div>
  `;

  document.getElementById('sidebar').classList.add('open');
  sidebarOpen = true;
  content.innerHTML = html;

  fetch(`/stations/${id}/departures?count=5`).then(r => r.json()).then(resp => {
    const deps = resp || [];
    if (deps.length === 0) {
      document.querySelector('.departures .loading').textContent = 'No upcoming departures';
      return;
    }
    let depHtml = '<h3>Next departures</h3>';
    deps.forEach(d => {
      const time = d.scheduledDeparture ? new Date(d.scheduledDeparture).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }) : '-';
      const delay = d.delaySeconds ? ` (${Math.round(d.delaySeconds / 60)} min delay)` : '';
      depHtml += `<div class="departure-item"><strong>${time}</strong> ${d.headsign ? esc(d.headsign) + ' — ' : ''}${esc(d.routeName || '')}${delay}</div>`;
    });
    document.querySelector('.departures').innerHTML = depHtml;
  }).catch(() => {
    document.querySelector('.departures .loading').textContent = 'Failed to load departures';
  });
}

function showRouteDetails(props) {
  const content = document.getElementById('sidebar-content');
  const color = getRouteColor(props.routeType || 'default');
  const name = props.name || props.shortName || 'Unknown';

  let html = `
    <h2>${esc(name)}</h2>
    <div class="info-row"><strong>Route type</strong> <span class="route-type-badge" style="background:${color}">${esc(props.routeType) || '-'}</span></div>
  `;

  document.getElementById('sidebar').classList.add('open');
  sidebarOpen = true;
  content.innerHTML = html;
}

function showMobilityStationSidebar(props) {
  const content = document.getElementById('sidebar-content');
  const cap = props.capacity > 0 ? props.capacity : null;
  const pct = cap ? Math.round(props.availableVehicles / cap * 100) : null;
  const loc = props.countryName || '';
  let html = `
    <h2>${esc(props.name || 'Unknown')}</h2>
    <div class="info-row"><strong>Provider</strong> ${esc(props.providerName || '-')}</div>
    <div class="info-row"><strong>Available</strong> ${cap ? props.availableVehicles + ' / ' + cap : props.availableVehicles + ' available'}</div>
    <div class="info-row"><strong>Utilization</strong> ${pct !== null ? pct + '%' : 'N/A'}</div>
    ${loc ? `<div class="info-row"><strong>Location</strong> ${esc(loc)}</div>` : ''}
    <div class="info-row"><strong>Last updated</strong> ${props.lastUpdated ? new Date(props.lastUpdated).toLocaleString() : '-'}</div>
  `;
  document.getElementById('sidebar').classList.add('open');
  sidebarOpen = true;
  content.innerHTML = html;
}

function closeSidebar() {
  document.getElementById('sidebar').classList.remove('open');
  sidebarOpen = false;
}

function esc(s) {
  if (!s) return '';
  const d = document.createElement('div');
  d.textContent = s;
  return d.innerHTML;
}

window.addEventListener('pagehide', () => { if (vehiclesInterval) clearInterval(vehiclesInterval); });

// Wired here rather than with an inline onclick attribute: the page's CSP has no
// 'unsafe-inline' in script-src, so an inline handler would silently stop firing.
document.getElementById('sidebarClose').addEventListener('click', closeSidebar);

// ════════════════════════════════════════════════════════════════════════════
//  Map chrome
//
//  Search, transport-mode chips and the two round controls. The MAUI client used to draw these
//  natively over this page and drive them through EvaluateJavaScriptAsync — the page exported
//  setModeFilter/recentreMap/toggleRouteLines and owned none of the state. That bridge was one-way,
//  so the chips could never reflect anything the map knew, and every control existed twice: once in
//  XAML and once here. The page now owns both halves and the client is just a WebView.
// ════════════════════════════════════════════════════════════════════════════

// ── Language ────────────────────────────────────────────────────────────────
// Comes from ?lang= (the client passes its current culture), falling back to the browser and then
// to English. Strings are lifted from the client's AppResources.resx / AppResources.hr.resx, which
// no longer carry them.
const STRINGS = {
  en: {
    search: 'Where are you going?',
    Tram: 'Tram', Bus: 'Bus', Train: 'Train', Bikes: 'Bikes',
    recentre: 'Recentre', layers: 'Layers',
    noResults: 'No stations found'
  },
  hr: {
    search: 'Kamo putujete?',
    Tram: 'Tramvaj', Bus: 'Autobus', Train: 'Vlak', Bikes: 'Bicikli',
    recentre: 'Centriraj', layers: 'Slojevi',
    noResults: 'Nema pronađenih stanica'
  }
};

const LANG = (() => {
  const requested = new URLSearchParams(location.search).get('lang')
    || (navigator.language || 'en');
  // Accept "hr-HR" as readily as "hr" — the client sends a two-letter code, a browser may not.
  const short = requested.toLowerCase().split('-')[0];
  return STRINGS[short] ? short : 'en';
})();

function t(key) { return STRINGS[LANG][key] ?? STRINGS.en[key] ?? key; }

// ── Transport-mode chips ────────────────────────────────────────────────────
// Keys map to the routeType values the stations and routes carry. Bike share is not a route type at
// all — it lives in its own source — so it is handled as a visibility toggle below.
const MODE_ROUTE_TYPES = {
  Tram: ['Tram'],
  Bus: ['Bus', 'Trolleybus'],
  Train: ['Subway', 'Train', 'Ferry', 'CableTram', 'CableCar', 'Funicular', 'Monorail']
};

const MODES = ['Tram', 'Bus', 'Train', 'Bikes'];

// Frame 3a opens with Tram lit.
const selectedModes = new Set(['Tram']);

function renderChips() {
  const host = document.getElementById('modeChips');
  host.replaceChildren(...MODES.map(mode => {
    const btn = document.createElement('button');
    btn.type = 'button';
    btn.className = selectedModes.has(mode) ? 'chip selected' : 'chip';
    btn.dataset.mode = mode;
    btn.textContent = t(mode);
    btn.setAttribute('aria-pressed', selectedModes.has(mode) ? 'true' : 'false');
    return btn;
  }));
}

/**
 * Applies the current chip selection to the map layers.
 *
 * All modes selected is the same as none, and both mean "show everything" — which is also the state
 * the layers are built in.
 */
function applyModeFilter() {
  if (!map || !map.isStyleLoaded()) return;

  const showAll = selectedModes.size === 0 || selectedModes.size === MODES.length;

  // Bike share lives in its own source, so it is a visibility toggle rather than a filter.
  const showBikes = showAll || selectedModes.has('Bikes');
  for (const id of ['mobility-cluster', 'mobility-cluster-count', 'mobility-dot']) {
    if (map.getLayer(id)) {
      map.setLayoutProperty(id, 'visibility', showBikes ? 'visible' : 'none');
    }
  }

  // Stops carry primaryRouteType, so the unclustered icons can be filtered directly. Clusters
  // aggregate across types and cannot be filtered by a member property, so they are hidden while a
  // transport filter is on and the individual stops carry the answer instead.
  const wanted = [...selectedModes].flatMap(m => MODE_ROUTE_TYPES[m] || []);
  const filteringStops = wanted.length > 0 && !showAll;

  if (map.getLayer('stations-circle')) {
    map.setFilter('stations-circle', filteringStops
      ? ['all', ['!', ['has', 'point_count']], ['in', ['get', 'primaryRouteType'], ['literal', wanted]]]
      : ['!', ['has', 'point_count']]);
  }

  for (const id of ['stations-cluster', 'stations-cluster-count']) {
    if (map.getLayer(id)) {
      map.setLayoutProperty(id, 'visibility', filteringStops ? 'none' : 'visible');
    }
  }

  // Route lines and live vehicles belong to the scheduled network, so they follow the same
  // transport selection and switch off entirely when only bikes are asked for.
  const showNetwork = showAll || wanted.length > 0;
  for (const id of ['routes-line', 'vehicles-circle', 'vehicles-arrow']) {
    if (map.getLayer(id)) {
      map.setLayoutProperty(id, 'visibility', showNetwork ? 'visible' : 'none');
    }
  }

  // The layers button toggles route lines independently; a mode change re-shows them, so keep its
  // indicator honest rather than letting it claim a state the map is no longer in.
  syncLayersButton();
}

// One delegated listener rather than one per chip, so re-rendering on a language change cannot
// leave handlers behind.
document.getElementById('modeChips').addEventListener('click', e => {
  const chip = e.target.closest('.chip');
  if (!chip) return;

  const mode = chip.dataset.mode;
  if (selectedModes.has(mode)) selectedModes.delete(mode); else selectedModes.add(mode);

  // Turning the last chip off would leave an empty map, which is never a useful state to be stuck
  // in; restore everything instead.
  if (selectedModes.size === 0) MODES.forEach(m => selectedModes.add(m));

  renderChips();
  applyModeFilter();
});

// ── Round controls ──────────────────────────────────────────────────────────

const DEFAULT_CENTRE = [16.0, 45.8];
const DEFAULT_ZOOM = 7;

/** Recentres on the device's position, falling back to the country view if that is unavailable. */
document.getElementById('btnRecentre').addEventListener('click', () => {
  if (!navigator.geolocation) {
    map.easeTo({ center: DEFAULT_CENTRE, zoom: DEFAULT_ZOOM });
    return;
  }

  navigator.geolocation.getCurrentPosition(
    pos => map.easeTo({ center: [pos.coords.longitude, pos.coords.latitude], zoom: 14 }),
    () => map.easeTo({ center: DEFAULT_CENTRE, zoom: DEFAULT_ZOOM }),
    { enableHighAccuracy: true, timeout: 8000, maximumAge: 60000 }
  );
});

/** Layers button: shows or hides the route shapes over the basemap. */
document.getElementById('btnLayers').addEventListener('click', () => {
  if (!map.isStyleLoaded() || !map.getLayer('routes-line')) return;

  const visible = map.getLayoutProperty('routes-line', 'visibility') !== 'none';
  map.setLayoutProperty('routes-line', 'visibility', visible ? 'none' : 'visible');
  syncLayersButton();
});

function syncLayersButton() {
  const btn = document.getElementById('btnLayers');
  if (!btn || !map.isStyleLoaded() || !map.getLayer('routes-line')) return;

  const visible = map.getLayoutProperty('routes-line', 'visibility') !== 'none';
  btn.classList.toggle('off', !visible);
}

// ── Station search ──────────────────────────────────────────────────────────
// /stations/search is anonymous, same-origin and unmetered by any cache, so the request is kept
// cheap: debounced, floored at two characters, and the previous one aborted rather than raced.

const searchInput = document.getElementById('searchInput');
const searchResults = document.getElementById('searchResults');
let searchTimer = null;
let searchAbort = null;

function closeSearchResults() {
  searchResults.hidden = true;
  searchResults.replaceChildren();
  searchInput.setAttribute('aria-expanded', 'false');
}

function renderSearchResults(stations) {
  if (stations.length === 0) {
    const li = document.createElement('li');
    li.className = 'empty';
    li.textContent = t('noResults');
    searchResults.replaceChildren(li);
  } else {
    searchResults.replaceChildren(...stations.map(s => {
      const li = document.createElement('li');
      li.setAttribute('role', 'option');
      li.textContent = s.name || '';

      const where = [s.cityName, s.countryName].filter(Boolean).join(', ');
      if (where) {
        const sub = document.createElement('span');
        sub.className = 'sub';
        sub.textContent = where;
        li.appendChild(sub);
      }

      li.addEventListener('click', () => selectStation(s));
      return li;
    }));
  }

  searchResults.hidden = false;
  searchInput.setAttribute('aria-expanded', 'true');
}

/**
 * Flies to a picked station and opens the detail sidebar.
 *
 * A StationResponse already carries the property names showStationDetails expects, so the existing
 * sidebar renders it without a second code path.
 */
function selectStation(station) {
  closeSearchResults();
  searchInput.blur();
  map.easeTo({ center: [station.longitude, station.latitude], zoom: 15 });
  showStationDetails(station.id, station);
}

function runSearch(query) {
  if (searchAbort) searchAbort.abort();
  searchAbort = new AbortController();

  fetch(`/stations/search?q=${encodeURIComponent(query)}&perPage=8`, { signal: searchAbort.signal })
    .then(r => {
      if (!r.ok) throw new Error('Search failed (' + r.status + ')');
      return r.json();
    })
    .then(page => renderSearchResults(page?.data || []))
    .catch(err => {
      // An aborted request is this code superseding itself, not a failure worth reporting.
      if (err.name === 'AbortError') return;
      closeSearchResults();
      showMapError('Search failed');
    });
}

searchInput.addEventListener('input', () => {
  clearTimeout(searchTimer);
  const query = searchInput.value.trim();

  if (query.length < 2) {
    if (searchAbort) searchAbort.abort();
    closeSearchResults();
    return;
  }

  searchTimer = setTimeout(() => runSearch(query), 250);
});

searchInput.addEventListener('keydown', e => {
  if (e.key === 'Escape') {
    closeSearchResults();
    searchInput.blur();
  }
});

// Dismiss on an outside tap, and whenever the user starts moving the map — the results describe a
// place they have just navigated away from.
document.addEventListener('click', e => {
  if (!searchResults.hidden && !e.target.closest('#chrome')) closeSearchResults();
});
map.on('movestart', () => { if (!searchResults.hidden) closeSearchResults(); });

// ── Boot ────────────────────────────────────────────────────────────────────
// Chips render immediately; applyModeFilter runs from the map's load handler, once there are layers
// to filter.
document.documentElement.lang = LANG;
searchInput.placeholder = t('search');
document.getElementById('btnRecentre').setAttribute('aria-label', t('recentre'));
document.getElementById('btnLayers').setAttribute('aria-label', t('layers'));
renderChips();
