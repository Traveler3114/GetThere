const ROUTE_COLORS = {
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
};

function rtBadge(type) {
  if (!type) return '';
  const color = getRouteColor(type);
  return `<span class="rt-badge" style="background:${color}">${formatEnumName(type)}</span>`;
}

function getRouteColor(type) {
  // hasOwn, not a bare index: ROUTE_COLORS inherits from Object.prototype, so a feed-supplied
  // route type of "constructor" or "toString" returned a *function* instead of falling through
  // to the default colour, and that function stringified into the style attribute as CSS.
  // Not injectable — the result cannot contain a quote — but it renders a broken badge.
  return Object.hasOwn(ROUTE_COLORS, type) ? ROUTE_COLORS[type] : ROUTE_COLORS.default;
}

function formatEnumName(name) {
  if (!name) return '';
  return name.replace(/([a-z])([A-Z])/g, '$1 $2');
}
