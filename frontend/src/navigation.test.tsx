import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import App from './App'
import { resetInviteTokenForTests } from './inviteLink'

const emptySheet = { year: 2026, month: 2, zurichToday: '2026-02-10', days: [], monthToDate: { workedMinutes: 0, expectedMinutes: 0, balanceMinutes: 0 }, wholeMonth: { expectedMinutes: 0 } }

function stubApi() {
  vi.stubGlobal('fetch', vi.fn().mockImplementation(async (url: string) => {
    const body =
      url.includes('/attendance/status') ? { clockedIn: false, record: null }
      : url.includes('/attendance/me/timesheet') ? emptySheet
      : url.includes('/admin/employees') ? []
      : url.includes('/admin/settings/shift') ? { durationMinutes: 480 }
      : {}
    return { ok: true, status: 200, json: async () => body }
  }))
}

function signIn(role: 'Employee' | 'Admin') {
  sessionStorage.setItem('clockapp.token', 't')
  sessionStorage.setItem('clockapp.user', JSON.stringify({ role, fullName: 'Test User' }))
}

beforeEach(() => { sessionStorage.clear(); resetInviteTokenForTests(); stubApi() })
afterEach(() => { cleanup(); vi.unstubAllGlobals(); window.history.replaceState(null, '', '/') })

const tab = (name: string) => screen.getByRole('link', { name })
const current = () => screen.getByRole('navigation', { name: 'Sections' }).querySelector('[aria-current="page"]')?.textContent

it('employee sees Clock and Timesheet, opens on Clock', async () => {
  signIn('Employee')
  render(<App />)

  expect(await screen.findByRole('button', { name: 'Clock in' })).toBeTruthy()
  expect(['Clock', 'Timesheet'].map((n) => tab(n).textContent)).toEqual(['Clock', 'Timesheet'])
  expect(current()).toBe('Clock')
  expect(window.location.pathname).toBe('/clock')
})

it('clicking a tab changes the section and the URL, and Back returns to the previous section', async () => {
  signIn('Employee')
  render(<App />)
  await screen.findByRole('button', { name: 'Clock in' })

  fireEvent.click(tab('Timesheet'))
  await waitFor(() => expect(screen.queryByRole('button', { name: 'Clock in' })).toBeNull())
  expect(window.location.pathname).toBe('/timesheet')
  expect(current()).toBe('Timesheet')

  act(() => window.history.back())
  await waitFor(() => expect(window.location.pathname).toBe('/clock'))
  await waitFor(() => expect(current()).toBe('Clock'))
})

it('a deep link opens the right section', async () => {
  signIn('Employee')
  window.history.replaceState(null, '', '/timesheet')
  render(<App />)

  await waitFor(() => expect(current()).toBe('Timesheet'))
  expect(screen.queryByRole('button', { name: 'Clock in' })).toBeNull()
})

it('role guard: an employee is redirected away from admin sections', async () => {
  signIn('Employee')
  window.history.replaceState(null, '', '/employees')
  render(<App />)

  await waitFor(() => expect(window.location.pathname).toBe('/clock'))
  expect(screen.queryByRole('link', { name: 'Employees' })).toBeNull()
})

it('admin sees Attendance, Employees and Settings, opens on Attendance', async () => {
  signIn('Admin')
  render(<App />)

  await waitFor(() => expect(current()).toBe('Attendance'))
  expect(['Attendance', 'Employees', 'Settings'].map((n) => tab(n).textContent)).toEqual(['Attendance', 'Employees', 'Settings'])
  expect(window.location.pathname).toBe('/attendance')

  fireEvent.click(tab('Settings'))
  expect(await screen.findByRole('heading', { name: 'Default shift duration' })).toBeTruthy()
  expect(window.location.pathname).toBe('/settings')
})

it('role guard: an admin is redirected away from employee sections', async () => {
  signIn('Admin')
  window.history.replaceState(null, '', '/clock')
  render(<App />)

  await waitFor(() => expect(window.location.pathname).toBe('/attendance'))
  expect(screen.queryByRole('link', { name: 'Clock' })).toBeNull()
})

it('keeps modified clicks for the browser (open in new tab) and does not navigate', async () => {
  signIn('Employee')
  render(<App />)
  await screen.findByRole('button', { name: 'Clock in' })

  fireEvent.click(tab('Timesheet'), { ctrlKey: true })

  expect(window.location.pathname).toBe('/clock')
})
