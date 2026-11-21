import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import DeleteShiftDialog from './DeleteShiftDialog'
import type { SheetDay } from './Timesheet'

afterEach(() => { cleanup(); vi.unstubAllGlobals() })
const day: SheetDay = { date: '2026-06-02', weekday: 'Tuesday', clockInUtc: '2026-06-02T06:00:00Z', clockOutUtc: '2026-06-02T15:00:00Z', clockInIsManual: false, clockOutIsManual: false,
  inProgress: false, needsCorrection: false, workedMinutes: 540, expectedMinutes: 510, balanceMinutes: 30, isToday: false, isFuture: false }

it('warns about the consequence and deletes only after the button is pressed, with the chosen reason', async () => {
  const calls: { url: string; body?: string }[] = []
  vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
    calls.push({ url, body: init?.body as string })
    return url.endsWith('/reasons') ? { ok: true, status: 200, json: async () => [{ code: 'DuplicateOrAccidental', label: 'Duplicate or accidental punch' }] } : { ok: true, status: 200, json: async () => ({}) }
  }))
  const onDeleted = vi.fn()
  render(<DeleteShiftDialog day={day} onClose={() => {}} onDeleted={onDeleted} />)

  expect(screen.getByText(/removes the shift/).textContent).toContain('balance becomes')
  await screen.findByRole('option', { name: 'Duplicate or accidental punch' })
  expect(calls.some((c) => c.url.endsWith('/delete'))).toBe(false)
  fireEvent.click(screen.getByRole('button', { name: 'Delete shift' }))

  await waitFor(() => expect(onDeleted).toHaveBeenCalled())
  expect(JSON.parse(calls.find((c) => c.url.endsWith('/delete'))!.body!)).toMatchObject({ date: '2026-06-02', reasonCode: 'DuplicateOrAccidental' })
})
