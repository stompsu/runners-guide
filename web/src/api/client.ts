import createClient from 'openapi-fetch'
import type { paths } from './generated/openapi'

export const apiClient = createClient<paths>({ baseUrl: '' })

let accessToken: string | null = null

export function setApiAccessToken(token: string | null) {
  accessToken = token
}

apiClient.use({
  onRequest({ request }) {
    if (accessToken) {
      request.headers.set('Authorization', `Bearer ${accessToken}`)
    }

    return request
  },
})
