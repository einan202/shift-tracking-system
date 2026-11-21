import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import Employees from './Employees'

afterEach(() => { cleanup(); vi.unstubAllGlobals() })

it('shows the list first and opens the add-employee form in a dialog prefilled with the company week', async () => {
  vi.stubGlobal('fetch', vi.fn(async (url: string) => ({
    ok: true, status: 200,
    json: async () => url.includes('/settings/shift') ? { durationMinutes: 540, minutes: [540, 540, 465, 540, 540, 0, 0] }
      : [{ id: 1, email: 'a@x.ch', fullName: 'Anna', isActive: true, hasPassword: true, invitePending: false, inviteExpiresUtc: null, employmentStartDate: '2026-06-01' }],
  })))
  render(<Employees />)

  expect(await screen.findByText('Anna')).toBeTruthy()
  expect(screen.queryByRole('dialog')).toBeNull()                       // no permanently expanded form

  fireEvent.click(screen.getByRole('button', { name: 'Add employee' }))
  expect(screen.getByRole('dialog', { name: 'Add employee' })).toBeTruthy()
  expect((screen.getByLabelText('Wednesday hours') as HTMLInputElement).value).toBe('7')
  expect((screen.getByLabelText('Wednesday minutes') as HTMLInputElement).value).toBe('45')
  expect((screen.getByLabelText('Monday hours') as HTMLInputElement).value).toBe('9')

  fireEvent.keyDown(screen.getByRole('dialog'), { key: 'Escape' })
  expect(screen.queryByRole('dialog')).toBeNull()
})
