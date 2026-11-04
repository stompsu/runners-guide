import { createContext, useCallback, useContext, useEffect, useState } from 'react'
import type { ReactNode } from 'react'
import type { AuthenticationResult, IPublicClientApplication } from '@azure/msal-browser'
import { useQueryClient } from '@tanstack/react-query'
import { Navigate, useLocation } from 'react-router-dom'
import { apiClient, setApiAccessToken, setApiTokenProvider } from '../api/client'
import { apiScope, entraClient } from './entra'
import { en } from '../i18n'

type Session = { kind: 'guest' | 'entra'; expiresAt?: string }
type SessionContext = {
  session: Session | null
  expired: boolean
  guest: () => Promise<void>
  signIn: (result: AuthenticationResult, client: IPublicClientApplication) => void
  signOut: () => void
}
const Context = createContext<SessionContext | null>(null)

export function SessionProvider({ children }: { children: ReactNode }) {
  const [session, setSession] = useState<Session | null>(null)
  const [expired, setExpired] = useState(false)
  const queryClient = useQueryClient()
  const clear = useCallback((isExpired = false) => {
    setApiAccessToken(null)
    setApiTokenProvider(null)
    setSession(null)
    setExpired(isExpired)
    queryClient.clear()
  }, [queryClient])

  useEffect(() => {
    const expire = () => {
      clear(true)
      void entraClient?.clearCache()
    }
    window.addEventListener('session-expired', expire)
    return () => window.removeEventListener('session-expired', expire)
  }, [clear])
  useEffect(() => {
    if (!session?.expiresAt) return
    const timeout = window.setTimeout(() => clear(true),
      Math.max(0, Date.parse(session.expiresAt) - Date.now()))
    return () => window.clearTimeout(timeout)
  }, [session, clear])
  useEffect(() => () => {
    setApiAccessToken(null)
    setApiTokenProvider(null)
  }, [])

  async function guest() {
    const { data, response } = await apiClient.POST('/api/auth/guest')
    if (!data) throw new Error(response.status === 429
      ? en.auth.rateLimited : en.requestFailed)
    clear()
    setApiAccessToken(data.accessToken)
    setSession({ kind: 'guest', expiresAt: data.expiresAtUtc })
  }
  function signIn(result: AuthenticationResult, client: IPublicClientApplication) {
    clear()
    setApiAccessToken(result.accessToken)
    setApiTokenProvider(async () => (await client.acquireTokenSilent({
      account: result.account!, scopes: [apiScope!],
    })).accessToken)
    setSession({ kind: 'entra' })
  }
  return <Context.Provider value={{ session, expired, guest, signIn, signOut: () => {
    clear()
    void entraClient?.clearCache()
  } }}>
    {children}
  </Context.Provider>
}

// A provider and its consuming hook intentionally share this small module.
// oxlint-disable-next-line react/only-export-components
export function useSession() {
  const context = useContext(Context)
  if (!context) throw new Error('SessionProvider is required')
  return context
}

export function Protected({ children }: { children: ReactNode }) {
  const { session } = useSession()
  const location = useLocation()
  return session ? children : <Navigate to="/sign-in" state={{ from: location.pathname + location.search }} replace />
}
