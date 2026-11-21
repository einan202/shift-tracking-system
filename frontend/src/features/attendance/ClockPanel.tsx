import { useCallback, useEffect, useState } from 'react'
import { formatTime } from '../../format'
import { describeStatus, realClockApi, runPunch, type ClockApi, type Notice, type StatusBody } from './punch'

type Phase = 'loading' | 'idle' | 'sending' | 'error'

/**
 * Clock In / Clock Out. The button is disabled while a request is in flight (stops double clicks) and the
 * state always comes from the server (GET /status), never from the browser clock. Times shown are the
 * ones the server stored, taken from the external Europe/Zurich time API.
 */
export default function ClockPanel({ client = realClockApi, onChange }: { client?: ClockApi; onChange?: () => void }) {
  const [phase, setPhase] = useState<Phase>('loading')
  const [status, setStatus] = useState<StatusBody | null>(null)
  const [notice, setNotice] = useState<Notice | null>(null)

  const loadStatus = useCallback(async () => {
    try {
      const res = await client.status()
      if (res.ok && res.data) { setStatus(res.data); setPhase('idle') } else setPhase('error')
    } catch {
      setPhase('error')
    }
  }, [client])

  useEffect(() => { void loadStatus() }, [loadStatus])

  async function punch() {
    const action = status?.clockedIn ? 'clock-out' : 'clock-in'
    setPhase('sending')
    setNotice(null)
    const outcome = await runPunch(action, client)
    if (outcome.status) setStatus(outcome.status)
    setNotice(outcome.notice)
    setPhase(outcome.status ? 'idle' : status ? 'idle' : 'error')
    onChange?.()
  }

  if (phase === 'loading') return <section className="panel punch" aria-busy="true"><p>Loading your status…</p></section>
  if (phase === 'error' && !status) {
    return (
      <section className="panel punch">
        <p role="alert" className="notice notice--error">Could not load your status.</p>
        <button className="btn" onClick={() => { setPhase('loading'); void loadStatus() }}>Try again</button>
      </section>
    )
  }

  const clockedIn = status?.clockedIn ?? false
  return (
    <section className="panel punch" aria-label="Clock">
      <p className="punch__state">
        {clockedIn && status?.record ? (
          <>On shift since <strong className="num">{formatTime(status.record.clockInUtc)}</strong> <span className="muted">Zurich time</span></>
        ) : status ? describeStatus(status) : ''}
      </p>
      <button
        className={`punch__button ${clockedIn ? 'punch__button--out' : 'punch__button--in'}`}
        onClick={punch}
        disabled={phase === 'sending'}
      >
        {phase === 'sending' ? 'Contacting time service…' : clockedIn ? 'Clock out' : 'Clock in'}
      </button>
      <div aria-live="polite">
        {notice && <p className={`notice notice--${notice.kind}`}>{notice.text}</p>}
      </div>
    </section>
  )
}

