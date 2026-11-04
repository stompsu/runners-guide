import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { apiClient } from '../api/client'
import { en } from '../i18n'
import type { Metrics } from './contracts'
import { MapView } from './MapView'
import { ElevationProfile } from './ElevationProfile'

export function TrackPanel({ runId, completed, onApply }: {
  runId: string; completed: boolean; onApply?: (metrics: Metrics) => void
}) {
  const client = useQueryClient()
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const [planned, setPlanned] = useState(!completed)
  const { data: track, error: loadError } = useQuery({
    queryKey: ['track', runId],
    queryFn: async () => {
      const result = await apiClient.GET('/api/runs/{id}/track', { params: { path: { id: runId } } })
      if (result.response.status === 404) return null
      if (!result.data) throw new Error(en.maps.loadFailed)
      return result.data
    },
  })
  return <section>
    <h3>{en.maps.track}</h3>
    <p>{en.maps.importHelp}</p>
    <label><input type="checkbox" checked={planned} onChange={e => setPlanned(e.target.checked)} />{en.maps.planned}</label>
    <label>{en.maps.upload}<input type="file" accept=".gpx,application/gpx+xml" disabled={busy}
      onChange={async event => {
        const file = event.target.files?.[0]
        event.target.value = ''
        if (!file) return
        if (file.size > 20 * 1024 * 1024) { setError(en.maps.tooLarge); return }
        setBusy(true); setError('')
        try {
          const result = await apiClient.POST('/api/runs/{id}/track', {
            params: { path: { id: runId }, query: { planned } },
            body: file, bodySerializer: body => body,
            headers: { 'Content-Type': 'application/gpx+xml' },
          })
          if (!result.data) throw new Error(result.response.status === 409
            ? en.maps.limit : en.maps.uploadFailed)
          client.setQueryData(['track', runId], result.data)
        } catch (e) { setError(e instanceof Error ? e.message : en.maps.uploadFailed) }
        finally { setBusy(false) }
      }} /></label>
    {busy && <p role="status">{en.maps.uploading}</p>}
    {error && <p role="alert">{error}</p>}
    {loadError && <p role="alert">{loadError.message}</p>}
    {track && <>
      <p>{track.isPlannedCourse ? en.maps.planned : en.maps.recorded} · {(track.features[0].properties.distanceMeters / 1000).toFixed(2)} km
        · ↑ {track.features[0].properties.elevationGainMeters.toFixed(0)} m
        · ↓ {track.features[0].properties.elevationLossMeters.toFixed(0)} m
        · {en.maps.duration}: {track.features[0].properties.durationSeconds == null ? en.maps.unavailableDuration : `${track.features[0].properties.durationSeconds} s`}</p>
      {completed && !track.isPlannedCourse && onApply && <button type="button"
        onClick={() => onApply(track.features[0].properties)}>{en.maps.apply}</button>}
      <MapView track={track} />
      <ElevationProfile track={track} />
      <button type="button" disabled={busy} onClick={async () => {
        setBusy(true); setError('')
        try {
          const result = await apiClient.GET('/api/runs/{id}/track/original', {
            params: { path: { id: runId } }, parseAs: 'blob',
          })
          if (!result.data) throw new Error()
          const url = URL.createObjectURL(result.data)
          const link = document.createElement('a')
          link.href = url; link.download = 'track.gpx'; link.click()
          setTimeout(() => URL.revokeObjectURL(url), 1000)
        } catch { setError(en.maps.downloadFailed) }
        finally { setBusy(false) }
      }}>{en.maps.download}</button>
      <button type="button" disabled={busy} onClick={async () => {
        if (!window.confirm(en.maps.deleteConfirm)) return
        setBusy(true); setError('')
        try {
          const result = await apiClient.DELETE('/api/runs/{id}/track', { params: { path: { id: runId } } })
          if (!result.response.ok) throw new Error()
          client.setQueryData(['track', runId], null)
        } catch { setError(en.maps.deleteFailed) }
        finally { setBusy(false) }
      }}>{en.maps.delete}</button>
    </>}
  </section>
}
