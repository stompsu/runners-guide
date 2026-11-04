import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, expect, it, vi } from 'vitest'
import { apiClient } from '../api/client'
import { SessionProvider, useSession } from './Session'

function Consumer() {
  const { session, expired, guest } = useSession()
  return <>
    <button onClick={() => void guest()}>Start guest</button>
    <output aria-label="Session">{session?.kind ?? 'none'}</output>
    <output aria-label="Expired">{String(expired)}</output>
  </>
}

afterEach(() => {
  cleanup()
  vi.restoreAllMocks()
  vi.useRealTimers()
})

it('expires a guest at the backend expiry time and clears private cached data', async () => {
  vi.useFakeTimers()
  vi.setSystemTime(new Date('2026-11-04T12:00:00Z'))
  vi.spyOn(apiClient, 'POST').mockResolvedValue({
    data: { accessToken: 'guest-memory-token', expiresAtUtc: '2026-11-04T12:01:00Z' },
    response: new Response(),
  })
  const client = new QueryClient()
  render(<QueryClientProvider client={client}><SessionProvider><Consumer /></SessionProvider></QueryClientProvider>)
  await act(async () => fireEvent.click(screen.getByRole('button', { name: 'Start guest' })))
  expect(screen.getByLabelText('Session')).toHaveTextContent('guest')
  client.setQueryData(['runs', 'private'], ['private run'])
  await act(async () => vi.advanceTimersByTime(60000))
  expect(screen.getByLabelText('Session')).toHaveTextContent('none')
  expect(screen.getByLabelText('Expired')).toHaveTextContent('true')
  expect(client.getQueryData(['runs', 'private'])).toBeUndefined()
})
