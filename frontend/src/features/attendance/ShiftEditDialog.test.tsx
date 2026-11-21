import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import ShiftEditDialog from './ShiftEditDialog'
import type { SheetDay } from './Timesheet'

afterEach(() => { cleanup(); vi.unstubAllGlobals() })

const day: SheetDay = {
  date: '2026-06-02', weekday: 'Tuesday', clockInUtc: '2026-06-02T06:00:00Z', clockOutUtc: null, clockInIsManual: false, clockOutIsManual: false,
  inProgress: true, needsCorrection: false, workedMinutes: null, expectedMinutes: 510, balanceMinutes: null, isToday: false, isFuture: false,
}
const side = (out: string | null) => ({ day: { clockInUtc: day.clockInUtc, clockOutUtc: out, clockInIsManual: false, clockOutIsManual: out != null, inProgress: out == null, workedMinutes: out ? 540 : null, balanceMinutes: out ? 30 : null }, monthToDate: { balanceMinutes: 30 } })

it('previews the effective before/after, then saves only after confirmation, sending Zurich local time and a reason', async () => {
  const calls: { url: string; body?: string }[] = []
  vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
    calls.push({ url, body: init?.body as string | undefined })
    if (url.endsWith('/attendance/reasons')) return { ok: true, status: 200, json: async () => [{ code: 'ForgotClockOut', label: 'Forgot to clock out' }, { code: 'Other', label: 'Other' }] }
    return { ok: true, status: 200, json: async () => ({ saved: url.endsWith('/shifts'), before: side(null), after: side('2026-06-02T15:00:00Z') }) }
  }))
  const onSaved = vi.fn()
  render(<ShiftEditDialog day={day} onClose={() => {}} onSaved={onSaved} />)

  await screen.findByRole('option', { name: 'Forgot to clock out' })
  fireEvent.change(screen.getByLabelText('Clock-out'), { target: { value: '17:00' } })
  fireEvent.click(screen.getByRole('button', { name: 'Preview' }))

  await screen.findByText('Before'); expect(screen.getByText('After')).toBeTruthy()
  expect(onSaved).not.toHaveBeenCalled()
  const preview = calls.find((c) => c.url.endsWith('/shifts/preview'))!
  expect(JSON.parse(preview.body!)).toMatchObject({ date: '2026-06-02', clockOutLocal: '2026-06-02T17:00:00', reasonCode: 'ForgotClockOut' })

  fireEvent.click(screen.getByRole('button', { name: 'Confirm and save' }))
  await waitFor(() => expect(onSaved).toHaveBeenCalled())
})

it('shows the server message and keeps the dialog open when the edit is rejected', async () => {
  vi.stubGlobal('fetch', vi.fn(async (url: string) =>
    url.endsWith('/reasons') ? { ok: true, status: 200, json: async () => [{ code: 'ForgotClockOut', label: 'Forgot to clock out' }] }
      : { ok: false, status: 422, json: async () => ({ error: 'Clock-out must be after clock-in.' }) }))
  render(<ShiftEditDialog day={day} onClose={() => {}} onSaved={() => {}} />)

  await screen.findByRole('option', { name: 'Forgot to clock out' })
  fireEvent.click(screen.getByRole('button', { name: 'Preview' }))

  expect((await screen.findByRole('alert')).textContent).toContain('Clock-out must be after clock-in.')
})
