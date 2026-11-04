import type { Track } from './contracts'
import { en } from '../i18n'

export function ElevationProfile({ track }: { track: Track }) {
  const points = track.elevation.filter(p => p.elevationMeters != null)
  if (!points.length) return <p>{en.maps.noElevation}</p>
  const low = points.reduce((min, p) => Math.min(min, Number(p.elevationMeters)), Infinity)
  const high = points.reduce((max, p) => Math.max(max, Number(p.elevationMeters)), -Infinity)
  const distance = Math.max(1, track.features[0].properties.distanceMeters)
  const path = points.map(p => `${10 + 580 * p.distanceMeters / distance},${140 - 120 * (Number(p.elevationMeters) - low) / Math.max(1, high - low)}`).join(' ')
  return <figure aria-label={en.maps.elevation}>
    <svg viewBox="0 0 600 160" role="img" aria-label={en.maps.profileLabel(low, high, distance)}>
      <polyline points={path} fill="none" stroke="currentColor" strokeWidth="2" />
    </svg>
    <figcaption>{low.toFixed(0)}–{high.toFixed(0)} m · {(distance / 1000).toFixed(2)} km</figcaption>
  </figure>
}
