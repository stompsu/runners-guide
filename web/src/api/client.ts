import createClient from 'openapi-fetch'
import type { paths } from './generated/openapi'

export const apiClient = createClient<paths>({ baseUrl: window.location.origin })

let accessToken: string | null = null
let tokenProvider: (() => Promise<string>) | null = null

export function setApiTokenProvider(provider: (() => Promise<string>) | null) {
  tokenProvider = provider
}

export function setApiAccessToken(token: string | null) {
  accessToken = token
}

apiClient.use({
  async onRequest({ request }) {
    // Guest creation must not inherit a previous session.
    if (new URL(request.url).pathname === '/api/auth/guest') return request
    let token = accessToken
    if (tokenProvider) {
      try {
        token = await tokenProvider()
      } catch {
        window.dispatchEvent(new Event('session-expired'))
        throw new Error('Your session has expired. Please sign in again.')
      }
    }
    if (token) {
      request.headers.set('Authorization', `Bearer ${token}`)
    }

    return request
  },
  onResponse({ response }) {
    if (response.status === 401) window.dispatchEvent(new Event('session-expired'))
    return response
  },
})
