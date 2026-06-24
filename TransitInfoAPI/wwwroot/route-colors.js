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
  const color = ROUTE_COLORS[type] || ROUTE_COLORS.default;
  return `<span class="rt-badge" style="background:${color}">${formatEnumName(type)}</span>`;
}

function getRouteColor(type) {
  return ROUTE_COLORS[type] || ROUTE_COLORS.default;
}

function formatEnumName(name) {
  if (!name) return '';
  return name.replace(/([a-z])([A-Z])/g, '$1 $2');
}
