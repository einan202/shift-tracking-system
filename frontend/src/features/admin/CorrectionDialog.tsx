import { useState, type FormEvent } from 'react'
import { api, errorText } from '../../api'
import { toLocalInput } from '../../format'

export type CorrectionTarget =
  | { kind: 'Add'; userId: number }
  | { kind: 'Adjust'; userId: number; recordId: number; clockInUtc: string; clockOutUtc: string | null }

/**
 * Admin correction (never edits the original punch; it appends an audit row with a mandatory reason).
 * Times are typed as Zurich wall-clock time. The offset selects exist only for the repeated hour when
 * daylight saving ends, where one local time means two different instants.
 */
export default function CorrectionDialog({ target, onClose, onSaved }: { target: CorrectionTarget; onClose: () => void; onSaved: () => void }) {
  const initialIn = target.kind === 'Adjust' ? toLocalInput(target.clockInUtc) : ''
  const initialOut = target.kind === 'Adjust' && target.clockOutUtc ? toLocalInput(target.clockOutUtc) : ''
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function submit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    const f = new FormData(e.currentTarget)
    const inVal = String(f.get('in') ?? '')
    const outVal = String(f.get('out') ?? '')
    const offset = (name: string) => (f.get(name) ? Number(f.get(name)) : null)
    setBusy(true)
    setError(null)
    // For an adjustment only changed fields are sent; the server keeps the other side as it is.
    const changedIn = target.kind === 'Add' || inVal !== initialIn
    const changedOut = target.kind === 'Add' || outVal !== initialOut
    try {
      const r = await api('/admin/attendance/corrections', {
        body: {
          kind: target.kind,
          userId: target.userId,
          attendanceRecordId: target.kind === 'Adjust' ? target.recordId : null,
          clockInLocal: changedIn && inVal ? inVal : null,
          clockOutLocal: changedOut && outVal ? outVal : null,
          clockInOffsetMinutes: changedIn ? offset('inOffset') : null,
          clockOutOffsetMinutes: changedOut ? offset('outOffset') : null,
          reason: String(f.get('reason')),
        },
      })
      if (r.ok) onSaved()
      else setError(errorText(r))
    } catch {
      setError('Cannot reach the server.')
    } finally {
      setBusy(false)
    }
  }

  const offsets = (name: string) => (
    <select name={name} defaultValue="" aria-label="UTC offset (only for the repeated hour)">
      <option value="">automatic</option>
      <option value="120">+02:00 (summer time)</option>
      <option value="60">+01:00 (winter time)</option>
    </select>
  )

  return (
    <div className="overlay" role="dialog" aria-modal="true" aria-labelledby="corr-title">
      <form className="dialog stack" onSubmit={submit}>
        <h2 id="corr-title">{target.kind === 'Add' ? 'Add missing record' : 'Correct record'}</h2>
        <label>Clock in (Zurich time)
          <input name="in" type="datetime-local" defaultValue={initialIn} required={target.kind === 'Add'} />
        </label>
        <label>Clock out (Zurich time)
          <input name="out" type="datetime-local" defaultValue={initialOut} required={target.kind === 'Add' || !!initialOut} />
        </label>
        <details>
          <summary>Daylight-saving repeated hour (last Sunday of October, 02:00–03:00)</summary>
          <div className="row row--wrap">
            <label>Clock-in offset {offsets('inOffset')}</label>
            <label>Clock-out offset {offsets('outOffset')}</label>
          </div>
        </details>
        <label>Reason (required, kept in the audit trail)
          <textarea name="reason" required minLength={5} maxLength={300} rows={3} />
        </label>
        {error && <p role="alert" className="notice notice--error">{error}</p>}
        <div className="row">
          <button className="btn btn--primary" disabled={busy}>{busy ? 'Saving…' : 'Save correction'}</button>
          <button type="button" className="btn" onClick={onClose}>Cancel</button>
        </div>
      </form>
    </div>
  )
}
