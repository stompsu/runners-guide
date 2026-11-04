import { cleanup, render, waitFor } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { MapView } from './MapView'

const mocks = vi.hoisted(() => {
  const fetchMock = vi.fn()
  vi.stubGlobal('fetch', fetchMock)
  return { fetchMock, options: vi.fn(), dispose: vi.fn(), markers: vi.fn() }
})
vi.mock('azure-maps-control', () => ({
  AuthenticationType: { anonymous: 'anonymous' }, ControlPosition: { TopRight: 'top-right' },
  Map: class {
    constructor(_element: HTMLElement, options: unknown) { mocks.options(options) }
    events = { add: (name: string, callback: () => void) => { if (name === 'ready') callback() } }
    controls = { add: vi.fn() }
    markers = { add: mocks.markers }
    dispose = mocks.dispose
  },
  control: { ZoomControl: class {} },
  HtmlMarker: class {},
}))
afterEach(() => cleanup())
it('uses SDK v3 anonymous callback with backend OAuth token and disposes the map', async () => {
  mocks.fetchMock.mockResolvedValue(new Response(JSON.stringify({
    clientId: 'maps-account-client-id', accessToken: 'synthetic-oauth-token',
    expiresAtUtc: new Date(Date.now() + 300_000).toISOString(),
  }), { headers: { 'Content-Type': 'application/json' } }))
  const view = render(<MapView />)
  await waitFor(() => expect(mocks.options).toHaveBeenCalledOnce())
  const options = mocks.options.mock.calls[0][0]
  expect(options.authOptions.authType).toBe('anonymous')
  expect(options.authOptions.clientId).toBe('maps-account-client-id')
  expect(options.authOptions).not.toHaveProperty('subscriptionKey')
  const resolve = vi.fn()
  options.authOptions.getToken(resolve, vi.fn())
  expect(resolve).toHaveBeenCalledWith('synthetic-oauth-token')
  expect(new URL(mocks.fetchMock.mock.calls[0][0].url).pathname).toBe('/api/maps/token')
  view.unmount()
  expect(mocks.dispose).toHaveBeenCalledOnce()
})
