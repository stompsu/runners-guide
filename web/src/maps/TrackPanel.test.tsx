import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { RunForm } from '../diary/RunForm'
import { TrackPanel } from './TrackPanel'
import { LocationPicker } from './LocationPicker'
import type { Run } from '../diary/calendar'
import type { Track } from './contracts'

const { fetchMock } = vi.hoisted(() => {
  const fetchMock = vi.fn()
  vi.stubGlobal('fetch', fetchMock)
  return { fetchMock }
})
vi.mock('./MapView', () => ({ MapView: () => <div aria-label="Run map" /> }))
const run: Run = {
  id: 'run-id', startAtUtc: '2026-11-04T08:00:00Z', title: 'Completed run',
  status: 'Completed', runTypeCode: 'easy-run', runTypeName: 'Easy run',
  distanceMeters: 1000, durationSeconds: 100, elevationGainMeters: 10, elevationLossMeters: 10,
  notes: null, averageHeartRateBpm: null, maximumHeartRateBpm: null, perceivedEffort: null,
  shoesOrGear: null, weather: null, surface: null, location: null, raceDetails: null, rating: null,
}
const preview: Track = {
  type: 'FeatureCollection', isPlannedCourse: false,
  features: [{ type: 'Feature', geometry: { type: 'MultiLineString', coordinates: [[[18, 50], [18.1, 50.1]]] },
    properties: { distanceMeters: 5000, durationSeconds: 1800, elevationGainMeters: 40,
      elevationLossMeters: 30, startLatitude: 50, startLongitude: 18 } }],
  elevation: [{ distanceMeters: 0, elevationMeters: 100 }, { distanceMeters: 5000, elevationMeters: 140 }],
}
let requests: Request[]
let track: Track | null
let failUpload: boolean
const json = (body: unknown) => new Response(JSON.stringify(body), { headers: { 'Content-Type': 'application/json' } })
beforeEach(() => {
  requests = []; track = null; failUpload = false
  fetchMock.mockReset()
  fetchMock.mockImplementation(async (request: Request) => {
    requests.push(request.clone())
    if (request.url.includes('/geocode')) return json([{ displayName: 'Gliwice', latitude: 50, longitude: 18 }])
    if (request.method === 'POST') {
      if (failUpload) return new Response(null, { status: 400 })
      track = { ...preview, isPlannedCourse: new URL(request.url).searchParams.get('planned') === 'true' }
      return json(track)
    }
    if (request.method === 'DELETE') { track = null; return new Response(null, { status: 204 }) }
    if (request.method === 'PUT') return json(run)
    return track ? json(track) : new Response(null, { status: 404 })
  })
})
afterEach(() => { cleanup(); vi.restoreAllMocks() })
function mount(node: React.ReactNode) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(<QueryClientProvider client={client}>{node}</QueryClientProvider>)
}
const file = () => new File(['<gpx>synthetic</gpx>'], 'demo.gpx', { type: 'application/gpx+xml' })
describe('M4 imports and location', () => {
  it('uploads raw GPX, previews, then applies completed-run suggestions only on explicit click and Save', async () => {
    const user = userEvent.setup()
    const saved = vi.fn()
    mount(<RunForm date="2026-11-04" run={run} onSaved={saved} onCancel={() => {}} />)
    await user.upload(screen.getByLabelText('Upload GPX (maximum 20 MiB)'), file())
    await screen.findByText(/Recorded track · 5.00 km/)
    expect(screen.getByLabelText('Distance (km)')).toHaveValue(1)
    expect(requests.filter(r => r.method === 'PUT')).toHaveLength(0)
    const upload = requests.find(r => r.method === 'POST')!
    expect(upload.headers.get('Content-Type')).toBe('application/gpx+xml')
    expect(await upload.text()).toBe('<gpx>synthetic</gpx>')
    await user.click(screen.getByRole('button', { name: 'Apply suggested metrics and start location' }))
    expect(screen.getByLabelText('Distance (km)')).toHaveValue(5)
    expect(requests.filter(r => r.method === 'PUT')).toHaveLength(0)
    await user.click(screen.getByRole('button', { name: 'Save run' }))
    await waitFor(() => expect(saved).toHaveBeenCalledOnce())
    const body = await requests.find(r => r.method === 'PUT')!.json()
    expect(body).toMatchObject({ distanceMeters: 5000, durationSeconds: 1800,
      elevationGainMeters: 40, elevationLossMeters: 30,
      location: { displayName: 'GPX start', latitude: 50, longitude: 18 } })
  })

  it('never offers planned course autofill, even on a completed run', async () => {
    const user = userEvent.setup()
    mount(<RunForm date="2026-11-04" run={run} onSaved={() => {}} onCancel={() => {}} />)
    await user.click(screen.getByLabelText('Planned course'))
    await user.upload(screen.getByLabelText('Upload GPX (maximum 20 MiB)'), file())
    await screen.findByText(/Planned course · 5.00 km/)
    expect(screen.queryByRole('button', { name: /Apply suggested/ })).not.toBeInTheDocument()
    expect(screen.getByLabelText('Distance (km)')).toHaveValue(1)
  })

  it('rejects oversized files locally and reports malformed import failures', async () => {
    const user = userEvent.setup()
    mount(<TrackPanel runId="run-id" completed />)
    const big = new File([new Uint8Array(20 * 1024 * 1024 + 1)], 'big.gpx', { type: 'application/gpx+xml' })
    await user.upload(screen.getByLabelText('Upload GPX (maximum 20 MiB)'), big)
    expect(screen.getByRole('alert')).toHaveTextContent('at most 20 MiB')
    expect(requests.some(r => r.method === 'POST')).toBe(false)
    failUpload = true
    await user.upload(screen.getByLabelText('Upload GPX (maximum 20 MiB)'), file())
    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('GPX upload failed'))
  })

  it('shows missing duration and deletes a track after confirmation', async () => {
    track = { ...preview, features: [{ ...preview.features[0],
      properties: { ...preview.features[0].properties, durationSeconds: null } }] }
    vi.spyOn(window, 'confirm').mockReturnValue(true)
    const user = userEvent.setup()
    mount(<TrackPanel runId="run-id" completed />)
    await screen.findByText(/Duration: unavailable/)
    await user.click(screen.getByRole('button', { name: 'Delete track' }))
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Delete track' })).not.toBeInTheDocument())
    expect(requests.some(r => r.method === 'DELETE')).toBe(true)
  })

  it('selects provider-neutral geocoding results without saving run fields', async () => {
    const user = userEvent.setup()
    const changed = vi.fn()
    mount(<LocationPicker value={null} onChange={changed} />)
    await user.type(screen.getByLabelText('Search location'), 'Gliwice')
    await user.click(screen.getByRole('button', { name: 'Search' }))
    await user.click(await screen.findByRole('button', { name: 'Gliwice' }))
    expect(changed).toHaveBeenCalledWith({ displayName: 'Gliwice', latitude: 50, longitude: 18 })
    expect(requests.every(r => r.method === 'GET')).toBe(true)
  })
})
