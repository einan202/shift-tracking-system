import { useState } from 'react'
import AttendanceAdmin from '../features/admin/AttendanceAdmin'
import Employees from '../features/admin/Employees'
import ShiftSettings from '../features/admin/ShiftSettings'

const TABS = [
  { id: 'employees', label: 'Employees' },
  { id: 'attendance', label: 'Attendance' },
  { id: 'settings', label: 'Shift settings' },
] as const

export default function Admin() {
  const [tab, setTab] = useState<(typeof TABS)[number]['id']>('employees')
  return (
    <div className="stack">
      <div role="tablist" className="tabs">
        {TABS.map((t) => (
          <button key={t.id} role="tab" aria-selected={tab === t.id} className="tabs__tab" onClick={() => setTab(t.id)}>{t.label}</button>
        ))}
      </div>
      {tab === 'employees' && <Employees />}
      {tab === 'attendance' && <AttendanceAdmin />}
      {tab === 'settings' && <ShiftSettings />}
    </div>
  )
}
