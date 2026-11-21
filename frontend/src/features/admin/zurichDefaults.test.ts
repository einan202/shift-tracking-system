import { expect, it } from 'vitest'
import { defaultAddTimes } from './zurichDefaults'

it('uses a normal working day when Zurich time is later than 17:00', () => {
  expect(defaultAddTimes('2026-11-21T19:05')).toEqual({ in: '2026-11-21T08:00', out: '2026-11-21T17:00' })
})

it('never defaults to a time after Zurich now', () => {
  expect(defaultAddTimes('2026-11-21T14:52')).toEqual({ in: '2026-11-21T08:00', out: '2026-11-21T14:52' })
  expect(defaultAddTimes('2026-11-21T08:20')).toEqual({ in: '2026-11-21T07:50', out: '2026-11-21T08:20' })
})

it('stays on the Zurich date just after midnight', () => {
  expect(defaultAddTimes('2026-07-02T00:10')).toEqual({ in: '2026-07-02T00:00', out: '2026-07-02T00:10' })
})
