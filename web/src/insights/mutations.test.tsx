import { act, cleanup, renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider, useQuery } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { useRunMutation } from '../diary/queries'
import { insightsOptions, racesOptions } from './queries'
import { insightFixture, raceFixture } from './fixtures.test-support'
import type { RunInput } from '../diary/calendar'

const { fetchMock } = vi.hoisted(() => {
  const fetchMock = vi.fn()
  vi.stubGlobal('fetch', fetchMock)
  return { fetchMock }
})
afterEach(cleanup)

describe('Insights invalidation', () => {
  it.each(['create', 'update', 'delete'] as const)('refetches active summary and race queries after successful run %s', async operation => {
    let summaryReads = 0
    let raceReads = 0
    fetchMock.mockImplementation(async (request: Request) => {
      const path = new URL(request.url).pathname
      if (path === '/api/insights') { summaryReads++; return Response.json(insightFixture()) }
      if (path === '/api/insights/races') { raceReads++; return Response.json(raceFixture()) }
      return new Response(null, { status: 204 })
    })
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    client.setQueryData(['runs', 'test'], [])
    const wrapper = ({ children }: { children: ReactNode }) => <QueryClientProvider client={client}>{children}</QueryClientProvider>
    const { result } = renderHook(() => {
      const summary = useQuery(insightsOptions({ from: '2026-10-01', to: '2026-10-14', bucket: 'week' }))
      const races = useQuery(racesOptions('2026-10-14'))
      const mutation = useRunMutation()
      return { summary, races, mutation }
    }, { wrapper })
    await waitFor(() => expect(result.current.summary.isSuccess && result.current.races.isSuccess).toBe(true))
    const body: RunInput = {
      title: 'Run', startAtUtc: '2026-10-14T08:00:00Z', runTypeCode: 'easy-run', status: 'Completed',
    }
    await act(async () => { await result.current.mutation.mutateAsync(
      operation === 'delete' ? { id: 'run-1' } : { body, id: operation === 'update' ? 'run-1' : undefined },
    ) })
    expect(summaryReads).toBe(2)
    expect(raceReads).toBe(2)
    expect(client.getQueryState(['runs', 'test'])?.isInvalidated).toBe(true)
    client.clear()
  })

  it('does not invalidate on failed writes', async () => {
    fetchMock.mockResolvedValue(new Response(null, { status: 500 }))
    const client = new QueryClient()
    client.setQueryData(['insights', 'cached'], insightFixture())
    const { result } = renderHook(useRunMutation, {
      wrapper: ({ children }) => <QueryClientProvider client={client}>{children}</QueryClientProvider>,
    })
    await act(async () => { await expect(result.current.mutateAsync({ id: 'run-1' })).rejects.toThrow() })
    expect(client.getQueryState(['insights', 'cached'])?.isInvalidated).toBe(false)
    client.clear()
  })
})
