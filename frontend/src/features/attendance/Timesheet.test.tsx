import { cleanup, render, screen, within } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import Timesheet, { type Sheet } from './Timesheet'

afterEach(() => { cleanup(); vi.unstubAllGlobals() })

const day = (date: string, weekday: string, o: Partial<Sheet['days'][number]> = {}) => ({
  date, weekday, clockInUtc: null, clockOutUtc: null, clockInIsManual: false, clockOutIsManual: false, inProgress: false, needsCorrection: false,
  workedMinutes: null, expectedMinutes: 0, balanceMinutes: null, isToday: false, isFuture: false, ...o,
})

const sheet: Sheet = {
  year: 2026, month: 6, zurichToday: '2026-06-10',
  days: [
    day('2026-06-01', 'Monday', { clockInUtc: '2026-06-01T06:00:00Z', clockOutUtc: '2026-06-01T15:30:00Z', workedMinutes: 570, expectedMinutes: 510, balanceMinutes: 60 }),
    day('2026-06-02', 'Tuesday', { clockInUtc: '2026-06-02T06:00:00Z', clockOutUtc: '2026-06-02T13:00:00Z', clockOutIsManual: true, workedMinutes: 420, expectedMinutes: 510, balanceMinutes: -90 }),
    day('2026-06-03', 'Wednesday', { clockInUtc: '2026-06-03T06:00:00Z', clockOutUtc: '2026-06-03T14:30:00Z', workedMinutes: 510, expectedMinutes: 510, balanceMinutes: 0 }),
    day('2026-06-06', 'Saturday'),
    day('2026-06-10', 'Wednesday', { clockInUtc: '2026-06-10T06:00:00Z', inProgress: true, expectedMinutes: 510, isToday: true }),
    day('2026-06-11', 'Thursday', { expectedMinutes: 510, isFuture: true }),
  ],
  monthToDate: { workedMinutes: 1500, expectedMinutes: 1530, balanceMinutes: -30 },
  wholeMonth: { expectedMinutes: 10620 },
}

function renderSheet() {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => sheet }))
  return render(<Timesheet />)
}

it('shows the agreed 7 columns (plus a screen-reader-only actions column) and every day', async () => {
  renderSheet()

  await screen.findByRole('table')
  expect(screen.getAllByRole('columnheader').map((h) => h.textContent)).toEqual(['Date', 'Day', 'Clock-in', 'Clock-out', 'Worked', 'Expected', 'Balance', 'Actions'])
  expect(screen.getAllByRole('row')).toHaveLength(1 + sheet.days.length)
})

it('formats balance compactly and colours it: green extra, red less, grey exact', async () => {
  renderSheet()
  await screen.findByRole('table')

  const row = (date: string) => document.querySelector(`tr[data-date="${date}"]`) as HTMLElement
  const bal = (date: string) => within(row(date)).getAllByRole('cell')[6]
  expect(bal('2026-06-01').textContent).toBe('+1h'); expect(bal('2026-06-01').className).toContain('bal--plus')
  expect(bal('2026-06-02').textContent).toBe('-1h 30m'); expect(bal('2026-06-02').className).toContain('bal--minus')
  expect(bal('2026-06-03').textContent).toBe('0'); expect(bal('2026-06-03').className).toContain('bal--zero')
})

it('future days show Expected only, today shows in progress without Worked/Balance, a non-working day is empty', async () => {
  renderSheet()
  await screen.findByRole('table')

  const cells = (date: string) => within(document.querySelector(`tr[data-date="${date}"]`) as HTMLElement).getAllByRole('cell').slice(0, 7).map((c) => c.textContent)
  expect(cells('2026-06-11')).toEqual(['11.06.2026', 'Thu', '', '', '', '8h 30m', ''])           // future
  expect(cells('2026-06-10')[3]).toContain('in progress'); expect(cells('2026-06-10')[4]).toBe(''); expect(cells('2026-06-10')[6]).toBe('')   // today, running
  expect(cells('2026-06-06')).toEqual(['06.06.2026', 'Sat', '', '', '', '', ''])                   // Saturday
  expect(document.querySelector('tr[data-date="2026-06-11"]')?.className).toContain('row--future')
})

it('marks only the manually set field as manual (orange) and shows month-to-date and whole-month blocks', async () => {
  renderSheet()
  await screen.findByRole('table')

  const cells = within(document.querySelector('tr[data-date="2026-06-02"]') as HTMLElement).getAllByRole('cell')
  expect(cells[2].className).not.toContain('manual')       // clock-in is a real punch
  expect(cells[3].className).toContain('manual')           // clock-out set manually
  expect(cells[3].textContent).toContain('Manual')
  expect(screen.getByText('Month to date')).toBeTruthy()
  expect(screen.getByText('Whole month')).toBeTruthy()
  expect(screen.getByText('177h')).toBeTruthy()            // whole-month expected 10620 min
})
