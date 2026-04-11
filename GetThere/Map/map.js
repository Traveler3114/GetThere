// ═══════════════════════════════════════════════════════════════════
// CONFIG
// ═══════════════════════════════════════════════════════════════════

const _TL_TILES_BASE = (window._TL_TILES_BASE || 'https://transit.land/api/v2/tiles').replace(/\/+$/, '');
const _TL_API_KEY = (window._TL_API_KEY || '').trim();

// ═══════════════════════════════════════════════════════════════════
// VIEWPORT STOP FETCHING
// Fetches stops from our REST API for the current map viewport.
// REST API returns correct route_type per stop → correct icons.
// ═══════════════════════════════════════════════════════════════════

const STOP_FETCH_MIN_ZOOM = 11;
let _stopFetchTimer = null;
let _lastFetchedBbox = null;

function _scheduleStopFetch() {
    if (_stopFetchTimer) clearTimeout(_stopFetchTimer);
    _stopFetchTimer = setTimeout(_fetchViewportStops, 400);
}

async function _fetchViewportStops() {
    const zoom = map.getZoom();
    if (zoom < STOP_FETCH_MIN_ZOOM) {
        map.getSource('gtfs-stops')?.setData({ type: 'FeatureCollection', features: [] });
        _lastFetchedBbox = null;
        return;
    }

    const bounds = map.getBounds();
    const bbox = [
        bounds.getWest().toFixed(6),
        bounds.getSouth().toFixed(6),
        bounds.getEast().toFixed(6),
        bounds.getNorth().toFixed(6),
    ].join(',');

    if (bbox === _lastFetchedBbox) return;
    _lastFetchedBbox = bbox;

    const apiBase = (window._API_BASE || '').replace(/\/+$/, '');
    if (!apiBase) return;

    try {
        const resp = await fetch(`${apiBase}/map/stops?bbox=${encodeURIComponent(bbox)}`);
        if (!resp.ok) return;
        const json = await resp.json();
        const stops = json?.data || [];
        console.log(`[ViewportStops] ${stops.length} stops at zoom=${zoom.toFixed(1)}`);
        renderStops(stops);
    } catch (e) {
        console.warn('[ViewportStops] fetch failed:', e);
    }
}

// ═══════════════════════════════════════════════════════════════════
// BOOT
// ═══════════════════════════════════════════════════════════════════

window.map = new maplibregl.Map({
    container: 'map',
    style: window._MAP_STYLE,
    center: [15.9775, 45.8129],
    zoom: 13,
    minZoom: 3,
    maxPitch: 60,
    transformRequest: (url) => {
        if (!_TL_API_KEY || typeof url !== 'string') return { url };
        if (!url.startsWith(_TL_TILES_BASE)) return { url };
        return { url, headers: { apikey: _TL_API_KEY } };
    }
});

map.addControl(new maplibregl.NavigationControl(), 'top-right');

map.on('style.load', async function () {
    try {
        await _onMapLoad();
    } catch (e) {
        window._jsError = 'onMapLoad ERROR: ' + e.message;
    }
});

// ═══════════════════════════════════════════════════════════════════
// MAP LOAD
// ═══════════════════════════════════════════════════════════════════

