import { useEffect, useRef, useState } from 'react'
import { apiClient } from '../api/client'
import { en } from '../i18n'
import type { Location, Track } from './contracts'
import 'azure-maps-control/dist/atlas.min.css'

// Provider-specific SDK stays behind this boundary; callers use WGS84 and GeoJSON.
export function MapView({ track, location, onPick }: {
  track?: Track; location?: Location | null; onPick?: (location: Location) => void
}) {
  const element = useRef<HTMLDivElement>(null)
  const [error, setError] = useState('')
  useEffect(() => {
    const lifetime = { disposed: false, cleanup: () => {} }
    async function start() {
      const atlas = await import('azure-maps-control')
      const { data } = await apiClient.GET('/api/maps/token')
      if (!data) throw new Error(en.maps.authFailed)
      if (lifetime.disposed || !element.current) return
      const map = new atlas.Map(element.current, {
        center: location ? [location.longitude, location.latitude] : [18.6714, 50.2945],
        zoom: 12, authOptions: {
          authType: atlas.AuthenticationType.anonymous, clientId: data.clientId,
          getToken: (resolve, reject) => {
            // No app token, Maps token or subscription key is persisted.
            if (Date.parse(data.expiresAtUtc) > Date.now() + 60_000) {
              resolve(data.accessToken)
              return
            }
            apiClient.GET('/api/maps/token').then(result => {
              if (!result.data) reject(new Error(en.maps.authFailed))
              else resolve(result.data.accessToken)
            }).catch(reject)
          },
        },
      })
      lifetime.cleanup = () => map.dispose()
      map.events.add('ready', () => {
        if (lifetime.disposed) return
        map.controls.add(new atlas.control.ZoomControl(), { position: atlas.ControlPosition.TopRight })
        if (onPick) map.events.add('click', event => {
          if (event.position) onPick({
            displayName: `${event.position[1].toFixed(5)}, ${event.position[0].toFixed(5)}`,
            latitude: event.position[1], longitude: event.position[0],
          })
        })
        if (location) map.markers.add(new atlas.HtmlMarker({ position: [location.longitude, location.latitude] }))
        if (track) {
          const source = new atlas.source.DataSource()
          map.sources.add(source)
          // Construct typed SDK geometry instead of casting the generated API contract.
          for (const feature of track.features) source.add(new atlas.data.Feature(
            new atlas.data.MultiLineString(feature.geometry.coordinates.map(segment => segment.map(point => point.map(Number))))))
          map.layers.add(new atlas.layer.LineLayer(source, undefined, { strokeColor: '#087e8b', strokeWidth: 4 }))
          const points = track.features.flatMap(f => f.geometry.coordinates.flat()).map(point => point.map(Number))
          if (points.length) {
            map.setCamera({ bounds: atlas.data.BoundingBox.fromPositions(points), padding: 40 })
            map.markers.add([
              new atlas.HtmlMarker({ position: points[0], text: 'S', color: '#147d38' }),
              new atlas.HtmlMarker({ position: points[points.length - 1], text: 'F', color: '#b52424' }),
            ])
          }
        }
      })
      map.events.add('error', () => { if (!lifetime.disposed) setError(en.maps.failed) })
    }
    start().catch(() => { if (!lifetime.disposed) setError(en.maps.unavailable) })
    return () => { lifetime.disposed = true; lifetime.cleanup() }
  }, [track, location, onPick])
  return <section aria-label={en.maps.map}>
    <p>{en.maps.help}</p>
    <div ref={element} style={{ height: 320, width: '100%' }} />
    {error && <p role="status">{error}</p>}
  </section>
}
