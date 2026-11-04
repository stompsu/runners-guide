import { render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import App from './App'

describe('Runners Diary shell', () => {
  it('renders the public landing page and sign-in route', () => {
    render(
      <MemoryRouter initialEntries={['/']}>
        <App />
      </MemoryRouter>,
    )

    expect(
      screen.getByRole('heading', {
        name: 'A clearer view of your running journey.',
      }),
    ).toBeInTheDocument()
    expect(
      within(
        screen.getByRole('navigation', { name: 'Main navigation' }),
      ).getByRole('link', { name: 'Sign in' }),
    ).toHaveAttribute(
      'href',
      '/sign-in',
    )
  })

  it('renders the sign-in placeholder at its route', () => {
    render(
      <MemoryRouter initialEntries={['/sign-in']}>
        <App />
      </MemoryRouter>,
    )

    expect(
      screen.getByRole('heading', { name: 'Sign in to Runners Diary' }),
    ).toBeInTheDocument()
  })
})
