// Null prototype on purpose, and it is the fix rather than a flourish.
//
// On an ordinary object literal a feed-supplied route type of "constructor" or "toString" reaches
// Object.prototype and comes back as a *function*, which is truthy — so `ROUTE_COLORS[type] ||
// default` returned that function and it stringified into the style attribute as CSS. Not
// injectable, since the result cannot contain a quote, but it renders a broken badge.
//
// Guarding the lookup inside getRouteColor only fixed the call sites that went through
// getRouteColor; reconciliation.page.js indexes this table directly in two places and kept the bug.
// Taking the prototype off the table fixes every reader at once, including ones not written yet,
// and it needs nothing newer than Object.create — unlike Object.hasOwn, which is ES2022 and would
// have thrown on a browser old enough to predate it while still being new enough for the map.
const ROUTE_COLORS = Object.assign(Object.create(null), {
  Tram: '#1f78b4',
  Subway: '#e31a1c',
  Train: '#b15928',
  Bus: '#126400',
  Ferry: '#6a3d9a',
  CableTram: '#fb9a99',
  CableCar: '#fb9a99',
  Funicular: '#fdbf6f',
  Trolleybus: '#33a02c',
  Monorail: '#cab2d6',
  Bicycle: '#a6cee3',
  Scooter: '#ff7f00',
  Airplane: '#b2df8a',
  default: '#888'
});

function rtBadge(type) {
  if (!type) return '';
  const color = getRouteColor(type);
  return `<span class="rt-badge" style="background:${color}">${formatEnumName(type)}</span>`;
}

function getRouteColor(type) {
  // Safe as a bare index because ROUTE_COLORS inherits nothing — see the note on the table.
  return ROUTE_COLORS[type] || ROUTE_COLORS.default;
}

function formatEnumName(name) {
  if (!name) return '';
  return name.replace(/([a-z])([A-Z])/g, '$1 $2');
}
