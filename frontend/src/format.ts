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