async function _onMapLoad() {
    console.log('[DEBUG] _onMapLoad start');

    // Route tile overlay only — no stop vector tiles needed anymore
    _addTransitlandRouteTileLayer();

    // ── Stop source + layer ────────────────────────────────────────
    map.addSource('gtfs-stops', {
        type: 'geojson',
        data: { type: 'FeatureCollection', features: [] }
    });

    _loadStopIcons();

    map.addLayer({
        id: 'stops',
        type: 'symbol',
        source: 'gtfs-stops',
        minzoom: 14,
        layout: {
            'icon-image': _buildIconExpression(),
            'icon-size': 0.375,
            'icon-anchor': 'bottom',
            'icon-allow-overlap': true,
            'icon-ignore-placement': true,
            'text-field': ['step', ['zoom'], '', 17, ['get', 'name']],
            'text-size': 11,
            'text-offset': [0, 0.2],
            'text-anchor': 'top',
            'text-allow-overlap': false,
            'text-optional': true,
            'text-font': ['Noto Sans Regular'],
        },
        paint: {
            'text-color': _buildColorExpression(),
            'text-halo-color': '#fff',
            'text-halo-width': 1,
        }
    });
    map.on('mouseenter', 'stops', () => map.getCanvas().style.cursor = 'pointer');
    map.on('mouseleave', 'stops', () => map.getCanvas().style.cursor = '');

    // ── Vehicle source + layers ────────────────────────────────────
    map.addSource('vehicles', {
        type: 'geojson',
        data: { type: 'FeatureCollection', features: [] }
    });

    map.on('styleimagemissing', async e => {
        const id = e.id;
        if (!id.endsWith('-bg') && !id.endsWith('-fg')) return;

        const suffix = id.endsWith('-bg') ? 'bg' : 'fg';
        const base = id.slice(0, -(suffix.length + 1));
        const parts = base.split('-');
        const realTime = parts[parts.length - 1] === 'true';
        const routeType = parseInt(parts[parts.length - 2], 10);
        const routeShortName = parts.slice(0, parts.length - 2).join('-');

        let color;
        if (routeType === 0) {
            color = realTime ? '#1264AB' : '#535353';
        } else {
            color = realTime ? '#126400' : '#727272';
        }

        const data = suffix === 'bg'
            ? _genVehicleBg(color)
            : await _genVehicleFg(color, 'white', routeShortName);

        if (!map.hasImage(id))
            map.addImage(id, {
                width: suffix === 'bg' ? 64 : 42,
                height: suffix === 'bg' ? 64 : 42,
                data
            });
    });

    // Fallback circle for missing stop icons
    map.on('styleimagemissing', e => {
        const id = e.id;
        if (!id.startsWith('stop-')) return;

        const entry = Object.values(STOP_ICON_MAP).find(cfg => cfg.id === id);
        const fillHex = (entry?.color || _defaultColor || '#126400').replace('#', '');
        const fillR = parseInt(fillHex.slice(0, 2), 16) || 18;
        const fillG = parseInt(fillHex.slice(2, 4), 16) || 100;
        const fillB = parseInt(fillHex.slice(4, 6), 16) || 0;

        const size = 32;
        const center = size / 2;
        const outerR = center - 1;
        const innerR = center - 4;
        const px = new Uint8Array(size * size * 4);
        for (let y = 0; y < size; y++) {
            for (let x = 0; x < size; x++) {
                const dist = Math.sqrt((x - center) ** 2 + (y - center) ** 2);
                const i = (y * size + x) * 4;
                if (dist <= innerR) {
                    px[i] = fillR; px[i + 1] = fillG; px[i + 2] = fillB; px[i + 3] = 255;
                } else if (dist <= outerR) {
                    px[i] = 255; px[i + 1] = 255; px[i + 2] = 255; px[i + 3] = 230;
                }
            }
        }
        if (!map.hasImage(id))
            map.addImage(id, { width: size, height: size, data: px });
    });

    map.addLayer({
        id: 'vehicles-bg', type: 'symbol', source: 'vehicles',
        layout: {
            'icon-image': ['concat',
                ['get', 'routeShortName'], '-',
                ['to-string', ['get', 'routeType']], '-',
                ['to-string', ['get', 'isRealtime']], '-bg'
            ],
            'icon-size': 0.65,
            'icon-allow-overlap': true,
            'icon-ignore-placement': true,
            'icon-rotation-alignment': 'map',
            'icon-rotate': ['get', 'bearing'],
            'symbol-sort-key': ['*', ['get', 'idx'], 2],
            'symbol-z-order': 'auto',
            'text-font': ['Noto Sans Regular'],
        }
    });

    map.addLayer({
        id: 'vehicles-fg', type: 'symbol', source: 'vehicles',
        layout: {
            'icon-image': ['concat',
                ['get', 'routeShortName'], '-',
                ['to-string', ['get', 'routeType']], '-',
                ['to-string', ['get', 'isRealtime']], '-fg'
            ],
            'icon-size': 0.65,
            'icon-allow-overlap': true,
            'icon-ignore-placement': true,
            'symbol-sort-key': ['*', ['get', 'idx'], 2],
            'symbol-z-order': 'source',
            'text-font': ['Noto Sans Regular'],
        }
    });

    map.addLayer({
        id: 'vehicle-details', type: 'symbol', source: 'vehicles',
        minzoom: 15,
        layout: {
            'text-field': ['concat',
                ['literal', 'VR: '], ['get', 'blockId'],
                ['literal', '\nGB: '], ['get', 'vehicleId']
            ],
            'text-font': ['Noto Sans Regular'],
            'text-size': 12,
            'text-anchor': 'left',
            'text-offset': [2, 0],
            'text-justify': 'left',
            'symbol-z-order': 'source',
            'icon-allow-overlap': true,
            'text-allow-overlap': true,
        },
        paint: {
            'text-color': '#000',
            'text-halo-color': '#fff',
            'text-halo-width': 12,
        }
    });

    // ── Bike station source + layer ────────────────────────────────
    map.addSource('bike-stations', {
        type: 'geojson',
        data: { type: 'FeatureCollection', features: [] }
    });

    map.addLayer({
        id: 'bike-stations', type: 'circle', source: 'bike-stations',
        minzoom: 13,
        paint: {
            'circle-radius': ['interpolate', ['linear'], ['zoom'], 13, 6, 16, 10],
            'circle-color': '#00a651',
            'circle-stroke-color': '#fff',
            'circle-stroke-width': 2,
            'circle-opacity': ['interpolate', ['linear'], ['zoom'], 13, 0.8, 16, 1],
        }
    });

    map.addLayer({
        id: 'bike-stations-label', type: 'symbol', source: 'bike-stations',
        minzoom: 15,
        layout: {
            'text-field': ['to-string', ['get', 'availableBikes']],
            'text-size': 11,
            'text-font': ['Noto Sans Regular'],
            'text-anchor': 'center',
            'text-allow-overlap': true,
            'text-ignore-placement': true,
        },
        paint: { 'text-color': '#fff' }
    });

    map.on('mouseenter', 'bike-stations', () => map.getCanvas().style.cursor = 'pointer');
    map.on('mouseleave', 'bike-stations', () => map.getCanvas().style.cursor = '');

    // Stops sit below vehicles
    map.moveLayer('stops', 'vehicles-bg');

    // ── Click events ───────────────────────────────────────────────
    map.on('click', 'stops', e => {
        if (e.defaultPrevented) return;
        e.preventDefault();
        const p = e.features[0].properties;
        _openStopSheet(p.stopId, p.name,
            typeof p.routeType === 'number' ? p.routeType : parseInt(p.routeType) || 3,
            p.supportsSchedule !== false);
    });

    ['vehicles-fg', 'vehicles-bg'].forEach(lyr => {
        map.on('click', lyr, e => {
            if (e.defaultPrevented) return;
            e.preventDefault();
            const p = e.features[0].properties;
            if (p.tripId) _openTripPanel(p.tripId);
        });
        map.on('mouseenter', lyr, () => map.getCanvas().style.cursor = 'pointer');
        map.on('mouseleave', lyr, () => map.getCanvas().style.cursor = '');
    });

    map.on('click', 'bike-stations', e => {
        if (e.defaultPrevented) return;
        e.preventDefault();
        _openBikeSheet(e.features[0].properties);
    });

    map.on('click', e => {
        if (e.defaultPrevented) return;
        _closeStopSheet();
        _closeTripPanel();
        _closeBikeSheet();
    });

    // ── Viewport stop fetching ─────────────────────────────────────
    map.on('moveend', _scheduleStopFetch);
    _scheduleStopFetch();

    console.log('[DEBUG] _onMapLoad complete — _mapReady = true');
    window._mapReady = true;
}

