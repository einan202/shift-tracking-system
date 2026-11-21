import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react'
import { api, errorText } from '../../api'
import { formatStamp } from '../../format'
import WeekdayDurations, { defaultWeek } from './WeekdayDurations'

export type Employee = {
  id: number; email: string; fullName: string; isActive: boolean; employmentStartDate?: string | null
  hasPassword: boolean; invitePending: boolean; inviteExpiresUtc: string | null
}
type Invite = { name: string; url: string; expires: string }

/** Admin creates the employee; the system returns a one-time link. The admin never sees or sets a password. */
export default function Employees() {
  const [list, setList] = useState<Employee[]>([])
  const [invite, setInvite] = useState<Invite | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [companyDefault, setCompanyDefault] = useState(480)
  const [week, setWeek] = useState<number[]>(defaultWeek(480))
  const [companyWeek, setCompanyWeek] = useState<number[]>(defaultWeek(480))
  const [adding, setAdding] = useState(false)
  const firstField = useRef<HTMLInputElement>(null)
  useEffect(() => { if (adding) firstField.current?.focus() }, [adding])

  const load = useCallback(async () => {
    try {
      const r = await api<Employee[]>('/admin/employees')
      if (r.ok && r.data) setList(r.data)
    } catch { setError('Could not load employees.') }
  }, [])
  useEffect(() => { void load() }, [load])
  // Prefill new employees with the company default duration (Mon-Fri); each weekday can then be changed.
  useEffect(() => {
    api<{ durationMinutes: number; minutes?: number[] }>('/admin/settings/shift').then((r) => {
      if (r.ok && r.data) { const w = r.data.minutes ?? defaultWeek(r.data.durationMinutes); setCompanyDefault(r.data.durationMinutes); setCompanyWeek(w); setWeek(w) }
    }).catch(() => {})
  }, [])

  async function create(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    const form = e.currentTarget
    const f = new FormData(form)
    setError(null)
    const r = await api<{ fullName: string; invitePath: string; inviteExpiresUtc: string }>('/admin/employees', {
      body: {
        email: f.get('email'), fullName: f.get('fullName'),
        employmentStartDate: f.get('startDate') ? String(f.get('startDate')) : null, // empty = today (Zurich date), set by the server
        schedule: { minutes: week, allowsOvernightShifts: f.get('overnight') === 'on' },
      },
    })
    if (!r.ok || !r.data) return setError(errorText(r))
    setInvite({ name: r.data.fullName, url: `${window.location.origin}${r.data.invitePath}`, expires: r.data.inviteExpiresUtc })
    form.reset()
    void load()
  }

  async function reissue(emp: Employee) {
    const r = await api<{ invitePath: string; inviteExpiresUtc: string }>(`/admin/employees/${emp.id}/invite`, { method: 'POST' })
    if (r.ok && r.data) setInvite({ name: emp.fullName, url: `${window.location.origin}${r.data.invitePath}`, expires: r.data.inviteExpiresUtc })
    void load()
  }

  async function toggle(emp: Employee) {
    await api(`/admin/employees/${emp.id}/${emp.isActive ? 'deactivate' : 'activate'}`, { method: 'POST' })
    void load()
  }

  return (
    <div className="stack">
      <section className="panel">
        <header className="panel__head">
          <h2>Employees</h2>
          <button className="btn btn--primary" onClick={() => { setError(null); setInvite(null); setWeek(companyWeek); setAdding(true) }}>Add employee</button>
        </header>
        {invite && !adding && (
          <div className="notice notice--success" role="status">
            <p>One-time link for <strong>{invite.name}</strong> (valid until {formatStamp(invite.expires)}). It is shown only now; send it to the employee.</p>
            <div className="row"><input readOnly value={invite.url} aria-label="Invite link" onFocus={(e) => e.currentTarget.select()} />
              <button className="btn btn--small" onClick={() => void navigator.clipboard?.writeText(invite.url)}>Copy link</button></div>
          </div>
        )}
        <div className="table-wrap">
          <table className="employees">
            <thead><tr><th>Name</th><th className="col-email">Email</th><th className="col-start">Starts</th><th className="col-status">Status</th><th><span className="sr-only">Actions</span></th></tr></thead>
            <tbody>
              {list.map((e) => (
                <tr key={e.id}>
                  <td>{e.fullName}<span className="email-sub">{e.email}</span><span className="status-sub">{!e.isActive ? <span className="tag">deactivated</span>: !e.hasPassword ? <span className="tag">waiting for password</span>: <span className="tag tag--ok">active</span>}</span></td>
                  <td className="email col-email">{e.email}</td>
                  <td className="num col-start">{e.employmentStartDate ?? '–'}</td>
                  <td className="col-status">
                    {!e.isActive ? <span className="tag">deactivated</span>
                      : !e.hasPassword ? <span className="tag">waiting for password</span>
                      : <span className="tag tag--ok">active</span>}
                  </td>
                  <td className="r actions">
                    <button className="btn btn--small" onClick={() => void reissue(e)}>New link</button>{' '}
                    <button className="btn btn--small" onClick={() => void toggle(e)}>{e.isActive ? 'Deactivate' : 'Activate'}</button>
                  </td>
                </tr>
              ))}
              {list.length === 0 && <tr><td colSpan={5} className="muted">No employees yet. Use “Add employee” to create the first one.</td></tr>}
            </tbody>
          </table>
        </div>
      </section>

      {adding && (
        <div className="overlay" role="dialog" aria-modal="true" aria-labelledby="add-emp-title" onKeyDown={(e) => { if (e.key === 'Escape') setAdding(false) }}>
          <div className="dialog dialog--wide stack">
            <h2 id="add-emp-title">{invite ? 'Employee created' : 'Add employee'}</h2>
            {invite ? (
              <>
                <div className="notice notice--success" role="status">
                  <p>One-time link for <strong>{invite.name}</strong> (valid until {formatStamp(invite.expires)}). It is shown only now; send it to the employee.</p>
                  <div className="row"><input readOnly value={invite.url} aria-label="Invite link" onFocus={(e) => e.currentTarget.select()} />
                    <button className="btn btn--small" onClick={() => void navigator.clipboard?.writeText(invite.url)}>Copy link</button></div>
                </div>
                <div className="row"><button className="btn btn--primary" onClick={() => setAdding(false)}>Done</button></div>
              </>
            ) : (
              <form onSubmit={create} className="stack">
                <div className="form-grid">
                  <label>Full name<input ref={firstField} name="fullName" required maxLength={200} autoComplete="off" /></label>
                  <label>Email<input name="email" type="email" required maxLength={256} autoComplete="off" /></label>
                  <label>Start date <span className="muted">(empty = today)</span><input name="startDate" type="date" /></label>
                </div>
                <WeekdayDurations value={week} onChange={setWeek} fallbackMinutes={companyDefault} />
                <label className="inline"><input name="overnight" type="checkbox" /> Overnight shifts allowed (night workers)</label>
                {error && <p role="alert" className="notice notice--error">{error}</p>}
                <div className="row dialog__actions">
                  <button className="btn btn--primary">Create and get link</button>
                  <button type="button" className="btn" onClick={() => setAdding(false)}>Cancel</button>
                </div>
              </form>
            )}
          </div>
        </div>
      )}
    </div>
  )
}
