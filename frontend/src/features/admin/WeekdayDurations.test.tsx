import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { useState } from 'react'
import { afterEach, expect, it } from 'vitest'
import WeekdayDurations, { defaultWeek } from './WeekdayDurations'

afterEach(cleanup)

function Harness({ onChange }: { onChange: (v: number[]) => void }) {
  const [value, setValue] = useState(defaultWeek(510))
  return <WeekdayDurations value={value} fallbackMinutes={510} onChange={(v) => { setValue(v); onChange(v) }} />
}

it('defaults to the company duration Monday-Friday with the weekend off', () => {
  expect(defaultWeek(510)).toEqual([510, 510, 510, 510, 510, 0, 0])
  render(<Harness onChange={() => {}} />)

  expect((screen.getByLabelText('Monday hours') as HTMLInputElement).value).toBe('8')
  expect((screen.getByLabelText('Monday minutes') as HTMLInputElement).value).toBe('30')
  expect((screen.getByRole('checkbox', { name: 'Saturday' }) as HTMLInputElement).checked).toBe(false)
  expect((screen.getByLabelText('Saturday hours') as HTMLInputElement).disabled).toBe(true)
})

it('each weekday has its own duration and any weekdays can be selected (not a range)', () => {
  let last: number[] = []
  render(<Harness onChange={(v) => { last = v }} />)

  fireEvent.change(screen.getByLabelText('Friday hours'), { target: { value: '6' } })       // Friday 6h30 (minutes stay 30)
  fireEvent.click(screen.getByRole('checkbox', { name: 'Tuesday' }))                       // untick Tuesday: a gap in the week
  fireEvent.click(screen.getByRole('checkbox', { name: 'Sunday' }))                        // tick Sunday: back to the company default

  expect(last).toEqual([510, 0, 510, 510, 390, 0, 510])
})

it('keeps durations within 0..24 hours', () => {
  let last: number[] = []
  render(<Harness onChange={(v) => { last = v }} />)

  fireEvent.change(screen.getByLabelText('Monday hours'), { target: { value: '99' } })

  expect(last[0]).toBe(1440)
})