// Route tile overlay only — stops come from REST API now
function _addTransitlandRouteTileLayer() {
    if (!_TL_API_KEY || !_TL_TILES_BASE) return;

    try {
        if (!map.getSource('transitland-routes-vt')) {
            map.addSource('transitland-routes-vt', {
                type: 'vector',
                tiles: [`${_TL_TILES_BASE}/routes/tiles/{z}/{x}/{y}.pbf`],
                minzoom: 0,
                maxzoom: 14
            });
        }
        if (!map.getLayer('transitland-routes-vt')) {
            map.addLayer({
                id: 'transitland-routes-vt',
                type: 'line',
                source: 'transitland-routes-vt',
                'source-layer': 'routes',
                layout: { 'line-join': 'round', 'line-cap': 'round' },
                paint: {
                    'line-color': '#2a6f97',
                    'line-width': ['interpolate', ['linear'], ['zoom'], 8, 1, 14, 3],
                    'line-opacity': 0.6
                }
            });
        }
    } catch (e) {
        console.warn('[TransitlandTiles] Route layer init failed:', e);
    }
}

// ═══════════════════════════════════════════════════════════════════
// PUBLIC FUNCTIONS — called by C# via CallJsAsync
// ═══════════════════════════════════════════════════════════════════

function renderStops(stops) {
    const features = (stops || []).map(s => ({
        type: 'Feature',
        geometry: { type: 'Point', coordinates: [s.lon, s.lat] },
        properties: {
            stopId: s.stopId,
            name: s.name,
            routeType: s.routeType ?? 3,
            supportsSchedule: s.supportsSchedule !== false,
        }
    }));

    map.getSource('gtfs-stops')?.setData({ type: 'FeatureCollection', features });
}

