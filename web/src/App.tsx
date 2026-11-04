import { Link, Route, Routes } from 'react-router-dom'
import './App.css'

function HomePage() {
  return (
    <main className="page-content">
      <p className="eyebrow">Your training, thoughtfully tracked</p>
      <h1>A clearer view of your running journey.</h1>
      <p className="intro">
        Plan your runs, keep your race history, and make sense of your training
        over time.
      </p>
      <Link className="primary-link" to="/sign-in">
        Sign in
      </Link>
      <p className="milestone-note">
        The diary and guest demo will be available in upcoming milestones.
      </p>
    </main>
  )
}

function SignInPage() {
  return (
    <main className="page-content">
      <p className="eyebrow">Welcome back</p>
      <h1>Sign in to Runners Diary</h1>
      <p className="intro">
        Account sign-in and guest demo access are coming in the next milestone.
      </p>
      <Link className="secondary-link" to="/">
        Back to home
      </Link>
    </main>
  )
}

function NotFoundPage() {
  return (
    <main className="page-content">
      <p className="eyebrow">Page not found</p>
      <h1>That route is not part of the diary yet.</h1>
      <Link className="secondary-link" to="/">
        Back to home
      </Link>
    </main>
  )
}

export default function App() {
  return (
    <div className="app-shell">
      <header className="site-header">
        <Link aria-label="Runners Diary home" className="brand" to="/">
          <span aria-hidden="true" className="brand-mark">R</span>
          <span>Runners Diary</span>
        </Link>
        <nav aria-label="Main navigation">
          <Link to="/sign-in">Sign in</Link>
        </nav>
      </header>
      <Routes>
        <Route element={<HomePage />} path="/" />
        <Route element={<SignInPage />} path="/sign-in" />
        <Route element={<NotFoundPage />} path="*" />
      </Routes>
      <footer className="site-footer">
        A private space for your running life.
      </footer>
    </div>
  )
}
