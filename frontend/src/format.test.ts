import { expect, it } from 'vitest'
import { formatRange, formatBalance, formatDate, formatHours } from './format'

it('formats the balance compactly with a sign', () => {
  expect(formatBalance(180)).toBe('+3h')
  expect(formatBalance(-66)).toBe('-1h 6m')
  expect(formatBalance(45)).toBe('+45m')
  expect(formatBalance(-480)).toBe('-8h')
  expect(formatBalance(0)).toBe('0')
})

it('formats durations and dates', () => {
  expect(formatHours(510)).toBe('8h 30m')
  expect(formatHours(360)).toBe('6h')
  expect(formatHours(0)).toBe('0')
  expect(formatDate('2026-11-21')).toBe('21.11.2026')
})

it('shows the date once for a same-day range', () => {
  expect(formatRange('2026-11-21T18:12:00Z', '2026-11-21T18:14:00Z')).toBe('21.11.26, 19:12 – 19:14')
  expect(formatRange('2026-11-21T18:12:00Z', '2026-11-22T18:14:00Z')).toBe('21.11.26, 19:12 – 22.11.26, 19:14')
  expect(formatRange('2026-11-21T18:12:00Z', null)).toBe('21.11.26, 19:12 – open')
})
