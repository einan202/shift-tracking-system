import { useCallback, useEffect, useState } from 'react'
import { api } from '../../api'
import { formatDay, formatRange, formatStamp, formatTime } from '../../format'
import CorrectionDialog, { type CorrectionTarget } from './CorrectionDialog'
import type { Employee } from './Employees'

type Hist = {
  id: number; kind: string; previousClockInUtc: string | null; previousClockOutUtc: string | null
  correctedClockInUtc: string | null; correctedClockOutUtc: string | null; reason: string; createdBy: string | null; createdAtUtc: string
}
type Row = {
  recordId: number | null; source: 'punch' | 'manual'
  original: { clockInUtc: string; clockOutUtc: string | null } | null
  effective: { clockInUtc: string; clockOutUtc: string | null; workDate: string }
  isAdjusted: boolean; isVoided?: boolean; isOpen: boolean; history: Hist[]
}
type View = { from: string; to: string; zurichNow: string; records: Row[] }

const pad = (n: number) => String(n).padStart(2, '0')
const daysIn = (y: number, m: number) => [31, (y % 4 === 0 && y % 100 !== 0) || y % 400 === 0 ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31][m - 1]
const range = (y: number, m: number) => `from=${y}-${pad(m)}-01&to=${y}-${pad(m)}-${pad(daysIn(y, m))}`

/** Admin view of one employee's month: original punches, effective values, and the audit history. */
export default function AttendanceAdmin() {
  const [employees, setEmployees] = useState<Employee[]>([])
  const [userId, setUserId] = useState<number | null>(null)
  const [ym, setYm] = useState<{ year: number; month: number } | null>(null) // null = server's current month
  const [view, setView] = useState<View | null>(null)
  const [dialog, setDialog] = useState<CorrectionTarget | null>(null)
  const [error, setError] = useState(false)

  useEffect(() => {
    api<Employee[]>('/admin/employees').then((r) => {
      if (r.ok && r.data) { setEmployees(r.data); setUserId((cur) => cur ?? r.data![0]?.id ?? null) }
    }).catch(() => setError(true))
  }, [])

  const load = useCallback(async () => {
    if (userId === null) return
    try {
      const q = ym ? `&${range(ym.year, ym.month)}` : ''
      const r = await api<View>(`/admin/attendance?userId=${userId}${q}`)
      if (r.ok && r.data) { setView(r.data); setError(false) } else setError(true)
    } catch { setError(true) }
  }, [userId, ym])
  useEffect(() => { void load() }, [load])

  // Reload before opening so the dialog's Zurich "now" (used for its defaults) is current, not from page load.
  const openDialog = async (target: CorrectionTarget) => {
    await load()
    setDialog(target)
  }

  const shown = view ? { year: Number(view.from.slice(0, 4)), month: Number(view.from.slice(5, 7)) } : null
  const step = (d: number) => {
    if (!shown) return
    const idx = shown.year * 12 + shown.month - 1 + d
    setYm({ year: Math.floor(idx / 12), month: (idx % 12) + 1 })
  }

  return (
    <section className="panel">
      <header className="panel__head">
        <h2>Attendance</h2>
        <div className="row row--wrap">
          <label className="inline">Employee
            <select value={userId ?? ''} onChange={(e) => { setUserId(Number(e.target.value)); setYm(null) }}>
              {employees.map((e) => <option key={e.id} value={e.id}>{e.fullName}</option>)}
            </select>
          </label>
          {shown && (
            <>
              <button className="btn btn--small" onClick={() => step(-1)} aria-label="Previous month">‹</button>
              <strong className="num">{shown.year}-{pad(shown.month)}</strong>
              <button className="btn btn--small" onClick={() => step(1)} aria-label="Next month">›</button>
            </>
          )}
          {userId !== null && <button className="btn" onClick={() => void openDialog({ kind: 'Add', userId })}>Add missing record</button>}
        </div>
      </header>

      {error && <p role="alert" className="notice notice--error">Could not load attendance.</p>}
      {view && view.records.length === 0 && <p className="muted">No records in this month.</p>}
      {view && view.records.length > 0 && (
        <div className="table-wrap">
          <table>
            <thead><tr><th>Day</th><th>Recorded (API time)</th><th>Counts as</th><th /></tr></thead>
            <tbody>
              {view.records.map((r) => (
                <tr key={r.recordId ?? `m${r.history[0]?.id}`}>
                  <td>{formatDay(r.effective.workDate)}</td>
                  <td className="num">
                    {r.original ? `${formatTime(r.original.clockInUtc)} – ${r.original.clockOutUtc ? formatTime(r.original.clockOutUtc) : 'open'}` : <span className="muted">manual entry</span>}
                  </td>
                  <td className={`num ${r.isVoided ? 'voided' : ''}`}>
                    {r.isVoided && <span className="tag tag--warn">deleted</span>}
                    {formatTime(r.effective.clockInUtc)} – {r.effective.clockOutUtc ? formatTime(r.effective.clockOutUtc) : <em>missing clock-out</em>}
                    {r.source === 'manual' && <span className="tag">added</span>}
                    {r.isAdjusted && !r.isVoided && <span className="tag">corrected</span>}
                    {r.history.length > 0 && (
                      <details>
                        <summary>History ({r.history.length})</summary>
                        <ul className="history">
                          {r.history.map((h) => (
                            <li key={h.id}>
                              <strong>{h.kind}</strong> by {h.createdBy ?? 'admin'} on {formatStamp(h.createdAtUtc)}: “{h.reason}”
                              <br />
                              {h.kind === 'Adjust' && <><span className="muted">was {formatRange(h.previousClockInUtc, h.previousClockOutUtc)}</span><br /></>}
                              <span className="muted">{h.kind === 'Void' ? `shift ${formatRange(h.previousClockInUtc, h.previousClockOutUtc)} deleted` : `now ${formatRange(h.correctedClockInUtc, h.correctedClockOutUtc)}`}</span>
                            </li>
                          ))}
                        </ul>
                      </details>
                    )}
                  </td>
                  <td className="r">
                    {r.recordId !== null && userId !== null && (
                      <button className="btn btn--small" onClick={() => void openDialog({
                        kind: 'Adjust', userId, recordId: r.recordId!, clockInUtc: r.effective.clockInUtc, clockOutUtc: r.effective.clockOutUtc,
                      })}>Correct</button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {dialog && view && <CorrectionDialog target={dialog} zurichNow={view.zurichNow} onClose={() => setDialog(null)} onSaved={() => { setDialog(null); void load() }} />}
    </section>
  )
}
