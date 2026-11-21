import { useState, type FormEvent } from 'react'
import { useAuth } from '../auth'

export default function Login() {
  const { login } = useAuth()
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function submit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    const form = new FormData(e.currentTarget)
    setBusy(true)
    setError(null)
    try {
      setError(await login(String(form.get('email')), String(form.get('password'))))
    } catch {
      setError('Cannot reach the server. Check your connection and try again.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <main className="narrow auth">
      <p className="auth__brand" aria-hidden="true">Shift Tracking</p>
      <h1 className="sr-only">Shift Tracking</h1>
      <section className="panel auth__card">
      <h2>Sign in</h2>
      <form onSubmit={submit} className="stack">
        <label>Email<input name="email" type="email" autoComplete="username" required /></label>
        <label>Password<input name="password" type="password" autoComplete="current-password" required /></label>
        {error && <p role="alert" className="notice notice--error">{error}</p>}
        <button className="btn btn--primary" disabled={busy}>{busy ? 'Signing in…' : 'Sign in'}</button>
      </form>
      <p className="hint">No password yet? Open the link your administrator gave you.</p>
      </section>
    </main>
  )
}
