export const WEEKDAYS = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'] as const

/** Default for a new employee: the company default duration Monday-Friday, weekend off. */
export const defaultWeek = (minutes: number): number[] => [minutes, minutes, minutes, minutes, minutes, 0, 0]

/**
 * Seven weekday rows: tick the days the employee works and give each its own expected duration (hours and minutes).
 * An unticked day is 0 (not a working day). No start or end times: only a duration per day.
 */
export default function WeekdayDurations({ value, onChange, fallbackMinutes }: { value: number[]; onChange: (next: number[]) => void; fallbackMinutes: number }) {
  const set = (i: number, minutes: number) => onChange(value.map((m, j) => (j === i ? Math.max(0, Math.min(1440, minutes)) : m)))
  return (
    <fieldset className="weekdays">
      <legend>Working days and expected duration</legend>
      {WEEKDAYS.map((day, i) => {
        const working = value[i] > 0
        return (
          <div key={day} className="weekdays__row">
            <label className="weekdays__day">
              <input type="checkbox" checked={working} onChange={(e) => set(i, e.target.checked ? fallbackMinutes : 0)} />
              {day}
            </label>
            <label className="weekdays__field">
              <span className="sr-only">{day} hours</span>
              <input type="number" min={0} max={24} disabled={!working} value={Math.floor(value[i] / 60)}
                onChange={(e) => set(i, Number(e.target.value) * 60 + (value[i] % 60))} aria-label={`${day} hours`} /> h
            </label>
            <label className="weekdays__field">
              <input type="number" min={0} max={59} disabled={!working} value={value[i] % 60}
                onChange={(e) => set(i, Math.floor(value[i] / 60) * 60 + Number(e.target.value))} aria-label={`${day} minutes`} /> min
            </label>
          </div>
        )
      })}
    </fieldset>
  )
}
