import { lazy, Suspense, useState } from 'react'
import { Link, NavLink, Navigate, Route, Routes, useLocation } from 'react-router-dom'
import { useMsal } from '@azure/msal-react'
import type { AuthenticationResult, IPublicClientApplication } from '@azure/msal-browser'
import { SessionProvider, Protected, useSession } from './auth/Session'
import { apiScope, entraClient } from './auth/entra'
import { DiaryPage } from './diary/DiaryPage'
import { monthPath } from './diary/calendar'
import { en } from './i18n'
import { safeReturnPath } from './auth/returnPath'
import './App.css'

const InsightsPage = lazy(() => import('./insights/InsightsPage'))

function EntraSignInButton({ pending, onPendingChange, onBegin, onSuccess, onFailure }: {
  pending: boolean
  onPendingChange: (pending: boolean) => void
  onBegin: () => void
  onSuccess: (result: AuthenticationResult, instance: IPublicClientApplication) => void
  onFailure: () => void
}) {
  const { instance } = useMsal()
  async function signIn() {
    onPendingChange(true)
    onBegin()
    try {
      onSuccess(await instance.loginPopup({ scopes: [apiScope!] }), instance)
    } catch {
      onFailure()
    } finally { onPendingChange(false) }
  }
  return <button className="primary" disabled={pending} onClick={() => void signIn()}>{en.signIn}</button>
}

function SignInPage() {
  const auth = useSession()
  const location = useLocation()
  const [pending, setPending] = useState(false)
  const [error, setError] = useState('')
  const [currentMonth] = useState(() => monthPath(new Date()))
  const from = location.state?.from
  const destination = safeReturnPath(from, currentMonth)
  if (auth.session) return <Navigate to={destination} replace />
  async function signInAsGuest() {
    setPending(true)
    setError('')
    try {
      await auth.guest()
    } catch {
      setError(en.auth.guestFailed)
    } finally { setPending(false) }
  }
  return <main className="page-content">
    <p className="eyebrow">{en.auth.eyebrow}</p>
    <h1>{en.auth.heading}</h1>
    <p className="intro">{en.auth.intro}</p>
    {auth.expired && <p role="alert">{en.expired}</p>}
    {error && <p role="alert">{error}</p>}
    <div className="toolbar">
      {entraClient
        ? <EntraSignInButton pending={pending} onPendingChange={setPending} onBegin={() => setError('')}
            onSuccess={auth.signIn} onFailure={() => setError(en.auth.failed)} />
        : <button className="primary" disabled>{en.signIn}</button>}
      <button disabled={pending} onClick={() => void signInAsGuest()}>{pending ? en.auth.wait : en.guest}</button>
    </div>
    {!entraClient && <p className="muted">{en.auth.unconfigured}</p>}
  </main>
}

function Shell() {
  const { session, signOut } = useSession()
  const [currentMonth] = useState(() => monthPath(new Date()))
  return <div className="app-shell">
    <header className="site-header">
      <Link aria-label={en.auth.home} className="brand" to={session ? '/diary' : '/sign-in'}>
        <span aria-hidden="true" className="brand-mark">R</span><span>{en.auth.brand}</span>
      </Link>
      <nav aria-label={en.auth.navigation}>
        {session && <><NavLink to="/diary">{en.insights.diary}</NavLink><NavLink to="/insights">{en.insights.heading}</NavLink></>}
        {session ? <button onClick={signOut}>{en.auth.signOut}</button> : <Link to="/sign-in">{en.signIn}</Link>}
      </nav>
    </header>
    {session?.kind === 'guest' && <aside className="demo-banner" aria-label="Demo mode">
      {en.demo} <button onClick={signOut}>{en.signIn}</button>
    </aside>}
    <Routes>
      <Route path="/" element={<Navigate to={session ? '/diary' : '/sign-in'} replace />} />
      <Route path="/sign-in" element={<SignInPage />} />
      <Route path="/diary" element={<Protected><Navigate to={currentMonth} replace /></Protected>} />
      <Route path="/diary/:year/:month" element={<Protected><DiaryPage /></Protected>} />
      <Route path="/insights" element={<Protected><Suspense fallback={<p role="status">{en.insights.loading}</p>}><InsightsPage /></Suspense></Protected>} />
      <Route path="*" element={<main className="page-content"><h1>{en.auth.notFound}</h1><Link to="/">{en.auth.back}</Link></main>} />
    </Routes>
    <footer className="site-footer">{en.auth.footer}</footer>
  </div>
}

export default function App() {
  return <SessionProvider><Shell /></SessionProvider>
}
