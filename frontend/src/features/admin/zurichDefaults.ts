// Defaults for the "Add missing record" form, derived from the SERVER's Zurich time (not the browser's
// time zone, which may differ, e.g. Israel). Values use the datetime-local format "YYYY-MM-DDTHH:mm".

const minutes = (hhmm: string) => Number(hhmm.slice(0, 2)) * 60 + Number(hhmm.slice(3, 5))
const hhmm = (m: number) => `${String(Math.floor(m / 60)).padStart(2, '0')}:${String(m % 60).padStart(2, '0')}`

/**
 * A typical day (08:00-17:00) on the current Zurich date, never later than Zurich "now", so the defaults can
 * be saved without hitting "cannot be in the future".
 */
export function defaultAddTimes(zurichNow: string): { in: string; out: string } {
  const date = zurichNow.slice(0, 10)
  const now = minutes(zurichNow.slice(11, 16))
  const out = Math.min(17 * 60, now)
  const start = Math.max(0, Math.min(8 * 60, out - 30))
  return { in: `${date}T${hhmm(start)}`, out: `${date}T${hhmm(out)}` }
}
