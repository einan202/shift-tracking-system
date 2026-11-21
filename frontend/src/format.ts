// Display helpers. Instants arrive from the API as ISO strings and are shown in Europe/Zurich.
// `new Date(iso)` only PARSES a server value; nothing here reads the current time.

const ZONE = 'Europe/Zurich'
const time = new Intl.DateTimeFormat('de-CH', { timeZone: ZONE, hour: '2-digit', minute: '2-digit', hour12: false })
const day = new Intl.DateTimeFormat('de-CH', { timeZone: ZONE, weekday: 'short', day: '2-digit', month: '2-digit', year: 'numeric' })
const stamp = new Intl.DateTimeFormat('de-CH', { timeZone: ZONE, dateStyle: 'short', timeStyle: 'short', hour12: false })
const parts = new Intl.DateTimeFormat('en-CA', {
  timeZone: ZONE, year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', hour12: false,
})

export const formatTime = (iso: string) => time.format(new Date(iso))
export const formatDay = (isoDate: string) => day.format(new Date(`${isoDate}T12:00:00Z`)) // date-only value, noon UTC avoids day shifts
export const formatStamp = (iso: string) => stamp.format(new Date(iso))

/** "2026-11-20T08:05" in Zurich local time, the format <input type="datetime-local"> uses. */
export function toLocalInput(iso: string): string {
  const p = Object.fromEntries(parts.formatToParts(new Date(iso)).map((x) => [x.type, x.value]))
  const hour = p.hour === '24' ? '00' : p.hour
  return `${p.year}-${p.month}-${p.day}T${hour}:${p.minute}`
}

/** 510 -> "8 h 30 min"; negative values keep their sign. */
export function formatMinutes(total: number): string {
  const sign = total < 0 ? '-' : ''
  const abs = Math.abs(total)
  const h = Math.floor(abs / 60)
  const m = abs % 60
  if (h === 0) return `${sign}${m} min`
  return m === 0 ? `${sign}${h} h` : `${sign}${h} h ${m} min`
}

export const formatDiff = (minutes: number) => (minutes > 0 ? `+${formatMinutes(minutes)}` : formatMinutes(minutes))

/** Compact balance: `+3h`, `-1h 6m`, `+45m`, `0`. */
export function formatBalance(minutes: number): string {
  if (minutes === 0) return '0'
  const sign = minutes < 0 ? '-' : '+'
  const abs = Math.abs(minutes)
  const h = Math.floor(abs / 60)
  const m = abs % 60
  return `${sign}${[h ? `${h}h` : '', m ? `${m}m` : ''].filter(Boolean).join(' ')}`
}

/** Compact duration without sign: `8h 30m`, `45m`, `6h`. */
export function formatHours(minutes: number): string {
  const h = Math.floor(minutes / 60)
  const m = minutes % 60
  return [h ? `${h}h` : '', m ? `${m}m` : ''].filter(Boolean).join(' ') || '0'
}

/** 2026-11-21 -> 21.11.2026 (plain calendar date, no time zone involved). */
export const formatDate = (iso: string) => `${iso.slice(8, 10)}.${iso.slice(5, 7)}.${iso.slice(0, 4)}`

/** "21.11.26, 19:12 – 19:14": the date is shown once when both ends fall on the same Zurich day; a missing end is "open". */
export function formatRange(from: string | null, to: string | null): string {
  const a = from ? formatStamp(from) : 'open'
  if (!to) return `${a} – open`
  const b = formatStamp(to)
  return a.split(', ')[0] === b.split(', ')[0] ? `${a} – ${b.split(', ')[1]}` : `${a} – ${b}`
}
