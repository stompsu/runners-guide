import { act, cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, useLocation, useNavigate } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { insightFixture, raceFixture } from './fixtures.test-support'
import type { ChartPoint, SeriesPoint, SeriesStyle } from './Charts'
import { en } from '../i18n'

const { fetchMock } = vi.hoisted(() => {
  const fetchMock = vi.fn()
  vi.stubGlobal('fetch', fetchMock)
  return { fetchMock }
})
vi.mock('../auth/entra', () => ({ entraClient: undefined, apiScope: undefined }))
vi.mock('./Charts', () => ({
  Chart: ({ label, points }: { label: string; points: ChartPoint[] }) =>
    <div role="img" aria-label={label}>{points.map(point => <span key={point.label}>{point.label}: {String(point.value)}; </span>)}</div>,
  SeriesChart: ({ label, points, series }: { label: string; points: SeriesPoint[]; series: SeriesStyle[] }) =>
    <div role="img" aria-label={label}>{series.map(item => <span key={item.key}>{item.name}{item.dashed ? ' (dashed)' : ''}: {
      points.map(point => `${point.label}=${String(point[item.key])}`).join(', ')}; </span>)}</div>,
}))
import App from '../App'

const bookmark = '/insights?from=2026-10-01&to=2026-10-14&bucket=week'
const json = (body: unknown) => new Response(JSON.stringify(body), { headers: { 'Content-Type': 'application/json' } })
let summary = insightFixture()
let status = 200
let raceStatus = 200
let emptyRaces = false
let pending = false
let diaryRuns: unknown[] = []
let release: (() => void) | undefined
function Location() {
  const location = useLocation()
  const navigate = useNavigate()
  return <><output aria-label="Current URL">{location.pathname + location.search}</output>
    <button onClick={() => navigate(-1)}>Browser back</button></>
}
function mount(path = bookmark) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  render(<QueryClientProvider client={client}><MemoryRouter initialEntries={[path]}><App /><Location /></MemoryRouter></QueryClientProvider>)
  return client
}
async function enter() {
  const user = userEvent.setup()
  await user.click(screen.getByRole('button', { name: 'Try as guest' }))
  await screen.findByRole('heading', { name: 'Insights', level: 1 })
  return user
}
beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date'] })
  vi.setSystemTime(new Date('2026-11-04T23:30:00Z'))
  summary = insightFixture()
  status = 200
  raceStatus = 200
  emptyRaces = false
  pending = false
  release = undefined
  diaryRuns = []
  fetchMock.mockReset()
  fetchMock.mockImplementation(async (request: Request) => {
    const url = new URL(request.url)
    if (url.pathname === '/api/auth/guest') return json({ accessToken: 'test-demo', expiresAtUtc: '2026-11-05T23:30:00Z' })
    if (url.pathname === '/api/insights/races') {
      if (raceStatus !== 200) return new Response(null, { status: raceStatus })
      if (emptyRaces) return json({ items: [], nextCursor: null })
      return json(url.searchParams.has('cursor') ? { ...raceFixture(null), nextCursor: null } : raceFixture())
    }
    if (url.pathname === '/api/insights') {
      if (pending) await new Promise<void>(resolve => { release = resolve })
      return status === 200 ? json(summary) : new Response(null, { status })
    }
    if (url.pathname === '/api/runs') return json(diaryRuns)
    throw new Error(`Unexpected request: ${url.pathname}`)
  })
})
afterEach(() => { cleanup(); vi.useRealTimers() })

