import { useState, type FormEvent } from 'react'
import { api, errorText } from '../api'

/** Invite / reset link: the employee chooses their own password; the administrator never sees it. */
export default function SetPassword({ token }: { token: string }) {
  const [error, setError] = useState<string | null>(null)
  const [done, setDone] = useState(false)

  async function submit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    const form = new FormData(e.currentTarget)
    const password = String(form.get('password'))
    if (password !== String(form.get('confirm'))) return setError('The two passwords do not match.')
    try {
      const res = await api('/auth/set-password', { body: { token, password } })
      if (res.ok) setDone(true)
      else setError(errorText(res))
    } catch {
      setError('Cannot reach the server. Try again.')
    }
  }

  if (done) {
    return (
      <main className="narrow">
        <h1>Password saved</h1>
        <p><a href="/">Go to sign in</a></p>
      </main>
    )
  }
  return (
    <main className="narrow">
      <h1>Choose your password</h1>
      <form onSubmit={submit} className="stack">
        <label>New password (at least 10 characters)
          <input name="password" type="password" minLength={10} autoComplete="new-password" required />
        </label>
        <label>Repeat password<input name="confirm" type="password" minLength={10} autoComplete="new-password" required /></label>
        {error && <p role="alert" className="notice notice--error">{error}</p>}
        <button className="btn btn--primary">Save password</button>
      </form>
    </main>
  )
}
