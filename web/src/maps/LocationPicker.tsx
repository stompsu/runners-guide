import { useState } from 'react'
import { apiClient } from '../api/client'
import { en } from '../i18n'
import type { Location } from './contracts'
import { MapView } from './MapView'

export function LocationPicker({ value, onChange }: { value: Location | null; onChange: (value: Location | null) => void }) {
  const [query, setQuery] = useState('')
  const [results, setResults] = useState<Location[]>([])
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  return <section className="location-picker">
    <h3>{en.maps.location}</h3>
    <label>{en.maps.searchLocation}<input value={query} maxLength={200} onChange={event => setQuery(event.target.value)} /></label>
    <button type="button" disabled={busy || !query.trim()} onClick={async () => {
      setBusy(true); setError('')
      try {
        const { data } = await apiClient.GET('/api/maps/geocode', { params: { query: { query } } })
        if (!data) throw new Error()
        setResults(data)
        if (!data.length) setError(en.maps.noResults)
      } catch { setError(en.maps.searchFailed) }
      finally { setBusy(false) }
    }}>{en.maps.search}</button>
    {error && <p role="status">{error}</p>}
    <ul>{results.map((result, i) => <li key={i}><button type="button" onClick={() => {
      onChange(result); setResults([])
    }}>{result.displayName}</button></li>)}</ul>
    {value && <p>{value.displayName} <button type="button" onClick={() => onChange(null)}>{en.maps.clear}</button></p>}
    <MapView location={value} onPick={onChange} />
  </section>
}
