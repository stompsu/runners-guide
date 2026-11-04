import { act, cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, useLocation } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { Run, RunInput } from './diary/calendar'
import type { AuthenticationResult } from '@azure/msal-browser'

const { fetchMock, entra } = vi.hoisted(() => {
  const fetchMock = vi.fn()
  vi.stubGlobal('fetch', fetchMock)
  return {
    fetchMock,
    entra: {
      configured: false,
      instance: { loginPopup: vi.fn(), acquireTokenSilent: vi.fn(), clearCache: vi.fn() },
    },
  }
})
vi.mock('./auth/entra', () => ({
  get entraClient() { return entra.configured ? entra.instance : undefined },
  apiScope: 'api://test/diary',
}))
vi.mock('./maps/MapView', () => ({ MapView: () => <div aria-label="Run map" /> }))
vi.mock('@azure/msal-react', () => ({
  useMsal: () => {
    if (!entra.configured) throw new Error('MsalProvider is required')
    return { instance: entra.instance }
  },
}))
import App from './App'

function fixture(overrides: Partial<Run> = {}): Run {
  return {
    id: 'run-1', startAtUtc: '2026-11-04T08:00:00Z', title: 'Morning easy',
    status: 'Completed', runTypeCode: 'easy-run', runTypeName: 'Easy run',
    distanceMeters: 5000, durationSeconds: 1800, elevationGainMeters: 40,
    elevationLossMeters: null, notes: null, averageHeartRateBpm: null, maximumHeartRateBpm: null,
    perceivedEffort: null, shoesOrGear: null, weather: null, surface: null, location: null,
    raceDetails: null, rating: null, ...overrides,
  }
}
let runs: Run[]
let writes: { method: string; body?: RunInput }[]
let listStatus = 200
let guestStatus = 200
function Location() {
  return <output aria-label="Current URL">{useLocation().pathname}</output>
}
function mount(path = '/diary/2026/11') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  return render(<QueryClientProvider client={client}>
    <MemoryRouter initialEntries={[path]}><App /><Location /></MemoryRouter>
  </QueryClientProvider>)
}
const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
beforeEach(() => {
  runs = [fixture()]
  writes = []
  listStatus = 200
  guestStatus = 200
  entra.configured = false
  entra.instance.loginPopup.mockReset()
  entra.instance.acquireTokenSilent.mockReset()
  entra.instance.clearCache.mockReset()
  fetchMock.mockReset()
  fetchMock.mockImplementation(async (request: Request) => {
    const url = new URL(request.url)
    if (url.pathname.endsWith('/track')) return new Response(null, { status: 404 })
    if (url.pathname === '/api/auth/guest') return guestStatus === 200
      ? json({ accessToken: 'memory-only-demo', expiresAtUtc: new Date(Date.now() + 86400000).toISOString() })
      : new Response(null, { status: guestStatus })
    if (request.method === 'GET') return listStatus === 200 ? json(runs) : new Response(null, { status: listStatus })
    if (request.method === 'DELETE') {
      runs = runs.filter(run => !url.pathname.endsWith(run.id))
      writes.push({ method: 'DELETE' })
      return new Response(null, { status: 204 })
    }
    const body = await request.json() as RunInput
    writes.push({ method: request.method, body })
    const run = fixture({
      ...body, id: request.method === 'POST' ? 'new-run' : 'run-1',
      raceDetails: body.raceDetails as Run['raceDetails'],
      rating: body.rating ? { stars: body.rating.stars, comment: body.rating.comment ?? null } : null,
    })
    runs = request.method === 'POST' ? [...runs, run] : runs.map(old => old.id === run.id ? run : old)
    return json(run, request.method === 'POST' ? 201 : 200)
  })
})
afterEach(() => cleanup())
async function enter() {
  const user = userEvent.setup()
  await user.click(screen.getByRole('button', { name: 'Try as guest' }))
  await screen.findByRole('grid', { name: 'November 2026' })
  await waitFor(() => expect(screen.queryByText('Loading runs…')).not.toBeInTheDocument())
  return user
}
const day = (number: number) => screen.getByRole('button', {
  name: new RegExp(`^${['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'][new Date(Date.UTC(2026, 10, number)).getUTCDay()]}, ${number} November 2026,`),
})