function renderVehicles(vehicles) {
    const features = (vehicles || []).map((v, i) => ({
        type: 'Feature',
        geometry: { type: 'Point', coordinates: [v.lon, v.lat] },
        properties: {
            idx: i,
            vehicleId: v.vehicleId || '',
            tripId: v.tripId || '',
            routeId: v.routeId || '',
            routeShortName: v.routeShortName || '?',
            routeType: v.routeType ?? 3,
            isRealtime: v.isRealtime ?? false,
            bearing: v.bearing ?? 0,
            blockId: v.blockId || '',
        }
    }));

    map.getSource('vehicles')?.setData({ type: 'FeatureCollection', features });
}

function renderBikeStations(stations) {
    const features = (stations || []).map(s => ({
        type: 'Feature',
        geometry: { type: 'Point', coordinates: [s.lon, s.lat] },
        properties: {
            stationId: s.stationId,
            name: s.name,
            availableBikes: s.availableBikes,
            capacity: s.capacity,
            providerId: s.providerId,
            providerName: s.providerName,
        }
    }));

    map.getSource('bike-stations')?.setData({ type: 'FeatureCollection', features });
}

function renderStopSchedule(data) {
    if (typeof data === 'string') data = JSON.parse(data);

    _sheetLoad.style.display = 'none';
    _sheetBody.innerHTML = '';

    const groups = data.groups || [];
    if (!groups.length) {
        _sheetEmpty.textContent = 'No upcoming departures.';
        _sheetEmpty.style.display = 'block';
        return;
    }

    const hasRealtime = groups.some(g => (g.departures || []).some(d => d.isRealtime));
    if (!hasRealtime) {
        const badge = document.createElement('div');
        badge.style.cssText = 'padding:6px 16px 2px;font-size:11px;color:#aaa;';
        badge.textContent = '📅 Scheduled only — no realtime data available';
        _sheetBody.appendChild(badge);
    }

    groups.forEach((g, gi) => {
        const pc = _pillClass(g.routeType ?? 3);
        const deps = g.departures || [];

        const timesHtml = deps.map((d, i) => {
            let cls = 'time-chip' + (i === 0 ? ' next' : '');
            const canClick = d.isRealtime || !!d.estimatedTime;
            if (canClick) cls += ' click';
            const onClick = canClick ? `onclick="_openTripPanel('${d.tripId}')"` : '';

            const hasEta = d.estimatedTime && d.estimatedTime !== d.scheduledTime;
            if (hasEta) {
                const delay = d.delayMinutes ?? 0;
                const badge = delay > 5
                    ? `<span class="delay-badge late">+${delay}'</span>`
                    : delay > 1
                        ? `<span class="delay-badge early">+${delay}'</span>`
                        : `<span class="delay-badge ontime">✓</span>`;
                const dot = d.isRealtime ? `<span class="live-dot">●</span>` : '';
                return `<span class="${cls}" ${onClick}>
                            <span class="sched-strike">${d.scheduledTime}</span>
                            <span class="eta-time">${d.estimatedTime}</span>
                            ${badge}${dot}
                        </span>`;
            } else if (d.isRealtime) {
                return `<span class="${cls}" ${onClick}>${d.scheduledTime}<span class="live-dot">●</span></span>`;
            } else {
                return `<span class="${cls}">${d.scheduledTime}</span>`;
            }
        }).join('');

        const tracked = deps.filter(d => d.isRealtime).length;
        const rtBadge = tracked > 0 ? `&nbsp;<span class="tracked-count">📡&nbsp;${tracked}</span>` : '';

        const div = document.createElement('div');
        div.className = 'route-group' + (gi === 0 ? ' expanded' : '');
        div.innerHTML = `
            <div class="route-group-header" onclick="this.parentElement.classList.toggle('expanded')">
                <span class="route-pill ${pc}">${_esc(g.shortName)}</span>
                <span class="route-headsign">${_esc(g.headsign)}</span>
                <span class="route-count">${deps.length}&nbsp;dep.${rtBadge}</span>
                <span class="route-chevron">▾</span>
            </div>
            <div class="route-times">${timesHtml}</div>`;
        _sheetBody.appendChild(div);
    });

    requestAnimationFrame(() => {
        const next = _sheetBody.querySelector('.time-chip.next');
        if (next) next.scrollIntoView({ block: 'nearest' });
    });
}

