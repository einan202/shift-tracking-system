import { useEffect, useState, type FormEvent } from 'react'
import { api, errorText } from '../../api'
import { formatMinutes } from '../../format'

/** The default shift is a duration only (no start time); it is the baseline of every monthly report. */
export default function ShiftSettings() {
  const [minutes, setMinutes] = useState<number | null>(null)
  const [msg, setMsg] = useState<{ kind: 'success' | 'error'; text: string } | null>(null)

  useEffect(() => {
    api<{ durationMinutes: number }>('/admin/settings/shift').then((r) => r.data && setMinutes(r.data.durationMinutes)).catch(() => {})
  }, [])

  async function save(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    const f = new FormData(e.currentTarget)
    const total = Number(f.get('hours')) * 60 + Number(f.get('minutes'))
    const r = await api<{ durationMinutes: number }>('/admin/settings/shift', { method: 'PUT', body: { durationMinutes: total } })
    if (r.ok && r.data) { setMinutes(r.data.durationMinutes); setMsg({ kind: 'success', text: 'Saved.' }) }
    else setMsg({ kind: 'error', text: errorText(r) })
  }

  if (minutes === null) return <section className="panel" aria-busy="true"><p>Loading…</p></section>
  return (
    <section className="panel">
      <h2>Default shift duration</h2>
      <p className="muted">Currently {formatMinutes(minutes)}. Employees see the difference to this value for each day.</p>
      <form onSubmit={save} className="row row--wrap" key={minutes}>
        <label>Hours<input name="hours" type="number" min={0} max={24} defaultValue={Math.floor(minutes / 60)} required /></label>
        <label>Minutes<input name="minutes" type="number" min={0} max={59} defaultValue={minutes % 60} required /></label>
        <button className="btn btn--primary">Save</button>
      </form>
      {msg && <p role={msg.kind === 'error' ? 'alert' : 'status'} className={`notice notice--${msg.kind}`}>{msg.text}</p>}
    </section>
  )
}
