import { useEffect, useState } from 'react'
import { api } from '../../api'
import { formatDay, formatDiff, formatMinutes, formatTime } from '../../format'

type Entry = { in: string; out: string | null; isManual: boolean; isAdjusted: boolean; isOpen: boolean }
type Day = { date: string; records: Entry[]; workedMinutes: number; differenceMinutes: number | null; hasOpenRecord: boolean }
type Report = {
  year: number; month: number; defaultShiftMinutes: number; days: Day[]
  totals: { workedMinutes: number; expectedMinutes: number; differenceMinutes: number; daysWorked: number }
}

const monthName = (y: number, m: number) =>
  new Intl.DateTimeFormat('en-GB', { month: 'long', year: 'numeric', timeZone: 'UTC' }).format(new Date(Date.UTC(y, m - 1, 1)))

/** The employee's own month: per-day totals compared with the admin-set default shift duration. */
export default function MonthReport({ refreshKey }: { refreshKey: number }) {
  // null = let the server pick the current Zurich month (the browser clock is never used for this).
  const [ym, setYm] = useState<{ year: number; month: number } | null>(null)
  const [report, setReport] = useState<Report | null>(null)
  const [error, setError] = useState(false)

  useEffect(() => {
    let cancelled = false
    const q = ym ? `?year=${ym.year}&month=${ym.month}` : ''
    api<Report>(`/attendance/me/month${q}`)
      .then((r) => { if (!cancelled) { if (r.ok && r.data) { setReport(r.data); setError(false) } else setError(true) } })
      .catch(() => { if (!cancelled) setError(true) })
    return () => { cancelled = true }
  }, [ym, refreshKey])

  if (error) return <section className="panel"><p role="alert" className="notice notice--error">Could not load the month.</p></section>
  if (!report) return <section className="panel" aria-busy="true"><p>Loading…</p></section>

  const step = (delta: number) => {
    const idx = report.year * 12 + (report.month - 1) + delta
    setYm({ year: Math.floor(idx / 12), month: (idx % 12) + 1 })
  }

  return (
    <section className="panel" aria-label="Monthly report">
      <header className="panel__head">
        <h2>{monthName(report.year, report.month)}</h2>
        <div className="row">
          <button className="btn btn--small" onClick={() => step(-1)} aria-label="Previous month">‹</button>
          <button className="btn btn--small" onClick={() => step(1)} aria-label="Next month">›</button>
        </div>
      </header>

      {report.days.length === 0 ? (
        <p className="muted">No attendance recorded this month.</p>
      ) : (
        <div className="table-wrap">
          <table>
            <thead>
              <tr><th>Day</th><th>Times</th><th className="r">Worked</th><th className="r">vs {formatMinutes(report.defaultShiftMinutes)}</th></tr>
            </thead>
            <tbody>
              {report.days.map((d) => (
                <tr key={d.date}>
                  <td>{formatDay(d.date)}</td>
                  <td>
                    {d.records.map((r, i) => (
                      <div key={i} className="num">
                        {formatTime(r.in)} – {r.out ? formatTime(r.out) : <em>missing clock-out</em>}
                        {r.isManual && <span className="tag">added by admin</span>}
                        {r.isAdjusted && <span className="tag">corrected</span>}
                      </div>
                    ))}
                  </td>
                  <td className="r num">{formatMinutes(d.workedMinutes)}</td>
                  <td className={`r num ${d.differenceMinutes == null ? '' : d.differenceMinutes < 0 ? 'neg' : 'pos'}`}>
                    {d.differenceMinutes == null ? '–' : formatDiff(d.differenceMinutes)}
                  </td>
                </tr>
              ))}
            </tbody>
            <tfoot>
              <tr>
                <th colSpan={2}>Month total ({report.totals.daysWorked} days)</th>
                <th className="r num">{formatMinutes(report.totals.workedMinutes)}</th>
                <th className="r num">{formatDiff(report.totals.differenceMinutes)}</th>
              </tr>
            </tfoot>
          </table>
        </div>
      )}
      {report.days.some((d) => d.hasOpenRecord) && (
        <p className="hint">Days with a missing clock-out are not counted. Ask an administrator to correct them.</p>
      )}
    </section>
  )
}