function renderTripDetail(data) {
    if (typeof data === 'string') data = JSON.parse(data);

    _tripLoading.style.display = 'none';

    const rType = data.routeType ?? 3;
    _tripPill.className = 'route-pill ' + _pillClass(rType);
    _tripPill.textContent = data.shortName || data.routeId || '?';
    _tripHead.textContent = data.headsign || '';

    const status = data.isRealtime ? '📡 Live'
        : (data.stops || []).some(s => s.estimatedTime) ? '⏱ Predicted' : '📅 Scheduled';
    _tripSubtitle.textContent = status;
    _tripSubtitle.style.cssText = 'padding:2px 16px 10px;font-size:12px;color:#888;flex-shrink:0';

    _tripBody.innerHTML = '';
    const stops = data.stops || [];
    const curIdx = data.currentStopIndex ?? 0;

    stops.forEach((s, i) => {
        const isPassed = s.isPassed;
        const isCurrent = i === curIdx && !isPassed;
        let cls = 'trip-stop';
        if (isPassed) cls += ' passed';
        if (isCurrent) cls += ' current';

        const hasEta = s.estimatedTime && s.estimatedTime !== s.scheduledTime;
        const times = hasEta
            ? `<div class="trip-stop-times">
                <span class="ts-sched">${s.scheduledTime}</span>
                <span class="ts-eta ${(s.delayMinutes ?? 0) > 1 ? 'late' : (s.delayMinutes ?? 0) < -1 ? 'early' : 'ontime'}">${s.estimatedTime}</span>
               </div>`
            : `<div class="trip-stop-times"><span class="ts-only">${s.scheduledTime}</span></div>`;

        const row = document.createElement('div');
        row.className = cls;
        row.innerHTML = `
            <div class="trip-stop-dot"></div>
            <div class="trip-stop-info"><div class="trip-stop-name">${_esc(s.stopName)}</div></div>
            ${times}`;
        _tripBody.appendChild(row);
    });

    if (data.isRealtime && data.vehicleLat && data.vehicleLon)
        map.flyTo({ center: [data.vehicleLon, data.vehicleLat], zoom: 15, duration: 600 });

    requestAnimationFrame(() => {
        const cur = _tripBody.querySelector('.trip-stop.current');
        if (cur) cur.scrollIntoView({ block: 'center' });
    });
}

