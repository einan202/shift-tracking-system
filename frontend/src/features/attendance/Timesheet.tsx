import { useCallback, useEffect, useState } from 'react'
import { api } from '../../api'
import ShiftEditDialog from './ShiftEditDialog'
import { formatBalance, formatDate, formatHours, formatTime } from '../../format'

export type SheetDay = {
  date: string; weekday: string; clockInUtc: string | null; clockOutUtc: string | null
  clockInIsManual: boolean; clockOutIsManual: boolean; inProgress: boolean; needsCorrection: boolean
  workedMinutes: number | null; expectedMinutes: number; balanceMinutes: number | null; isToday: boolean; isFuture: boolean
}
export type Sheet = {
  year: number; month: number; zurichToday: string; employmentStartDate: string | null; days: SheetDay[]
  monthToDate: { workedMinutes: number; expectedMinutes: number; balanceMinutes: number }
  wholeMonth: { expectedMinutes: number }
}

const monthName = (y: number, m: number) =>
  new Intl.DateTimeFormat('en-GB', { month: 'long', year: 'numeric', timeZone: 'UTC' }).format(new Date(Date.UTC(y, m - 1, 1)))

/** Colour class for the balance: green extra time, red less than needed, grey exactly as needed (the sign/number keeps it readable without colour). */
const balanceClass = (b: number) => (b > 0 ? 'bal bal--plus' : b < 0 ? 'bal bal--minus' : 'bal bal--zero')

/**
 * The employee's month: every calendar day with ONE consolidated shift (first clock-in, last clock-out, corrections
 * applied). Future days show Expected only; today shows Worked/Balance once the shift is finished. Manually set times
 * are orange with a "Manual" tag. Employees never see punches, reasons or history.
 */
export default function Timesheet({ reloadKey = 0 }: { reloadKey?: number }) {
  // null = let the server pick the current Zurich month (the browser clock is never used for this).
  const [ym, setYm] = useState<{ year: number; month: number } | null>(null)
  const [sheet, setSheet] = useState<Sheet | null>(null)
  const [error, setError] = useState(false)
  const [editing, setEditing] = useState<SheetDay | null>(null)
  const [bump, setBump] = useState(0)

  const load = useCallback(async () => {
    try {
      const r = await api<Sheet>(`/attendance/me/timesheet${ym ? `?year=${ym.year}&month=${ym.month}` : ''}`)
      if (r.ok && r.data) { setSheet(r.data); setError(false) } else setError(true)
    } catch { setError(true) }
  }, [ym])
  useEffect(() => { void load() }, [load, reloadKey, bump])

  if (error) return <section className="panel"><p role="alert" className="notice notice--error">Could not load the timesheet.</p></section>
  if (!sheet) return <section className="panel" aria-busy="true"><p>Loading…</p></section>

  const step = (delta: number) => {
    const idx = sheet.year * 12 + (sheet.month - 1) + delta
    setYm({ year: Math.floor(idx / 12), month: (idx % 12) + 1 })
  }
  const t = sheet.monthToDate

  return (
    <section className="panel" aria-label="Timesheet">
      <header className="panel__head">
        <h2>{monthName(sheet.year, sheet.month)}</h2>
        <div className="row">
          <button className="btn btn--small" onClick={() => step(-1)} aria-label="Previous month">‹</button>
          <button className="btn btn--small" onClick={() => step(1)} aria-label="Next month">›</button>
        </div>
      </header>

      <div className="summary">
        <div className="summary__block">
          <h3>Month to date</h3>
          <dl>
            <div><dt>Worked</dt><dd className="num">{formatHours(t.workedMinutes)}</dd></div>
            <div><dt>Expected</dt><dd className="num">{formatHours(t.expectedMinutes)}</dd></div>
            <div><dt>Balance</dt><dd className={`num ${balanceClass(t.balanceMinutes)}`}>{formatBalance(t.balanceMinutes)}</dd></div>
          </dl>
        </div>
        <div className="summary__block">
          <h3>Whole month</h3>
          <dl><div><dt>Expected</dt><dd className="num">{formatHours(sheet.wholeMonth.expectedMinutes)}</dd></div></dl>
        </div>
      </div>

      <div className="table-wrap">
        <table className="timesheet">
          <thead>
            <tr><th>Date</th><th>Day</th><th>Clock-in</th><th>Clock-out</th><th className="r">Worked</th><th className="r">Expected</th><th className="r">Balance</th><th><span className="sr-only">Actions</span></th></tr>
          </thead>
          <tbody>
            {sheet.days.map((d) => (
              <tr key={d.date} className={`${d.isFuture ? 'row--future' : ''} ${d.isToday ? 'row--today' : ''}`} data-date={d.date}>
                <td className="num">{formatDate(d.date)}</td>
                <td>{d.weekday.slice(0, 3)}</td>
                <td className={`num ${d.clockInIsManual ? 'manual' : ''}`}>
                  {d.clockInUtc ? formatTime(d.clockInUtc) : ''}
                  {d.clockInIsManual && <span className="tag tag--manual">Manual</span>}
                </td>
                <td className={`num ${d.clockOutIsManual ? 'manual' : ''}`}>
                  {d.clockOutUtc ? formatTime(d.clockOutUtc) : ''}
                  {d.clockOutIsManual && <span className="tag tag--manual">Manual</span>}
                  {d.inProgress && <span className="tag">in progress</span>}
                  {d.needsCorrection && <span className="tag tag--warn">needs correction</span>}
                </td>
                <td className="r num">{d.workedMinutes != null ? formatHours(d.workedMinutes) : ''}</td>
                <td className="r num">{d.expectedMinutes > 0 ? formatHours(d.expectedMinutes) : ''}</td>
                <td className={`r num ${d.balanceMinutes != null ? balanceClass(d.balanceMinutes) : ''}`}>{d.balanceMinutes != null ? formatBalance(d.balanceMinutes) : ''}</td>
                <td>{!d.isFuture && (!sheet.employmentStartDate || d.date >= sheet.employmentStartDate) && <button className="btn btn--small" onClick={() => setEditing(d)}>{d.clockInUtc ? 'Edit' : 'Add'}<span className="sr-only"> {formatDate(d.date)}</span></button>}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {editing && <ShiftEditDialog day={editing} onClose={() => setEditing(null)} onSaved={() => { setEditing(null); setBump((b) => b + 1) }} />}
    </section>
  )
}