describe('M5 protected Insights dashboard', () => {
  it('preserves authenticated bookmarks and displays only server totals, weighted pace, coverage and load snapshots', async () => {
    mount()
    expect(fetchMock).not.toHaveBeenCalled()
    await enter()
    expect(screen.getByLabelText('Current URL')).toHaveTextContent(bookmark)
    const selected = await screen.findByRole('region', { name: en.insights.selected })
    expect(selected).toHaveTextContent('12.56 km')
    expect(selected).toHaveTextContent('76.12 min')
    expect(selected).toHaveTextContent('2 / 3 · Incomplete · 1 missing')
    expect(selected).toHaveTextContent('Completed rest / injury entries: 2')
    expect(screen.getByRole('region', { name: en.insights.distribution })).not.toHaveTextContent('Rest / injury day')
    expect(screen.getByRole('region', { name: en.insights.ytd })).toHaveTextContent('2026-01-01')
    const pace = screen.getByRole('region', { name: en.insights.pace })
    expect(pace).toHaveTextContent('5.619 min/km')
    expect(pace).toHaveTextContent('1 / 2 · Incomplete')
    const load = screen.getByRole('region', { name: en.insights.trainingLoad })
    expect(load).toHaveTextContent('2,100 min × RPE')
    expect(load).toHaveTextContent('1,400 min × RPE')
    const acwr = screen.getByRole('region', { name: en.insights.acwrSection })
    expect(acwr).toHaveTextContent('1.5 ratio')
    expect(acwr).toHaveTextContent('Limited history')
    expect(screen.getByRole('region', { name: en.insights.monotonySection })).toHaveTextContent('Zero daily-load variance')
    expect(screen.getByRole('region', { name: en.insights.comparisons })).toHaveTextContent('Previous subtotal is zero')
    await waitFor(() => expect(screen.getAllByRole('img').length).toBeGreaterThanOrEqual(7))
    const user = userEvent.setup()
    await user.click(screen.getByText(`Show data table: ${en.insights.bucketTable}`))
    expect(screen.getByRole('table', { name: en.insights.bucketTable })).toHaveTextContent('321 m')
    const requests = fetchMock.mock.calls.map(([request]) => request as Request)
    const insights = requests.find(request => new URL(request.url).pathname === '/api/insights')!
    expect(insights.headers.has('Authorization')).toBe(true)
    expect(new URL(insights.url).search).toBe('?from=2026-10-01&to=2026-10-14&bucket=week')
  })

  it('renders daily load, ACWR and monotony as separate time series with gaps and data tables', async () => {
    mount()
    const user = await enter()
    const load = await screen.findByRole('region', { name: en.insights.trainingLoad })
    const loadChart = await within(load).findByRole('img', { name: /Daily acute vs weekly-equivalent chronic load/ })
    // Incomplete chronic window on 14 Oct is a gap (null), never a partial value or zero.
    expect(loadChart).toHaveTextContent('Acute: 2026-10-13=1800, 2026-10-14=2100')
    expect(loadChart).toHaveTextContent('Chronic / week (dashed): 2026-10-13=1200, 2026-10-14=null')
    const acwr = screen.getByRole('region', { name: en.insights.acwrSection })
    const acwrChart = await within(acwr).findByRole('img', { name: /Daily ACWR/ })
    expect(acwrChart).toHaveTextContent('2026-10-13: 1.25; 2026-10-14: null')
    expect(acwrChart).not.toHaveTextContent('Monotony')
    const monotony = screen.getByRole('region', { name: en.insights.monotonySection })
    expect(await within(monotony).findByRole('img', { name: /Daily monotony/ })).toHaveTextContent('2026-10-13: 1.7; 2026-10-14: null')
    expect(within(load).queryByRole('img', { name: /ACWR|monotony/i })).toBeNull()
    await user.click(within(acwr).getByText('Show data table: Daily ACWR'))
    const acwrTable = within(acwr).getByRole('table', { name: 'Daily ACWR' })
    expect(within(acwrTable).getByRole('row', { name: /2026-10-13/ })).toHaveTextContent('1.25 ratioOptimal')
    expect(within(acwrTable).getByRole('row', { name: /2026-10-14/ })).toHaveTextContent('Unavailable — Missing activity data')
    await user.click(within(load).getByText(/^Show data table: Daily acute/))
    expect(within(load).getByRole('row', { name: /2026-10-14/ })).toHaveTextContent('2 / 3 · Incomplete · 1 missing')
  })

  it('updates bookmarkable controls, supports browser back and defaults to UTC month-to-today', async () => {
    mount('/insights')
    const user = await enter()
    await waitFor(() => expect(screen.getByLabelText('Current URL')).toHaveTextContent('/insights?from=2026-11-01&to=2026-11-04&bucket=week'))
    await user.selectOptions(screen.getByLabelText('Group by'), 'month')
    await user.click(screen.getByRole('button', { name: 'Apply period' }))
    expect(screen.getByLabelText('Current URL')).toHaveTextContent('bucket=month')
    await user.click(screen.getByRole('button', { name: 'Browser back' }))
    expect(screen.getByLabelText('Group by')).toHaveValue('week')
    const from = screen.getByLabelText('From (UTC)')
    await user.clear(from)
    await user.type(from, '2025-01-01')
    await user.click(screen.getByRole('button', { name: 'Apply period' }))
    expect(screen.getByRole('alert')).toHaveTextContent(en.insights.invalidPeriod)
    expect(screen.getByLabelText('Current URL')).toHaveTextContent('from=2026-11-01')
  })

  it('paginates races using opaque cursors, shows missing details, and resets pagination when To changes', async () => {
    mount()
    const user = await enter()
    const raceSection = await screen.findByRole('region', { name: en.insights.races })
    expect(await within(raceSection).findByText('Autumn race')).toBeVisible()
    const table = within(raceSection).getByRole('table')
    expect(table).toHaveTextContent('0:40:01')
    expect(table).toHaveTextContent('Unavailable')
    await user.click(within(raceSection).getByRole('button', { name: 'Next page' }))
    await waitFor(() => expect(within(raceSection).getByRole('button', { name: 'Next page' })).toBeDisabled())
    expect(raceSection).toHaveTextContent('Page 2')
    expect(fetchMock.mock.calls.some(([request]) => new URL((request as Request).url).searchParams.get('cursor') === 'opaque-next')).toBe(true)
    await user.click(within(raceSection).getByRole('button', { name: 'Previous page' }))
    expect(await within(raceSection).findByText('Autumn race')).toBeVisible()
    await user.click(screen.getByRole('button', { name: 'Month to today' }))
    const reset = await screen.findByRole('region', { name: en.insights.races })
    expect(reset).toHaveTextContent('Page 1')
    expect(within(reset).getByRole('button', { name: 'Previous page' })).toBeDisabled()
  })

  it('has loading, independent API error/retry and empty states', async () => {
    pending = true
    mount()
    const user = await enter()
    expect(screen.getByText(en.insights.loading)).toBeVisible()
    await waitFor(() => expect(release).toBeTypeOf('function'))
    status = 500
    await act(async () => { release?.() })
    expect(await screen.findByRole('alert')).toHaveTextContent(en.insights.failed)
    expect(screen.getByText('Autumn race')).toBeVisible()
    pending = false
    status = 200
    summary.selected.totals.sessionCount = 0
    summary.distribution = []
    summary.selected.easyPace.secondsPerKilometer = { value: null, reason: 'noEligibleSessions' }
    await user.click(screen.getByRole('button', { name: 'Retry' }))
    expect(await screen.findByText(en.insights.empty)).toBeVisible()
    expect(screen.getByText(en.insights.noPace)).toBeVisible()
  })

  it('handles race errors without losing the summary and clears cached insights on sign-out', async () => {
    raceStatus = 500
    const client = mount()
    const user = await enter()
    expect(await screen.findByRole('alert')).toHaveTextContent(en.insights.racesFailed)
    expect(screen.getByRole('region', { name: en.insights.selected })).toBeVisible()
    raceStatus = 200
    await user.click(screen.getByRole('button', { name: 'Retry' }))
    expect(await screen.findByText('Autumn race')).toBeVisible()
    expect(client.getQueriesData({ queryKey: ['insights'] }).length).toBeGreaterThan(0)
    await user.click(screen.getByRole('button', { name: 'Sign out' }))
    expect(client.getQueriesData({ queryKey: ['insights'] })).toHaveLength(0)
    expect(screen.getByRole('heading', { name: 'Sign in to Runners Diary' })).toBeVisible()
  })

  it('does not request insights for an invalid bookmarked period', async () => {
    mount('/insights?from=2026-02-30&to=2026-03-01&bucket=week')
    await userEvent.setup().click(screen.getByRole('button', { name: 'Try as guest' }))
    await screen.findByRole('grid', { name: 'November 2026' })
    // Invalid return paths intentionally fall back to the diary rather than replaying invalid API queries.
    expect(fetchMock.mock.calls.some(([request]) => new URL((request as Request).url).pathname === '/api/insights')).toBe(false)
  })

  it('links each race to its diary day and opens that day panel with owner-scoped month data', async () => {
    diaryRuns = [{
      id: 'race-1', startAtUtc: '2026-10-04T08:30:00Z', title: 'Autumn 10K', status: 'Completed',
      runTypeCode: 'road-race', runTypeName: 'Road race', distanceMeters: 10000, durationSeconds: 2401,
      elevationGainMeters: null, elevationLossMeters: null, notes: null, averageHeartRateBpm: null,
      maximumHeartRateBpm: null, perceivedEffort: null, shoesOrGear: null, weather: null, surface: null,
      location: null, raceDetails: null, rating: null,
    }]
    mount()
    const user = await enter()
    const link = await screen.findByRole('link', { name: 'Open Autumn race on 2026-10-04 in the diary' })
    link.focus()
    await user.keyboard('{Enter}')
    expect(screen.getByLabelText('Current URL')).toHaveTextContent('/diary/2026/10')
    const dialog = await screen.findByRole('dialog', { name: 'Sunday, 4 October 2026' })
    expect(within(dialog).getByText('Autumn 10K')).toBeVisible()
    const runsRequest = fetchMock.mock.calls.map(([request]) => new URL((request as Request).url))
      .find(url => url.pathname === '/api/runs')!
    expect(runsRequest.searchParams.get('from')).toBe('2026-09-28')
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(screen.getByRole('button', { name: /^Sunday, 4 October 2026,/ })).toHaveFocus()
  })

  it('shows empty race history without a next page', async () => {
    emptyRaces = true
    mount()
    await enter()
    expect(await screen.findByText(en.insights.noRaces)).toBeVisible()
    const pagination = screen.getByRole('navigation', { name: en.insights.racePagination })
    expect(within(pagination).getByRole('button', { name: 'Next page' })).toBeDisabled()
  })
})