function updateMapLocation(lng, lat) {
    if (!_userMarker) {
        _userMarker = new maplibregl.Marker({ color: '#4285F4' })
            .setLngLat([lng, lat]).addTo(map);
    } else {
        _userMarker.setLngLat([lng, lat]);
    }
    map.setCenter([lng, lat]);
    map.setZoom(15);
}

// ═══════════════════════════════════════════════════════════════════
// STATE
// ═══════════════════════════════════════════════════════════════════

let _userMarker = null;
let _currentStop = null;
let _scheduleRefreshTimer = null;

const _sheet = document.getElementById('stop-sheet');
const _sheetName = document.getElementById('sheet-stop-name');
const _sheetSid = document.getElementById('sheet-stop-id');
const _sheetDate = document.getElementById('sheet-date');
const _sheetLoad = document.getElementById('sheet-loading');
const _sheetEmpty = document.getElementById('sheet-empty');
const _sheetBody = document.getElementById('sheet-body');
const _tripPanel = document.getElementById('trip-panel');
const _tripPill = document.getElementById('trip-panel-pill');
const _tripHead = document.getElementById('trip-panel-headsign');
const _tripSubtitle = document.getElementById('trip-panel-subtitle');
const _tripLoading = document.getElementById('trip-panel-loading');
const _tripBody = document.getElementById('trip-panel-body');
const _bikeSheet = document.getElementById('bike-sheet');
const _bikeSheetName = document.getElementById('bike-sheet-name');
const _bikeSheetBody = document.getElementById('bike-sheet-body');

// ═══════════════════════════════════════════════════════════════════
// PANELS
// ═══════════════════════════════════════════════════════════════════

function _openStopSheet(stopId, stopName, routeType, supportsSchedule = true) {
    _tripPanel.classList.remove('open');
    _clearScheduleRefresh();
    _currentStop = stopId;

    _sheetName.textContent = stopName;
    const isTram = routeType === 0 || routeType === 11;
    const isTrain = routeType === 2;
    _sheetSid.textContent = (isTrain ? '🚂 ' : isTram ? '🚋 ' : '🚌 ') + '#' + stopId;

    const now = new Date();
    const days = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
    const months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
    _sheetDate.textContent = `${days[now.getDay()]}, ${now.getDate()} ${months[now.getMonth()]} ${now.getFullYear()}`;

    _sheetLoad.style.display = 'flex';
    _sheetEmpty.style.display = 'none';
    _sheetEmpty.textContent = 'No upcoming departures.';
    _sheetBody.innerHTML = '';
    _sheet.classList.add('open');

    if (!supportsSchedule) {
        _sheetLoad.style.display = 'none';
        _sheetEmpty.textContent = 'Schedule is not available for this stop.';
        _sheetEmpty.style.display = 'block';
        return;
    }

    window._pendingMsg = 'stopSchedule:' + stopId;
}

function _closeStopSheet() {
    _clearScheduleRefresh();
    _currentStop = null;
    _sheet.classList.remove('open');
    window._pendingMsg = 'stopClosed';
}

function _clearScheduleRefresh() {
    if (_scheduleRefreshTimer !== null) {
        clearInterval(_scheduleRefreshTimer);
        _scheduleRefreshTimer = null;
    }
}

function _openTripPanel(tripId) {
    _sheet.classList.remove('open');
    _clearScheduleRefresh();
    _currentStop = null;

    _tripPill.className = 'route-pill bus';
    _tripPill.textContent = '…';
    _tripHead.textContent = 'Loading…';
    _tripSubtitle.textContent = '';
    _tripLoading.style.display = 'flex';
    _tripBody.innerHTML = '';
    _tripPanel.classList.add('open');

    window._pendingMsg = 'tripDetail:' + tripId;
}

