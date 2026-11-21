import { render, screen } from '@testing-library/react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import App from './App'
import { resetInviteTokenForTests } from './inviteLink'

beforeEach(() => { sessionStorage.clear(); resetInviteTokenForTests() })
afterEach(() => { vi.unstubAllGlobals(); window.history.pushState({}, '', '/') })

it('shows the sign-in form when logged out', () => {
  render(<App />)
  expect(screen.getByRole('heading', { name: 'Sign in' })).toBeTruthy()
})

it('opens the password form from an invite link', () => {
  window.history.pushState({}, '', '/set-password#token=abc')
  render(<App />)
  expect(screen.getByRole('heading', { name: 'Choose your password' })).toBeTruthy()
  expect(window.location.hash).toBe('') // the token no longer shows in the address bar
})

it('shows the employee screen for an employee session', async () => {
  sessionStorage.setItem('clockapp.token', 't')
  sessionStorage.setItem('clockapp.user', JSON.stringify({ role: 'Employee', fullName: 'Eve Employee' }))
  vi.stubGlobal('fetch', vi.fn().mockImplementation(async (url: string) => ({
    ok: true, status: 200,
    json: async () => (url.includes('/status') ? { clockedIn: false, record: null }
      : { year: 2026, month: 2, defaultShiftMinutes: 480, days: [], totals: { workedMinutes: 0, expectedMinutes: 0, differenceMinutes: 0, daysWorked: 0 } }),
  })))
  render(<App />)
  expect(await screen.findByRole('button', { name: 'Clock in' })).toBeTruthy()
  expect(screen.getByText('Eve Employee')).toBeTruthy()
})