describe('M3 calendar and session', () => {
  it('allows guest access without MSAL and attaches only an in-memory bearer token', async () => {
    mount()
    expect(screen.getByRole('heading', { name: 'Sign in to Runners Diary' })).toBeVisible()
    expect(fetchMock).not.toHaveBeenCalled()
    await enter()
    expect(screen.getByLabelText('Current URL')).toHaveTextContent('/diary/2026/11')
    expect(screen.getByRole('complementary', { name: 'Demo mode' })).toHaveTextContent('data is reset after 24 h')
    const requests = fetchMock.mock.calls.map(([request]) => request as Request)
    expect(requests[0].method).toBe('POST')
    expect(requests[0].headers.has('Authorization')).toBe(false)
    const list = requests.find(request => new URL(request.url).pathname === '/api/runs')!
    expect(list.headers.get('Authorization')).toBe('Bearer memory-only-demo')
    expect(new URL(list.url).searchParams.get('from')).toBe('2026-10-26')
    expect(new URL(list.url).searchParams.get('to')).toBe('2026-12-06')
    expect(localStorage.length).toBe(0)
    expect(sessionStorage.length).toBe(0)
  })

  it('uses Monday-first grid semantics, ISO week totals, and explicit planned/completed bubbles', async () => {
    runs = [
      fixture({ startAtUtc: '2026-10-31T08:00:00Z', distanceMeters: 10000, durationSeconds: 3600, elevationGainMeters: 100 }),
      fixture({ id: 'planned', startAtUtc: '2026-11-01T08:00:00Z', status: 'Planned', distanceMeters: 99000 }),
      fixture({ id: 'cross', startAtUtc: '2026-11-01T09:00:00Z', runTypeCode: 'cross-training', distanceMeters: 20000, durationSeconds: 1800, elevationGainMeters: 0 }),
    ]
    mount()
    await enter()
    const headers = screen.getAllByRole('columnheader')
    expect(headers[1]).toHaveTextContent('Monday')
    expect(headers[7]).toHaveTextContent('Sunday')
    const week = screen.getByRole('rowheader', { name: 'Week 44, 2026, completed totals' })
    expect(week).toHaveTextContent('10 km')
    expect(week).toHaveTextContent('1h 30m')
    expect(week).toHaveTextContent('100 m')
    expect(day(1).querySelector('.planned')).toHaveTextContent('Planned')
    expect(day(1).querySelector('.completed')).toHaveTextContent('Completed')
    const user = userEvent.setup()
    day(4).focus()
    await user.keyboard('{ArrowRight}')
    expect(day(5)).toHaveFocus()
    await user.keyboard('{Home}')
    expect(day(2)).toHaveFocus()
    await user.keyboard('{Enter}')
    const dialog = screen.getByRole('dialog')
    expect(dialog).toHaveAccessibleName('Monday, 2 November 2026')
    await user.keyboard('{Escape}')
    await waitFor(() => expect(day(2)).toHaveFocus())
  })

  it('navigates bookmarkable months, year and month controls, Today, and keyboard page navigation', async () => {
    mount()
    const user = await enter()
    await user.click(screen.getByRole('button', { name: 'Next month' }))
    expect(screen.getByLabelText('Current URL')).toHaveTextContent('/diary/2026/12')
    expect(screen.getByRole('button', { name: 'Next month' })).toHaveFocus()
    await user.click(screen.getByRole('button', { name: 'Previous month' }))
    await user.selectOptions(screen.getByLabelText('Month'), '1')
    expect(screen.getByLabelText('Current URL')).toHaveTextContent('/diary/2026/01')
    await user.clear(screen.getByLabelText('Year'))
    await user.type(screen.getByLabelText('Year'), '2027{Enter}')
    expect(screen.getByLabelText('Current URL')).toHaveTextContent('/diary/2027/01')
    await user.tab()
    expect(screen.getByRole('button', { name: 'Go to year' })).toHaveFocus()
    const january = await screen.findByRole('grid', { name: 'January 2027' })
    within(january).getByRole('button', { name: /^Friday, 1 January 2027,/ }).focus()
    await user.keyboard('{PageDown}')
    expect(screen.getByLabelText('Current URL')).toHaveTextContent('/diary/2027/02')
    await waitFor(() => expect(screen.getByRole('button', { name: /^Monday, 1 February 2027,/ })).toHaveFocus())
    await user.click(screen.getByRole('button', { name: 'Today' }))
    expect(screen.getByLabelText('Current URL')).toHaveTextContent(`/diary/${new Date().toISOString().slice(0, 7).replace('-', '/')}`)
  })

  it('adds a run on the selected date, converts to completed, and submits SI values and a star rating', async () => {
    mount()
    const user = await enter()
    await user.click(day(5))
    await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Add run' }))
    expect(screen.getByLabelText('Date (UTC)')).toHaveValue('2026-11-05')
    expect(screen.queryByRole('group', { name: 'Experience rating' })).not.toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Save run' }))
    expect(await screen.findByText('Title is required.')).toBeVisible()
    await user.type(screen.getByLabelText('Title'), 'Lunch run')
    await user.type(screen.getByLabelText('Distance (km)'), '7.5')
    await user.type(screen.getByLabelText('Duration (min)'), '45')
    await user.selectOptions(screen.getByLabelText('Status'), 'Completed')
    await user.click(screen.getByRole('radio', { name: '4 stars' }))
    await user.type(screen.getByLabelText('Experience comment'), 'Felt good')
    await user.click(screen.getByRole('button', { name: 'Save run' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(writes[0]).toMatchObject({ method: 'POST', body: {
      title: 'Lunch run', startAtUtc: '2026-11-05T08:00:00Z',
      status: 'Completed', distanceMeters: 7500, durationSeconds: 2700,
      rating: { stars: 4, comment: 'Felt good' },
    } })
    expect(day(5)).toHaveTextContent('7.5 km')
  })

  it('views details, edits without losing existing race/location data, and confirms deletion', async () => {
    runs = [fixture({
      runTypeCode: 'road-race', runTypeName: 'Road race', title: 'Autumn race',
      status: 'Planned', location: { displayName: 'Gliwice', latitude: 50, longitude: 18 },
      raceDetails: { raceName: 'Autumn 5K', bibNumber: '42', officialDistanceCategory: '5K',
        targetTimeSeconds: 1500, resultTimeSeconds: null, overallPlace: null, categoryPlace: null, resultsUrl: null },
    })]
    mount()
    const user = await enter()
    await user.click(day(4))
    await user.click(screen.getByRole('button', { name: 'View details' }))
    expect(screen.getByText('Gliwice')).toBeVisible()
    expect(screen.getByText('Autumn 5K')).toBeVisible()
    await user.click(screen.getByRole('button', { name: 'Edit run' }))
    await user.selectOptions(screen.getByLabelText('Status'), 'Completed')
    await user.click(screen.getByRole('radio', { name: '5 stars' }))
    await user.click(screen.getByRole('button', { name: 'Save run' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(writes[0].body).toMatchObject({
      status: 'Completed', location: { displayName: 'Gliwice' },
      raceDetails: { bibNumber: '42' }, rating: { stars: 5 },
    })
    await user.click(day(4))
    await user.click(screen.getByRole('button', { name: 'Edit' }))
    await user.selectOptions(screen.getByLabelText('Status'), 'Planned')
    expect(screen.queryByRole('radio')).not.toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Save run' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(writes[1].body?.rating).toBeNull()
    await user.click(day(4))
    await user.click(screen.getByRole('button', { name: 'Delete' }))
    expect(writes).toHaveLength(2)
    await user.click(screen.getByRole('button', { name: 'Cancel' }))
    expect(writes).toHaveLength(2)
    await user.click(screen.getByRole('button', { name: 'Delete' }))
    await user.click(screen.getByRole('button', { name: 'Confirm delete' }))
    expect(await screen.findByText('No runs in this period.')).toBeVisible()
    expect(writes.at(-1)?.method).toBe('DELETE')
  })

  it('handles loading errors, retries, and 401 by dropping the session and returning to sign-in', async () => {
    listStatus = 500
    mount()
    const user = await enter()
    expect(screen.getByRole('alert')).toHaveTextContent('Something went wrong')
    listStatus = 200
    await user.click(screen.getByRole('button', { name: 'Retry' }))
    await waitFor(() => expect(day(4)).toHaveTextContent('5 km'))
    listStatus = 401
    await user.click(screen.getByRole('button', { name: 'Next month' }))
    expect(await screen.findByRole('heading', { name: 'Sign in to Runners Diary' })).toBeVisible()
    expect(screen.getByRole('alert')).toHaveTextContent('Your session has expired')
    expect(screen.queryByRole('grid')).not.toBeInTheDocument()
  })

  it('shows guest failures without entering the diary and forgets the session on reload', async () => {
    guestStatus = 429
    const view = mount()
    const user = userEvent.setup()
    await user.click(screen.getByRole('button', { name: 'Try as guest' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Could not start the demo')
    guestStatus = 200
    await enter()
    view.unmount()
    mount()
    expect(screen.queryByRole('grid')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Try as guest' })).toBeVisible()
  })

  it('opens the month list and clears cached data on sign out', async () => {
    mount()
    const user = await enter()
    await user.click(screen.getByRole('button', { name: 'November 2026' }))
    expect(screen.getByRole('dialog')).toHaveAccessibleName('November 2026')
    expect(screen.getByRole('heading', { name: 'Morning easy' })).toBeVisible()
    await user.keyboard('{Escape}')
    await user.click(screen.getByRole('button', { name: 'Sign out' }))
    expect(screen.queryByRole('grid')).not.toBeInTheDocument()
    expect(screen.queryByRole('complementary')).not.toBeInTheDocument()
    // No browser persistence and no stale session after explicit expiry either.
    act(() => window.dispatchEvent(new Event('session-expired')))
    expect(screen.getByRole('alert')).toHaveTextContent('Your session has expired')
  })

  it('uses MSAL popup sign-in and silent API tokens, without a demo banner', async () => {
    entra.configured = true
    entra.instance.loginPopup.mockResolvedValue({
      accessToken: 'initial-entra-token', account: { homeAccountId: 'test-account' },
    } as AuthenticationResult)
    entra.instance.acquireTokenSilent.mockResolvedValue({ accessToken: 'fresh-entra-token' })
    mount()
    const user = userEvent.setup()
    await user.click(screen.getByRole('button', { name: 'Sign in' }))
    await screen.findByRole('grid', { name: 'November 2026' })
    await waitFor(() => expect(day(4)).toHaveTextContent('5 km'))
    expect(entra.instance.loginPopup).toHaveBeenCalledWith({ scopes: ['api://test/diary'] })
    expect(entra.instance.acquireTokenSilent).toHaveBeenCalledWith({
      account: { homeAccountId: 'test-account' }, scopes: ['api://test/diary'],
    })
    expect((fetchMock.mock.calls[0][0] as Request).headers.get('Authorization')).toBe('Bearer fresh-entra-token')
    expect(screen.queryByRole('complementary', { name: 'Demo mode' })).not.toBeInTheDocument()
    expect(entra.instance.clearCache).not.toHaveBeenCalled()
    await user.click(screen.getByRole('button', { name: 'Sign out' }))
    expect(entra.instance.clearCache).toHaveBeenCalled()
  })

  it('returns to sign-in when silent token acquisition fails', async () => {
    entra.configured = true
    entra.instance.loginPopup.mockResolvedValue({
      accessToken: 'entra-token', account: { homeAccountId: 'test-account' },
    } as AuthenticationResult)
    entra.instance.acquireTokenSilent.mockRejectedValue(new Error('interaction_required'))
    mount()
    await userEvent.setup().click(screen.getByRole('button', { name: 'Sign in' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Your session has expired')
    expect(screen.queryByRole('grid')).not.toBeInTheDocument()
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('keeps form data after server validation failure and allows retry', async () => {
    mount()
    const user = await enter()
    await user.click(day(5))
    await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Add run' }))
    await user.type(screen.getByLabelText('Title'), 'Keep my draft')
    fetchMock.mockResolvedValueOnce(json({ errors: { Title: ['A server validation message.'] } }, 400))
    await user.click(screen.getByRole('button', { name: 'Save run' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('A server validation message.')
    expect(screen.getByLabelText('Title')).toHaveValue('Keep my draft')
    await user.click(screen.getByRole('button', { name: 'Save run' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(writes.at(-1)?.body?.title).toBe('Keep my draft')
  })

  it('offers retry on network errors and shows a missing-run delete error', async () => {
    mount()
    const user = await enter()
    await user.click(day(4))
    await user.click(screen.getByRole('button', { name: 'Delete' }))
    fetchMock.mockResolvedValueOnce(new Response(null, { status: 404 }))
    await user.click(screen.getByRole('button', { name: 'Confirm delete' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('This run no longer exists')
    await user.keyboard('{Escape}')
    fetchMock.mockRejectedValueOnce(new Error('Network unavailable'))
    await user.click(screen.getByRole('button', { name: 'Next month' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Network unavailable')
    await user.click(screen.getByRole('button', { name: 'Retry' }))
    await waitFor(() => expect(screen.queryByRole('alert')).not.toBeInTheDocument())
  })

  it('marks loading as busy and prevents writes until data is available', async () => {
    let resolveList!: (response: Response) => void
    const original = fetchMock.getMockImplementation()!
    fetchMock.mockImplementation((request: Request) =>
      request.method === 'GET'
        ? new Promise<Response>(resolve => { resolveList = resolve })
        : original(request))
    mount()
    const user = userEvent.setup()
    await user.click(screen.getByRole('button', { name: 'Try as guest' }))
    expect(await screen.findByRole('grid')).toHaveAttribute('aria-busy', 'true')
    expect(screen.getByRole('button', { name: 'Add run' })).toBeDisabled()
    await waitFor(() => expect(resolveList).toBeTypeOf('function'))
    await act(async () => resolveList(json([])))
    await waitFor(() => expect(screen.getByRole('grid')).toHaveAttribute('aria-busy', 'false'))
    await user.click(screen.getByRole('button', { name: 'November 2026' }))
    expect(screen.getByText('No runs in this period.')).toBeVisible()
  })
})
