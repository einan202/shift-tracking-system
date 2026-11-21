import { useState } from 'react'
import ClockPanel from '../features/attendance/ClockPanel'
import MonthReport from '../features/attendance/MonthReport'

export default function Employee() {
  const [refresh, setRefresh] = useState(0)
  return (
    <div className="split">
      <ClockPanel onChange={() => setRefresh((n) => n + 1)} />
      <MonthReport refreshKey={refresh} />
    </div>
  )
}
