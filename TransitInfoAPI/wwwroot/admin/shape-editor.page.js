const params = new URLSearchParams(window.location.search);
const routeId = params.get('routeId');

if (!routeId) {
  document.getElementById('route-name').textContent = 'No route specified';
  document.getElementById('save-btn').disabled = true;
}

const map = new maplibregl.Map({
  container: 'map',
  style: '/map/style.json',
  center: [16.0, 45.8],
  zoom: 12
});

map.addControl(new maplibregl.NavigationControl());
map.addControl(new maplibregl.ScaleControl({ unit: 'metric' }));

let draw = null;
let currentShapeId = null;
let stopMarkers = [];
let originalGeometry = null;
let shapeLoaded = false;

function showToast(msg, type) {
  const el = document.getElementById('toast');
  el.textContent = msg;
  el.className = 'toast ' + type;
  el.style.display = 'block';
  setTimeout(() => { el.style.display = 'none'; }, 3000);
}

function esc(s) {
  // Escapes quotes as well as angle brackets — the result is interpolated into HTML attributes,
  // where a textContent round-trip would leave " and ' intact and allow attribute injection.
  if (s === null || s === undefined) return '';
  return String(s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
}

function cancelEdit() {
  // Return to the admin editing map the operator picked this route from, not the public planner.
  window.location.href = '/admin/map.html';
}

function addStopMarkers(stops) {
  stops.forEach(s => {
    // != null, not truthiness: longitude 0 is the Greenwich meridian, not a missing coordinate.
    if (s.latitude == null || s.longitude == null) return;
    const el = document.createElement('div');
    el.style.cssText = 'width:14px;height:14px;background:#27ae60;border:2px solid #fff;border-radius:50%;box-shadow:0 1px 4px rgba(0,0,0,0.4);cursor:pointer;';
    const popup = new maplibregl.Popup({ offset: 10, closeButton: false })
      .setText(s.name || 'Stop');
    const marker = new maplibregl.Marker({ element: el })
      .setLngLat([s.longitude, s.latitude])
      .setPopup(popup)
      .addTo(map);
    marker.getElement().addEventListener('mouseenter', () => marker.togglePopup());
    marker.getElement().addEventListener('mouseleave', () => marker.togglePopup());
    stopMarkers.push(marker);
  });
}

function getDrawGeoJSON() {
  if (!draw) return null;
  const data = draw.getAll();
  if (!data || !data.features || data.features.length === 0) return null;
  const feature = data.features[0];
  if (!feature || !feature.geometry || feature.geometry.type !== 'LineString') return null;
  return feature.geometry;
}

async function saveShape() {
  const geometry = getDrawGeoJSON();
  if (!geometry) {
    showToast('No shape to save. Edit the line first.', 'error');
    return;
  }

  const btn = document.getElementById('save-btn');
  btn.disabled = true;
  btn.textContent = 'Saving...';

  try {
    const resp = await fetch('/routes/' + routeId + '/shape', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(geometry)
    });

    if (!resp.ok) {
      const err = await resp.json().catch(() => ({ error: 'HTTP ' + resp.status }));
      throw new Error(err.error || 'Save failed');
    }

    showToast('Shape saved successfully', 'success');
    originalGeometry = JSON.parse(JSON.stringify(geometry));
  } catch (err) {
    showToast('Error: ' + err.message, 'error');
  } finally {
    btn.disabled = false;
    btn.textContent = 'Save';
  }
}

function fitMapToShape(geometry) {
  if (!geometry || !geometry.coordinates) return;
  const bounds = new maplibregl.LngLatBounds();
  geometry.coordinates.forEach(c => bounds.extend(c));
  map.fitBounds(bounds, { padding: 80, maxZoom: 17 });
}

// Reverts the drawing to the last shape the server confirmed — the one fetched on load, or the one
// written back after the most recent successful save.
//
// It used to say "Reset to auto-generated shape" and call location.reload(), which promised
// something it could not do. The reload re-fetches GET /routes/{id}/shape — the *saved* shape — so
// after a save it reloaded the very manual shape it claimed to be replacing. The auto-generated
// geometry is not recoverable at that point: PUT overwrote it, and AutoGenerateShapesIfMissing lives
// inside FeedManager's import path with no endpoint in front of it, since RoutesController exposes
// only GET and PUT for a shape. A true "reset to auto" still needs a regenerate endpoint.
//
// Reverting in place also keeps the route name, badge and stop markers already on the map instead of
// re-fetching all three, and `originalGeometry` is deep-copied on the way out so MapboxDraw editing
// the restored line cannot mutate the copy this reads next time.
function resetShape() {
  if (!originalGeometry) {
    showToast('Nothing to revert to — no shape was loaded for this route.', 'error');
    return;
  }

  if (!confirm('Revert to the last saved shape? Your unsaved edits will be discarded.')) return;

  const geometry = JSON.parse(JSON.stringify(originalGeometry));
  initDraw(geometry);
  fitMapToShape(geometry);
  showToast('Reverted to the last saved shape', 'success');
}

