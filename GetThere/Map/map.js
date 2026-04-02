// ═══════════════════════════════════════════════════════════════════
// CONFIG
// ═══════════════════════════════════════════════════════════════════

const STOPS_MIN_ZOOM = 14;   // stop icons visible at this zoom and above

// ═══════════════════════════════════════════════════════════════════
// BOOT — init map using the injected style (window._MAP_STYLE)
// Style is injected by C# as a <script> block before this file loads.
// We set _mapReady on 'style.load' (fires when base style tiles are
// ready) rather than 'load' (which can stall waiting for all tiles).
// ═══════════════════════════════════════════════════════════════════

window.map = new maplibregl.Map({
    container: 'map',
    style: window._MAP_STYLE,
    center: [15.9775, 45.8129],
    zoom: 13,
    minZoom: 10,
    maxPitch: 60
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
// MAP LOAD — set up all sources and layers
// ═══════════════════════════════════════════════════════════════════
async function _onMapLoad() {

    // ── Stop source + layers ───────────────────────────────────────
    map.addSource('gtfs-stops', {
        type: 'geojson',
        data: { type: 'FeatureCollection', features: [] }
    });

    // Load stop icons. Called without await so _mapReady is never
    // blocked by a network call. Icons register themselves and then
    // MapLibre re-renders the stops layer automatically.
    _loadStopIcons();

    // Single stops layer, no clustering, visible at all zoom levels ≥ 14
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

    // Vehicle icons — exact zetRt approach.
    // Key format: "{routeShortName}-{routeType}-{realTime}-bg" or "-fg"
    // bg = directional arrow (64x64), fg = coloured circle + route number (42x42)
    map.on('styleimagemissing', async e => {
        const id = e.id;
        if (!id.endsWith('-bg') && !id.endsWith('-fg')) return;

        const suffix = id.endsWith('-bg') ? 'bg' : 'fg';
        const base = id.slice(0, -(suffix.length + 1)); // strip "-bg"/"-fg"
        const parts = base.split('-');
        // parts = [routeShortName, routeType, realTime]
        // routeShortName can contain digits only so split is safe
        const realTime = parts[parts.length - 1] === 'true';
        const routeType = parseInt(parts[parts.length - 2], 10);
        const routeShortName = parts.slice(0, parts.length - 2).join('-');

        let color;
        if (routeType === 0) {
            color = realTime ? '#1264AB' : '#535353'; // tram blue / grey
        } else {
            color = realTime ? '#126400' : '#727272'; // bus green / grey
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

    // bg layer: directional arrow, rotates with vehicle bearing
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

    // fg layer: coloured circle with route number, no rotation
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

    // Vehicle detail labels at zoom 15+ (VR + vehicle number, same as zetRt)
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

    // Stops sit below vehicle bg layer
    map.moveLayer('stops', 'vehicles-bg');

    // ── Active route line ─────────────────────────────────────────
    map.addSource('active-route', {
        type: 'geojson',
        data: { type: 'FeatureCollection', features: [] }
    });
    map.addLayer({
        id: 'active-route-outline', type: 'line', source: 'active-route',
        layout: { 'line-join': 'round', 'line-cap': 'round' },
        paint: { 'line-color': '#000', 'line-width': 9, 'line-opacity': 0.08 }
    });
    map.addLayer({
        id: 'active-route', type: 'line', source: 'active-route',
        layout: { 'line-join': 'round', 'line-cap': 'round' },
        paint: { 'line-color': '#1565c0', 'line-width': 5, 'line-opacity': 0.92 }
    });

    // ── Click events ───────────────────────────────────────────────
    // Stop click → request schedule from C#
    map.on('click', 'stops', e => {
        if (e.defaultPrevented) return;
        e.preventDefault();
        const p = e.features[0].properties;
        _openStopSheet(p.stopId, p.name,
            typeof p.routeType === 'number' ? p.routeType : parseInt(p.routeType) || 3);
    });

    // Vehicle click → request trip detail from C#
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

    // Empty tap → close panels
    map.on('click', e => {
        if (e.defaultPrevented) return;
        _clearRoute();
        _closeStopSheet();
        _closeTripPanel();
    });

    // Signal C# that the map is ready for data
    window._mapReady = true;
}

// ═══════════════════════════════════════════════════════════════════
// PUBLIC FUNCTIONS — called by C# via CallJsAsync
// ═══════════════════════════════════════════════════════════════════

// ── renderStops ────────────────────────────────────────────────────
// Called once on startup with all stops from the API.
function renderStops(stops) {
    const features = (stops || []).map(s => ({
        type: 'Feature',
        geometry: { type: 'Point', coordinates: [s.lon, s.lat] },
        properties: {
            stopId: s.stopId,
            name: s.name,
            routeType: s.routeType ?? 3,
            stopCategory: _stopCategory(s.routeType ?? 3),
        }
    }));

    map.getSource('gtfs-stops')?.setData({
        type: 'FeatureCollection', features
    });
}

// ── renderRoutes ───────────────────────────────────────────────────
// Stores route metadata (name, type, color, shape) for later use.
function renderRoutes(routes) {
    _routeMap = {};
    (routes || []).forEach(r => {
        _routeMap[r.routeId] = r;
    });
}

// ── renderVehicles ─────────────────────────────────────────────────
// Called every 10 seconds with live vehicle positions.
function renderVehicles(vehicles) {
    const features = (vehicles || []).map((v, i) => {
        const route = _routeMap[v.routeId] || {};
        return {
            type: 'Feature',
            geometry: { type: 'Point', coordinates: [v.lon, v.lat] },
            properties: {
                idx: i,
                vehicleId: v.vehicleId || '',
                tripId: v.tripId || '',
                routeId: v.routeId || '',
                routeShortName: v.routeShortName || route.shortName || '?',
                routeType: v.routeType ?? route.routeType ?? 3,
                isRealtime: v.isRealtime ?? false,
                bearing: v.bearing ?? 0,
                blockId: v.blockId || '',
            }
        };
    });

    map.getSource('vehicles')?.setData({
        type: 'FeatureCollection', features
    });
}

// ── renderStopSchedule ─────────────────────────────────────────────
// Called by C# after user taps a stop.
function renderStopSchedule(data) {
    if (typeof data === 'string') data = JSON.parse(data);

    _sheetLoad.style.display = 'none';
    _sheetBody.innerHTML = '';

    const groups = data.groups || [];
    if (!groups.length) { _sheetEmpty.style.display = 'block'; return; }

    groups.forEach((g, gi) => {
        const route = _routeMap[g.routeId] || {};
        const rType = route.routeType ?? 3;
        const pc = _pillClass(rType);
        const deps = g.departures || [];

        const timesHtml = deps.map((d, i) => {
            let cls = 'time-chip' + (i === 0 ? ' next' : '');
            const canClick = d.isRealtime || !!d.estimatedTime;
            if (canClick) cls += ' click';
            const onClick = canClick
                ? `onclick="_openTripPanel('${d.tripId}')"` : '';

            const hasEta = d.estimatedTime && d.estimatedTime !== d.scheduledTime;
            if (hasEta) {
                const delay = d.delayMinutes ?? 0;
                const badge = delay > 1
                    ? `<span class="delay-badge late">+${delay}'</span>`
                    : delay < -1
                        ? `<span class="delay-badge early">${delay}'</span>`
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
        const rtBadge = tracked > 0
            ? `&nbsp;<span class="tracked-count">📡&nbsp;${tracked}</span>` : '';

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

// ── renderTripDetail ───────────────────────────────────────────────
// Called by C# after user taps a vehicle.
function renderTripDetail(data) {
    if (typeof data === 'string') data = JSON.parse(data);

    _tripLoading.style.display = 'none';

    const rType = data.routeType ?? 3;
    _tripPill.className = 'route-pill ' + _pillClass(rType);
    _tripPill.textContent = data.shortName || data.routeId || '?';
    _tripHead.textContent = data.headsign || '';

    const status = data.isRealtime
        ? '📡 Live'
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
        let times = '';
        if (hasEta) {
            const d = s.delayMinutes ?? 0;
            const ec = d > 1 ? 'late' : d < -1 ? 'early' : 'ontime';
            times = `<div class="trip-stop-times">
                        <span class="ts-sched">${s.scheduledTime}</span>
                        <span class="ts-eta ${ec}">${s.estimatedTime}</span>
                    </div>`;
        } else {
            times = `<div class="trip-stop-times">
                        <span class="ts-only">${s.scheduledTime}</span>
                    </div>`;
        }

        const row = document.createElement('div');
        row.className = cls;
        row.innerHTML = `
                    <div class="trip-stop-dot"></div>
                    <div class="trip-stop-info">
                        <div class="trip-stop-name">${_esc(s.stopName)}</div>
                    </div>
                    ${times}`;
        _tripBody.appendChild(row);
    });

    // Fly to vehicle if tracked
    if (data.isRealtime && data.vehicleLat && data.vehicleLon)
        map.flyTo({ center: [data.vehicleLon, data.vehicleLat], zoom: 15, duration: 600 });

    // Draw full route shape
    if (data.routeId) _showRoute(data.routeId);

    requestAnimationFrame(() => {
        const cur = _tripBody.querySelector('.trip-stop.current');
        if (cur) cur.scrollIntoView({ block: 'center' });
    });
}

// ── updateMapLocation ──────────────────────────────────────────────
// Called by C# when GPS location is available.
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

let _routeMap = {};
let _userMarker = null;
let _currentStop = null;

// Panel element refs
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

// ═══════════════════════════════════════════════════════════════════
// PANELS
// ═══════════════════════════════════════════════════════════════════

function _openStopSheet(stopId, stopName, routeType) {
    _tripPanel.classList.remove('open');
    _currentStop = stopId;

    _sheetName.textContent = stopName;
    const isTram  = routeType === 0 || routeType === 11;
    const isTrain = routeType === 2;
    _sheetSid.textContent = (isTrain ? '🚂 ' : isTram ? '🚋 ' : '🚌 ') + '#' + stopId;

    const now = new Date();
    const days = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
    const months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
    _sheetDate.textContent = `${days[now.getDay()]}, ${now.getDate()} ${months[now.getMonth()]} ${now.getFullYear()}`;

    _sheetLoad.style.display = 'flex';
    _sheetEmpty.style.display = 'none';
    _sheetBody.innerHTML = '';
    _sheet.classList.add('open');

    // Tell C# to fetch the schedule
    window._pendingMsg = 'stopSchedule:' + stopId;
}

function _closeStopSheet() {
    _sheet.classList.remove('open');
    _currentStop = null;
}

function _openTripPanel(tripId) {
    _sheet.classList.remove('open');
    _currentStop = null;

    _tripPill.className = 'route-pill bus';
    _tripPill.textContent = '…';
    _tripHead.textContent = 'Loading…';
    _tripSubtitle.textContent = '';
    _tripLoading.style.display = 'flex';
    _tripBody.innerHTML = '';
    _tripPanel.classList.add('open');

    // Tell C# to fetch the trip detail
    window._pendingMsg = 'tripDetail:' + tripId;
}

function _closeTripPanel() {
    _tripPanel.classList.remove('open');
    _clearRoute();
}

// ═══════════════════════════════════════════════════════════════════
// ROUTE LINE
// ═══════════════════════════════════════════════════════════════════

// Draw the full route shape for the given routeId in the route's colour.
function _showRoute(routeId) {
    const route = _routeMap[routeId];
    const shape = route?.shape;
    const color = route?.color ? '#' + route.color : '#1565c0';

    map.getSource('active-route')?.setData(
        shape && shape.length >= 2
            ? { type: 'Feature', geometry: { type: 'LineString', coordinates: shape } }
            : { type: 'FeatureCollection', features: [] }
    );

    if (map.getLayer('active-route'))
        map.setPaintProperty('active-route', 'line-color', color);
}

function _clearRoute() {
    map.getSource('active-route')?.setData(
        { type: 'FeatureCollection', features: [] }
    );
}

// ═══════════════════════════════════════════════════════════════════
// STOP ICON LOADER
//
// Icons are served by the API from GET /operator/images/{filename}.
// To add a new stop type: drop a PNG in GetThereAPI/wwwroot/images/
// then add one entry to STOP_ICON_MAP — no other changes needed.
//
// GTFS routeType numbers:
//   0  = tram / light-rail   → tram.png
//   3  = bus                 → bus.png
//   11 = trolleybus          → tram.png  (reuses tram icon)
//   2  = rail/train          → add rail.png + uncomment below
//   4  = ferry               → add ferry.png + uncomment below
// ═══════════════════════════════════════════════════════════════════

const STOP_ICON_MAP = Object.fromEntries(
    (window._TRANSPORT_TYPES || []).map(t => [
        t.gtfsRouteType,
        { id: 'stop-' + t.iconFile.replace('.png', ''), file: t.iconFile, color: t.color }
    ])
);

// Default fallback: prefer bus (routeType 3), otherwise first available type
const _defaultEntry = STOP_ICON_MAP[3] || Object.values(STOP_ICON_MAP)[0] || { id: 'stop-bus', color: '#126400' };
const _defaultIconId = _defaultEntry.id;
const _defaultColor = _defaultEntry.color;

function _buildIconExpression() {
    const nonDefault = Object.entries(STOP_ICON_MAP).filter(([, cfg]) => cfg.id !== _defaultIconId);
    if (!nonDefault.length) return _defaultIconId;
    const expr = ['case'];
    for (const [type, cfg] of nonDefault) {
        expr.push(['==', ['get', 'routeType'], parseInt(type)], cfg.id);
    }
    expr.push(_defaultIconId);
    return expr;
}

function _buildColorExpression() {
    const nonDefault = Object.entries(STOP_ICON_MAP).filter(([, cfg]) => cfg.color !== _defaultColor);
    if (!nonDefault.length) return _defaultColor;
    const expr = ['case'];
    for (const [type, cfg] of nonDefault) {
        expr.push(['==', ['get', 'routeType'], parseInt(type)], cfg.color);
    }
    expr.push(_defaultColor);
    return expr;
}

function _loadStopIcons() {
    const unique = Object.values(
        Object.values(STOP_ICON_MAP).reduce((acc, e) => { acc[e.id] = e; return acc; }, {})
    );
    return Promise.all(unique.map(({ id, file }) =>
        new Promise(resolve => {
            // C# pre-fetches icons and injects them as base64 into window._ICON_DATA
            // to avoid CORS issues. Fall back to direct URL if not present.
            const dataUri = window._ICON_DATA && window._ICON_DATA[file];
            const url = dataUri || (window._API_BASE || '') + '/images/' + file;
            console.log('[StopIcons] Loading:', file, dataUri ? '(base64)' : '(url)');
            const img = new Image();
            img.onload = () => {
                try {
                    const canvas = document.createElement('canvas');
                    canvas.width = img.width;
                    canvas.height = img.height;
                    canvas.getContext('2d').drawImage(img, 0, 0);
                    const imageData = canvas.getContext('2d').getImageData(0, 0, img.width, img.height);
                    if (!map.hasImage(id)) {
                        map.addImage(id, imageData);
                        console.log('[StopIcons] Added:', id, img.width, img.height);
                    }
                } catch (e) {
                    console.error('[StopIcons] addImage failed:', id, e);
                }
                resolve();
            };
            img.onerror = (e) => {
                console.error('[StopIcons] FAILED to load:', file, e);
                resolve();
            };
            if (!dataUri) img.crossOrigin = 'anonymous';
            img.src = url;
        })
    ));
}

// ═══════════════════════════════════════════════════════════════════
// VEHICLE ICON GENERATORS — exact zetRt code
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
    ctx.font = text.length > 3
        ? 'bold 18px Noto Sans, sans-serif'
        : text.length > 2
            ? 'bold 20px Noto Sans, sans-serif'
            : 'bold 24px Noto Sans, sans-serif';
    ctx.fillText(text, center, center);

    return ctx.getImageData(0, 0, 42, 42).data;
}

// ═══════════════════════════════════════════════════════════════════
// HELPERS
// ═══════════════════════════════════════════════════════════════════

function _stopCategory(t) {
    if (t === 0 || t === 11) return 'tram';
    if (t === 1) return 'metro';
    if (t === 2) return 'rail';
    if (t === 4) return 'ferry';
    return 'bus';
}

function _pillClass(t) {
    if (t === 0 || t === 11) return 'tram';
    if (t === 1) return 'metro';
    if (t === 2) return 'rail';
    if (t === 4) return 'ferry';
    return 'bus';
}

// darkenColor — exact zetRt implementation
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

// Prevent map clicks from firing when touching panels
[_sheet, _tripPanel].forEach(p => {
    p.addEventListener('pointerdown', e => e.stopPropagation());
    p.addEventListener('click', e => e.stopPropagation());
});

// Swipe down to close
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