function _closeTripPanel() {
    _tripPanel.classList.remove('open');
}

function _openBikeSheet(p) {
    _sheet.classList.remove('open');
    _tripPanel.classList.remove('open');
    _clearScheduleRefresh();
    _currentStop = null;

    _bikeSheetName.textContent = p.name || p.stationId;

    const avail = p.availableBikes ?? 0;
    const cap = p.capacity ?? 0;
    const fillPct = cap > 0 ? Math.min(100, Math.round((avail / cap) * 100)) : 0;

    _bikeSheetBody.innerHTML = `
        <div class="bike-stat-row">
            <span class="bike-stat-icon">🚲</span>
            <div>
                <div class="bike-stat-label">Available bikes</div>
                <div class="bike-stat-value">${avail}${cap > 0 ? ' / ' + cap : ''}</div>
            </div>
        </div>
        ${cap > 0 ? `<div class="bike-availability-bar">
            <div class="bike-availability-fill" style="width:${fillPct}%"></div>
        </div>` : ''}
        ${p.providerName ? `<div class="bike-stat-row">
            <span class="bike-stat-icon">🏢</span>
            <div>
                <div class="bike-stat-label">Provider</div>
                <div class="bike-provider-name">${_esc(p.providerName)}</div>
            </div>
        </div>` : ''}`;

    _bikeSheet.classList.add('open');
}

function _closeBikeSheet() {
    _bikeSheet.classList.remove('open');
}

// ═══════════════════════════════════════════════════════════════════
// STOP ICON LOADER
// ═══════════════════════════════════════════════════════════════════

const STOP_ICON_MAP = Object.fromEntries(
    (window._TRANSPORT_TYPES || []).map(t => [
        t.gtfsRouteType,
        { id: 'stop-' + t.iconFile.replace('.png', ''), file: t.iconFile, color: t.color }
    ])
);

const _defaultEntry = STOP_ICON_MAP[3] || Object.values(STOP_ICON_MAP)[0] || { id: 'stop-bus', color: '#126400' };
const _defaultIconId = _defaultEntry.id;
const _defaultColor = _defaultEntry.color;

function _buildIconExpression() {
    const nonDefault = Object.entries(STOP_ICON_MAP).filter(([, cfg]) => cfg.id !== _defaultIconId);
    if (!nonDefault.length) return _defaultIconId;
    const expr = ['case'];
    for (const [type, cfg] of nonDefault)
        expr.push(['==', ['get', 'routeType'], parseInt(type)], cfg.id);
    expr.push(_defaultIconId);
    return expr;
}

function _buildColorExpression() {
    const nonDefault = Object.entries(STOP_ICON_MAP).filter(([, cfg]) => cfg.color !== _defaultColor);
    if (!nonDefault.length) return _defaultColor;
    const expr = ['case'];
    for (const [type, cfg] of nonDefault)
        expr.push(['==', ['get', 'routeType'], parseInt(type)], cfg.color);
    expr.push(_defaultColor);
    return expr;
}

function _loadStopIcons() {
    const unique = Object.values(
        Object.values(STOP_ICON_MAP).reduce((acc, e) => { acc[e.id] = e; return acc; }, {})
    );
    return Promise.all(unique.map(({ id, file }) =>
        new Promise(resolve => {
            const dataUri = window._ICON_DATA && window._ICON_DATA[file];
            const url = dataUri || (window._API_BASE || '') + '/images/' + file;
            const img = new Image();
            img.onload = () => {
                try {
                    const canvas = document.createElement('canvas');
                    canvas.width = img.width;
                    canvas.height = img.height;
                    canvas.getContext('2d').drawImage(img, 0, 0);
                    const imageData = canvas.getContext('2d').getImageData(0, 0, img.width, img.height);
                    if (!map.hasImage(id)) map.addImage(id, imageData);
                } catch (e) {
                    console.error('[StopIcons] addImage failed:', id, e);
                }
                resolve();
            };
            img.onerror = () => resolve();
            if (!dataUri) img.crossOrigin = 'anonymous';
            img.src = url;
        })
    ));
}