function initDraw(geometry) {
  if (draw) {
    draw.deleteAll();
    draw.set({
      type: 'FeatureCollection',
      features: [{
        type: 'Feature',
        geometry: geometry,
        properties: {}
      }]
    });
    return;
  }

  draw = new MapboxDraw({
    displayControlsDefault: false,
    controls: {},
    styles: [
      {
        id: 'gl-draw-line',
        type: 'line',
        filter: ['all', ['==', '$type', 'LineString'], ['!=', 'mode', 'static']],
        layout: { 'line-cap': 'round', 'line-join': 'round' },
        paint: {
          'line-color': '#1f78b4',
          'line-width': 3,
          'line-opacity': 0.9
        }
      },
      {
        id: 'gl-draw-polygon-and-line-fill',
        type: 'fill',
        filter: ['all', ['==', '$type', 'Polygon'], ['!=', 'mode', 'static']],
        paint: { 'fill-color': '#1f78b4', 'fill-outline-color': '#1f78b4', 'fill-opacity': 0.1 }
      },
      {
        id: 'gl-draw-line-active',
        type: 'line',
        filter: ['all', ['==', '$type', 'LineString'], ['!=', 'mode', 'static']],
        paint: {
          'line-color': '#e31a1c',
          'line-width': 1.5,
          'line-dasharray': [2, 2]
        }
      },
      {
        id: 'gl-draw-polygon-and-line-fill-active',
        type: 'fill',
        filter: ['all', ['==', '$type', 'Polygon'], ['!=', 'mode', 'static']],
        paint: { 'fill-color': '#e31a1c', 'fill-outline-color': '#e31a1c', 'fill-opacity': 0.1 }
      },
      {
        id: 'gl-draw-point',
        type: 'circle',
        filter: ['all', ['==', '$type', 'Point'], ['!=', 'mode', 'static']],
        paint: {
          'circle-radius': 6,
          'circle-color': '#1f78b4',
          'circle-stroke-width': 2,
          'circle-stroke-color': '#fff'
        }
      },
      {
        id: 'gl-draw-point-active',
        type: 'circle',
        filter: ['all', ['==', '$type', 'Point'], ['!=', 'mode', 'static']],
        paint: {
          'circle-radius': 7,
          'circle-color': '#e31a1c',
          'circle-stroke-width': 2,
          'circle-stroke-color': '#fff'
        }
      },
      {
        id: 'gl-draw-polygon-and-line-fill-static',
        type: 'fill',
        filter: ['all', ['==', '$type', 'Polygon'], ['==', 'mode', 'static']],
        paint: { 'fill-color': '#000', 'fill-outline-color': '#000', 'fill-opacity': 0.1 }
      },
      {
        id: 'gl-draw-line-static',
        type: 'line',
        filter: ['all', ['==', '$type', 'LineString'], ['==', 'mode', 'static']],
        layout: { 'line-cap': 'round', 'line-join': 'round' },
        paint: { 'line-color': '#000', 'line-width': 3, 'line-opacity': 0.9 }
      }
    ]
  });
  map.addControl(draw);

  const ids = draw.set({
    type: 'FeatureCollection',
    features: [{
      type: 'Feature',
      geometry: geometry,
      properties: {}
    }]
  });

  if (ids && ids.length > 0) {
    draw.changeMode('direct_select', { featureId: ids[0] });
  }
}

map.on('load', async () => {
  if (!routeId) return;

  try {
    const [routeResp, shapeResp, stopsResp] = await Promise.all([
      fetch('/routes/' + routeId),
      fetch('/routes/' + routeId + '/shape'),
      fetch('/routes/' + routeId + '/stops')
    ]);

    if (!routeResp.ok) throw new Error('Route not found');

    const route = await routeResp.json();
    document.getElementById('route-name').textContent = route.name || route.shortName || 'Route #' + routeId;
    if (route.routeType) {
      const color = getRouteColor(route.routeType);
      document.getElementById('route-badge').innerHTML = `<span class="route-type-badge" style="background:${color}">${formatEnumName(route.routeType)}</span>`;
    }

    if (stopsResp.ok) {
      const stops = await stopsResp.json();
      addStopMarkers(stops);
    }

    if (shapeResp.ok) {
      const shapeData = await shapeResp.json();
      if (shapeData && shapeData.geometry && shapeData.geometry.type === 'LineString') {
        originalGeometry = JSON.parse(JSON.stringify(shapeData.geometry));
        initDraw(shapeData.geometry);
        fitMapToShape(shapeData.geometry);
        document.getElementById('save-btn').disabled = false;
        shapeLoaded = true;
      } else {
        showToast('Shape data has no valid LineString geometry', 'error');
      }
    } else {
      const errMsg = shapeResp.status === 404
        ? 'No shape generated yet for this route'
        : 'Failed to load shape (HTTP ' + shapeResp.status + ')';
      showToast(errMsg, 'error');
    }
  } catch (err) {
    document.getElementById('route-name').textContent = 'Error: ' + err.message;
  }
});
