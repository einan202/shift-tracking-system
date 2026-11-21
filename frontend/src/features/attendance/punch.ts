import { api, errorText, NetworkError, type ApiResult } from '../../api'
import { formatTime } from '../../format'

export type AttendanceRecord = {
  id: number
  clockInUtc: string
  clockOutUtc: string | null
  workDate: string
  provider: string
}
export type StatusBody = { clockedIn: boolean; record: AttendanceRecord | null }
export type PunchAction = 'clock-in' | 'clock-out'

export type Notice = { kind: 'success' | 'info' | 'error'; text: string }
export type PunchOutcome = { status: StatusBody | null; notice: Notice }

export type ClockApi = {
  punch: (action: PunchAction) => Promise<ApiResult<StatusBody>>
  status: () => Promise<ApiResult<StatusBody>>
}

export const realClockApi: ClockApi = {
  punch: (action) => api<StatusBody>(`/attendance/${action}`, { method: 'POST' }),
  status: () => api<StatusBody>('/attendance/status'),
}

const describe = (s: StatusBody) =>
  s.clockedIn && s.record
    ? `You are clocked in since ${formatTime(s.record.clockInUtc)} (Zurich time).`
    : s.record?.clockOutUtc
      ? `You are clocked out. Last shift ended at ${formatTime(s.record.clockOutUtc)} (Zurich time).`
      : 'You are not clocked in.'

/**
 * One Clock In / Clock Out attempt, including recovery from failures.
 *
 * A lost response does NOT mean a lost punch: the server may have recorded it. So after a network error or a
 * 409 we never guess, we ask GET /status (the source of truth) and report what really happened.
 * The punch is never queued or retried automatically, because its time must come from the server's time API
 * at the moment the server receives it.
 */
export async function runPunch(action: PunchAction, client: ClockApi = realClockApi): Promise<PunchOutcome> {
  let res: ApiResult<StatusBody>
  try {
    res = await client.punch(action)
  } catch (e) {
    if (!(e instanceof NetworkError)) throw e
    return reconcile(action, client)
  }

  if (res.ok && res.data?.record) {
    const t = formatTime(action === 'clock-in' ? res.data.record.clockInUtc : res.data.record.clockOutUtc!)
    return {
      status: res.data,
      notice: { kind: 'success', text: `${action === 'clock-in' ? 'Clocked in' : 'Clocked out'} at ${t} (Zurich time).` },
    }
  }
  if (res.status === 409) {
    // The click was valid but the state already changed (double click, other tab, retry after a lost response).
    const status = await safeStatus(client)
    return { status, notice: { kind: 'info', text: `${errorText(res)} ${status ? describe(status) : ''}`.trim() } }
  }
  if (res.status === 503) {
    return { status: null, notice: { kind: 'error', text: errorText(res, 'The time service is unavailable. Nothing was recorded.') } }
  }
  return { status: null, notice: { kind: 'error', text: errorText(res) } }
}

/** After a timeout / dropped connection: find out from the server whether the punch was recorded. */
async function reconcile(action: PunchAction, client: ClockApi): Promise<PunchOutcome> {
  const status = await safeStatus(client)
  if (!status) {
    return { status: null, notice: { kind: 'error', text: 'No connection. We could not confirm whether your punch was recorded. Reload to check.' } }
  }
  const recorded = action === 'clock-in' ? status.clockedIn : !status.clockedIn && status.record?.clockOutUtc != null
  if (recorded) {
    return { status, notice: { kind: 'success', text: `The connection dropped, but your punch was recorded. ${describe(status)}` } }
  }
  return { status, notice: { kind: 'error', text: 'Not recorded. Please try again.' } }
}

async function safeStatus(client: ClockApi): Promise<StatusBody | null> {
  try {
    const r = await client.status()
    return r.ok ? r.data : null
  } catch {
    return null
  }
}

export { describe as describeStatus }
