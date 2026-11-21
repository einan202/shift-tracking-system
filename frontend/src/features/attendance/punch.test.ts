import { describe, expect, it } from 'vitest'
import type { ApiResult } from '../../api'
import { NetworkError } from '../../api'
import { runPunch, type ClockApi, type StatusBody } from './punch'

const res = (status: number, data: unknown): ApiResult<StatusBody> => ({ ok: status >= 200 && status < 300, status, data: data as StatusBody | null })
const IN = '2026-02-02T08:00:00Z'
const OUT = '2026-02-02T16:00:00Z'
const open: StatusBody = { clockedIn: true, record: { id: 1, clockInUtc: IN, clockOutUtc: null, workDate: '2026-02-02', provider: 'x' } }
const closed: StatusBody = { clockedIn: false, record: { id: 1, clockInUtc: IN, clockOutUtc: OUT, workDate: '2026-02-02', provider: 'x' } }
const none: StatusBody = { clockedIn: false, record: null }

const client = (punch: ClockApi['punch'], status: ClockApi['status']): ClockApi => ({ punch, status })
const boom = async () => { throw new NetworkError('x') }

describe('runPunch', () => {
  it('reports the server time on success', async () => {
    const out = await runPunch('clock-in', client(async () => res(200, open), async () => res(200, open)))
    expect(out.notice.kind).toBe('success')
    expect(out.notice.text).toContain('09:00') // 08:00Z = 09:00 Zurich (winter)
    expect(out.status).toEqual(open)
  })

  it('after a timeout, asks /status and confirms the punch was recorded', async () => {
    const out = await runPunch('clock-in', client(boom, async () => res(200, open)))
    expect(out.notice.kind).toBe('success')
    expect(out.notice.text).toContain('recorded')
    expect(out.status?.clockedIn).toBe(true)
  })

  it('after a timeout, says "not recorded" when /status shows nothing happened', async () => {
    const out = await runPunch('clock-in', client(boom, async () => res(200, none)))
    expect(out.notice).toEqual({ kind: 'error', text: 'Not recorded. Please try again.' })
  })

  it('after a clock-out timeout, recognises the recorded clock-out', async () => {
    const out = await runPunch('clock-out', client(boom, async () => res(200, closed)))
    expect(out.notice.kind).toBe('success')
  })

  it('if /status is also unreachable, tells the user to reload instead of guessing', async () => {
    const out = await runPunch('clock-in', client(boom, boom))
    expect(out.notice.kind).toBe('error')
    expect(out.notice.text).toContain('Reload')
    expect(out.status).toBeNull()
  })

  it('turns a 409 into an explanation based on /status', async () => {
    const out = await runPunch('clock-in', client(async () => res(409, { error: 'You are already clocked in.' }), async () => res(200, open)))
    expect(out.notice.kind).toBe('info')
    expect(out.notice.text).toContain('since 09:00')
  })

  it('shows 503 as nothing recorded', async () => {
    const out = await runPunch('clock-in', client(async () => res(503, { error: 'The time service is unavailable.' }), async () => res(200, none)))
    expect(out.notice.kind).toBe('error')
    expect(out.notice.text).toContain('time service')
  })
})
