import { cleanup, render, screen } from '@testing-library/react'
import { afterEach, expect, it } from 'vitest'
import CorrectionDialog from './CorrectionDialog'

const noop = () => {}
afterEach(cleanup)

it('defaults a new record to Zurich time and shows the Zurich date and time', () => {
  render(<CorrectionDialog target={{ kind: 'Add', userId: 1 }} zurichNow="2026-11-21T13:57" onClose={noop} onSaved={noop} />)

  expect((screen.getByLabelText('Clock in (Zurich time)') as HTMLInputElement).value).toBe('2026-11-21T08:00')
  expect((screen.getByLabelText('Clock out (Zurich time)') as HTMLInputElement).value).toBe('2026-11-21T13:57')
  expect(screen.getByText(/It is now 2026-11-21 13:57 in Zurich/)).toBeTruthy()
})

it('has no browser-side max, which would go stale and block valid current times (regression)', () => {
  render(<CorrectionDialog target={{ kind: 'Add', userId: 1 }} zurichNow="2026-11-21T13:57" onClose={noop} onSaved={noop} />)

  expect((screen.getByLabelText('Clock in (Zurich time)') as HTMLInputElement).max).toBe('')
  expect((screen.getByLabelText('Clock out (Zurich time)') as HTMLInputElement).max).toBe('')
})

it('pre-fills a missing clock-out with Zurich now, not the browser time zone (regression)', () => {
  render(
    <CorrectionDialog
      target={{ kind: 'Adjust', userId: 1, recordId: 7, clockInUtc: '2026-11-21T07:00:00Z', clockOutUtc: null }}
      zurichNow="2026-11-21T15:41" onClose={noop} onSaved={noop}
    />,
  )

  expect((screen.getByLabelText('Clock in (Zurich time)') as HTMLInputElement).value).toBe('2026-11-21T08:00') // 07:00Z shown in Zurich time
  expect((screen.getByLabelText('Clock out (Zurich time)') as HTMLInputElement).value).toBe('2026-11-21T15:41')
  expect(screen.getByText(/clear the field to leave the record open/)).toBeTruthy()
})

it('keeps the recorded clock-out of a closed record and shows no "missing clock-out" hint', () => {
  render(
    <CorrectionDialog
      target={{ kind: 'Adjust', userId: 1, recordId: 7, clockInUtc: '2026-11-21T07:00:00Z', clockOutUtc: '2026-11-21T15:00:00Z' }}
      zurichNow="2026-11-21T15:41" onClose={noop} onSaved={noop}
    />,
  )

  expect((screen.getByLabelText('Clock out (Zurich time)') as HTMLInputElement).value).toBe('2026-11-21T16:00')
  expect(screen.queryByText(/clear the field to leave the record open/)).toBeNull()
})

