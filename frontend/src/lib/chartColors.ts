// Colors validated by the dataviz skill's colorblind-safety checks (worst
// adjacent CVD Delta E >= 8, normal-vision floor >= 15 — see references/palette.md).
// Kept in sync by hand with the `--primary`/`--chart-1..5` tokens in index.css;
// Chart.js/MapLibre can't consume CSS custom properties directly, so this is
// the actual source of truth for anything rendered on a <canvas> or as a
// MapLibre paint expression. Update both places together if this ever changes.

// Fixed order — never cycle or re-sort. A 9th series should fold into "Other"
// rather than generating a new hue.
export const CATEGORICAL_COLORS = [
  '#2a78d6', // blue
  '#eb6834', // orange
  '#1baf7a', // aqua
  '#eda100', // yellow
  '#e87ba4', // magenta
  '#008300', // green
  '#4a3aa7', // violet
  '#e34948', // red
] as const

// Single-hue blue ramp, light -> dark, for magnitude/choropleth encoding.
export const SEQUENTIAL_BLUE = {
  100: '#cde2fb',
  200: '#9ec5f4',
  300: '#6da7ec',
  400: '#3987e5',
  500: '#256abf',
  600: '#184f95',
  700: '#0d366b',
} as const

// Diverging blue<->red pair with a neutral gray midpoint. Blue/red (not
// red/green) is the deliberate choice here — red/green is the one combination
// both major forms of color blindness confuse, so a "below average / above
// average" map needs poles that read as opposite to everyone.
export const DIVERGING_NEGATIVE = '#e34948' // red — notably below average
export const DIVERGING_NEUTRAL = '#f0efec' // gray — near the average
export const DIVERGING_POSITIVE = '#2a78d6' // blue — notably above average
