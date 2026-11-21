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
