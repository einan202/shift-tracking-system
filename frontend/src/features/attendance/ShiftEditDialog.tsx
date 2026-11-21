import { useEffect, useState } from 'react'
import { api } from '../../api'
import { formatBalance, formatDate, formatHours, formatTime } from '../../format'
import type { SheetDay } from './Timesheet'

type Reason = { code: string; label: string }
type Side = {
  day: { clockInUtc: string | null; clockOutUtc: string | null; clockInIsManual: boolean; clockOutIsManual: boolean; inProgress: boolean; workedMinutes: number | null; balanceMinutes: number | null }
  monthToDate: { balanceMinutes: number }
}
type Preview = { before: Side; after: Side }

const hhmm = (iso: string | null) => (iso ? formatTime(iso).replace('.', ':') : '')

/** One side of the effective before/after summary: only the consolidated result, never punches or history. */
function Summary({ title, s }: { title: string; s: Side }) {
  const d = s.day
  return (
    <div className="summary__block">
      <h3>{title}</h3>
      <p className="num">
        {d.clockInUtc ? formatTime(d.clockInUtc) : '–'} → {d.clockOutUtc ? formatTime(d.clockOutUtc) : d.inProgress ? 'open' : '–'}
        {d.workedMinutes != null && <> · Worked {formatHours(d.workedMinutes)} · Balance {d.balanceMinutes != null ? formatBalance(d.balanceMinutes) : ''}</>}
      </p>
    </div>
  )
}

/**
 * Employee edit of one day's shift: set the clock-in and/or clock-out (Zurich time) with a reason, preview the
 * effective before/after, then confirm. Times are sent as Zurich wall-clock text; the browser clock is never read.
 */
export default function ShiftEditDialog({ day, onClose, onSaved }: { day: SheetDay; onClose: () => void; onSaved: () => void }) {
  const hasShift = day.clockInUtc != null
  const [reasons, setReasons] = useState<Reason[]>([])
  const [clockIn, setClockIn] = useState(hhmm(day.clockInUtc))
  const [clockOut, setClockOut] = useState(hhmm(day.clockOutUtc))
  const [code, setCode] = useState(hasShift && day.clockOutUtc == null ? 'ForgotClockOut' : hasShift ? 'WrongTimePunched' : 'ForgotClockIn')
  const [text, setText] = useState('')
  const [preview, setPreview] = useState<Preview | null>(null)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  useEffect(() => { void api<Reason[]>('/attendance/reasons').then((r) => { if (r.ok && r.data) setReasons(r.data) }) }, [])

  const body = () => ({
    date: day.date,
    clockInLocal: clockIn && clockIn !== hhmm(day.clockInUtc) ? `${day.date}T${clockIn}:00` : undefined,
    clockOutLocal: clockOut && clockOut !== hhmm(day.clockOutUtc) ? `${day.date}T${clockOut}:00` : undefined,
    // A day without a shift needs both times, so send them even when equal to the (empty) current values.
    ...(hasShift ? {} : { clockInLocal: clockIn ? `${day.date}T${clockIn}:00` : undefined, clockOutLocal: clockOut ? `${day.date}T${clockOut}:00` : undefined }),
    reasonCode: code, reasonText: text,
  })

  async function send(commit: boolean) {
    setBusy(true); setError('')
    try {
      const r = await api<Preview & { error?: string }>(commit ? '/attendance/me/shifts' : '/attendance/me/shifts/preview', { body: body() })
      if (r.ok && r.data) { if (commit) onSaved(); else setPreview(r.data) }
      else setError((r.data as { error?: string } | null)?.error ?? 'Could not save. Nothing was changed.')
    } catch { setError('The server could not be reached. Nothing was changed.') }
    finally { setBusy(false) }
  }

  return (
    <div className="overlay" role="dialog" aria-modal="true" aria-labelledby="shift-title">
      <form className="dialog stack" onSubmit={(e) => { e.preventDefault(); void send(preview != null) }}>
        <h2 id="shift-title">{hasShift ? 'Edit shift' : 'Add shift'} · {formatDate(day.date)}</h2>
        <p className="hint">Times are Europe/Zurich time. Your original punches are kept; this adds a reasoned correction.</p>
        <div className="row row--wrap">
          <label>Clock-in <input type="time" value={clockIn} onChange={(e) => { setClockIn(e.target.value); setPreview(null) }} /></label>
          <label>Clock-out <input type="time" value={clockOut} onChange={(e) => { setClockOut(e.target.value); setPreview(null) }} /></label>
        </div>
        <label>Reason
          <select value={code} onChange={(e) => { setCode(e.target.value); setPreview(null) }}>
            {reasons.map((r) => <option key={r.code} value={r.code}>{r.label}</option>)}
          </select>
        </label>
        {code === 'Other' && (
          <label>Describe the reason <input value={text} maxLength={300} onChange={(e) => { setText(e.target.value); setPreview(null) }} /></label>
        )}
        {preview && (
          <div className="summary">
            <Summary title="Before" s={preview.before} />
            <Summary title="After" s={preview.after} />
          </div>
        )}
        {error && <p role="alert" className="notice notice--error">{error}</p>}
        <div className="row">
          <button className="btn btn--primary" disabled={busy}>{busy ? 'Working…' : preview ? 'Confirm and save' : 'Preview'}</button>
          <button type="button" className="btn" onClick={onClose}>Cancel</button>
        </div>
      </form>
    </div>
  )
}
