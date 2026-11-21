import { useEffect, useState } from 'react'
import { api } from '../../api'
import { formatDate, formatHours, formatTime } from '../../format'
import type { SheetDay } from './Timesheet'

type Reason = { code: string; label: string }

/** Alert + reason before deleting a day's shift. The original punches are kept; admins still see the history. */
export default function DeleteShiftDialog({ day, onClose, onDeleted }: { day: SheetDay; onClose: () => void; onDeleted: () => void }) {
  const [reasons, setReasons] = useState<Reason[]>([])
  const [code, setCode] = useState('DuplicateOrAccidental')
  const [text, setText] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  useEffect(() => { void api<Reason[]>('/attendance/reasons').then((r) => { if (r.ok && r.data) setReasons(r.data) }) }, [])

  async function submit(e: React.FormEvent) {
    e.preventDefault()
    setBusy(true); setError('')
    try {
      const r = await api<{ error?: string }>('/attendance/me/shifts/delete', { body: { date: day.date, reasonCode: code, reasonText: text } })
      if (r.ok) onDeleted()
      else setError(r.data?.error ?? 'Could not delete. Nothing was changed.')
    } catch { setError('The server could not be reached. Nothing was changed.') }
    finally { setBusy(false) }
  }

  return (
    <div className="overlay" role="alertdialog" aria-modal="true" aria-labelledby="del-title">
      <form className="dialog stack" onSubmit={submit}>
        <h2 id="del-title">Delete shift · {formatDate(day.date)}</h2>
        <p className="notice notice--error">
          This removes the shift {day.clockInUtc ? formatTime(day.clockInUtc) : ''} – {day.clockOutUtc ? formatTime(day.clockOutUtc) : ''}
          {day.workedMinutes != null && <> ({formatHours(day.workedMinutes)})</>} from your timesheet. The day will count as no work
          {day.expectedMinutes > 0 && <> and its balance becomes −{formatHours(day.expectedMinutes)}</>}.
        </p>
        <label>Reason
          <select value={code} onChange={(e) => setCode(e.target.value)}>{reasons.map((r) => <option key={r.code} value={r.code}>{r.label}</option>)}</select>
        </label>
        {code === 'Other' && <label>Describe the reason <input value={text} maxLength={300} onChange={(e) => setText(e.target.value)} /></label>}
        {error && <p role="alert" className="notice notice--error">{error}</p>}
        <div className="row">
          <button className="btn btn--danger" disabled={busy}>{busy ? 'Deleting…' : 'Delete shift'}</button>
          <button type="button" className="btn" onClick={onClose}>Keep it</button>
        </div>
      </form>
    </div>
  )
}
