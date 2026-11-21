import { useEffect, useState } from 'react'
import { api, errorText } from '../../api'
import { formatHours } from '../../format'
import WeekdayDurations, { WEEKDAYS, defaultWeek } from './WeekdayDurations'

type Settings = { durationMinutes: number; minutes: number[] }

/** Company default expected duration for each weekday. It only prefills new employees; each employee's schedule stays independent. */
export default function ShiftSettings() {
  const [saved, setSaved] = useState<Settings | null>(null)
  const [week, setWeek] = useState<number[]>(defaultWeek(480))
  const [msg, setMsg] = useState<{ kind: 'success' | 'error'; text: string } | null>(null)

  useEffect(() => {
    api<Settings>('/admin/settings/shift').then((r) => { if (r.data) { const w = r.data.minutes ?? defaultWeek(r.data.durationMinutes); setSaved({ ...r.data, minutes: w }); setWeek(w) } }).catch(() => {})
  }, [])

  async function save() {
    const r = await api<Settings>('/admin/settings/shift', { method: 'PUT', body: { minutes: week } })
    if (r.ok && r.data) { setSaved(r.data); setWeek(r.data.minutes); setMsg({ kind: 'success', text: 'Saved.' }) }
    else setMsg({ kind: 'error', text: errorText(r) })
  }

  if (!saved) return <section className="panel" aria-busy="true"><p>Loading…</p></section>
  const summary = WEEKDAYS.map((d, i) => (saved.minutes[i] > 0 ? `${d.slice(0, 3)} ${formatHours(saved.minutes[i])}` : null)).filter(Boolean).join(', ')
  return (
    <section className="panel narrow-panel settings-panel">
      <h2>Default working week</h2>
      <p className="muted">Current default: {summary}. New employees start with this week; you can change each day for each employee. Existing employees are not affected.</p>
      <div className="stack">
        <WeekdayDurations value={week} onChange={(w) => { setWeek(w); setMsg(null) }} fallbackMinutes={saved.durationMinutes} />
        <div className="row"><button className="btn btn--primary" onClick={() => void save()}>Save default week</button></div>
      </div>
      {msg && <p role={msg.kind === 'error' ? 'alert' : 'status'} className={`notice notice--${msg.kind}`}>{msg.text}</p>}
    </section>
  )
}