// ═══════════════════════════════════════════════════════════════════
// VEHICLE ICON GENERATORS
// ═══════════════════════════════════════════════════════════════════

function _genVehicleBg(color) {
    const canvas = document.createElement('canvas');
    canvas.width = canvas.height = 64;
    const ctx = canvas.getContext('2d');
    const center = 32;
    ctx.fillStyle = darkenColor(color, 0.8);
    ctx.beginPath();
    ctx.moveTo(center, 0);
    ctx.lineTo(center - 18, 20);
    ctx.lineTo(center + 18, 20);
    ctx.closePath();
    ctx.fill();
    return ctx.getImageData(0, 0, 64, 64).data;
}

async function _genVehicleFg(bg, fg, text) {
    const canvas = document.createElement('canvas');
    canvas.width = canvas.height = 42;
    const ctx = canvas.getContext('2d');
    const center = 21;

    ctx.fillStyle = bg;
    ctx.beginPath();
    ctx.arc(center, center, 20, 0, Math.PI * 2);
    ctx.fill();

    ctx.lineWidth = 2;
    ctx.strokeStyle = darkenColor(bg, 0.8);
    ctx.stroke();

    try {
        await Promise.all([
            document.fonts.load('bold 24px "Noto Sans"'),
            document.fonts.load('bold 20px "Noto Sans"'),
            document.fonts.load('bold 18px "Noto Sans"')
        ]);
    } catch (_) { }

    ctx.fillStyle = fg;
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    ctx.font = text.length > 3 ? 'bold 18px Noto Sans, sans-serif'
        : text.length > 2 ? 'bold 20px Noto Sans, sans-serif'
            : 'bold 24px Noto Sans, sans-serif';
    ctx.fillText(text, center, center);

    return ctx.getImageData(0, 0, 42, 42).data;
}

// ═══════════════════════════════════════════════════════════════════
// HELPERS
// ═══════════════════════════════════════════════════════════════════

function _pillClass(t) {
    if (t === 0 || t === 11) return 'tram';
    if (t === 1) return 'metro';
    if (t === 2) return 'rail';
    if (t === 4) return 'ferry';
    return 'bus';
}

function darkenColor(hex, factor = 0.8) {
    const r = Math.floor(parseInt(hex.slice(1, 3), 16) * factor);
    const g = Math.floor(parseInt(hex.slice(3, 5), 16) * factor);
    const b = Math.floor(parseInt(hex.slice(5, 7), 16) * factor);
    return `rgb(${r},${g},${b})`;
}

function _esc(s) {
    return (s || '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}

// ═══════════════════════════════════════════════════════════════════
// PANEL CLOSE BUTTONS + SWIPE TO CLOSE
// ═══════════════════════════════════════════════════════════════════

document.getElementById('sheet-close').addEventListener('click', e => {
    e.stopPropagation(); _closeStopSheet();
});
document.getElementById('trip-panel-close').addEventListener('click', e => {
    e.stopPropagation(); _closeTripPanel();
});
document.getElementById('bike-sheet-close').addEventListener('click', e => {
    e.stopPropagation(); _closeBikeSheet();
});

[_sheet, _tripPanel, _bikeSheet].forEach(p => {
    p.addEventListener('pointerdown', e => e.stopPropagation());
    p.addEventListener('click', e => e.stopPropagation());
});

function _addSwipeClose(panel, closeFn) {
    let startY = 0, dragging = false;
    panel.addEventListener('pointerdown', e => { startY = e.clientY; dragging = true; });
    panel.addEventListener('pointermove', e => {
        if (!dragging) return;
        if (e.clientY - startY > 60) { dragging = false; closeFn(); }
    });
    panel.addEventListener('pointerup', () => { dragging = false; });
}

_addSwipeClose(_sheet, _closeStopSheet);
_addSwipeClose(_tripPanel, _closeTripPanel);
_addSwipeClose(_bikeSheet, _closeBikeSheet);