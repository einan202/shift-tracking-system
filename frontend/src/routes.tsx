import type { ReactElement } from 'react'
import AttendanceAdmin from './features/admin/AttendanceAdmin'
import Employees from './features/admin/Employees'
import ShiftSettings from './features/admin/ShiftSettings'
import ClockPanel from './features/attendance/ClockPanel'
import MonthReport from './features/attendance/MonthReport'
import type { Role } from './auth'

export type Route = { path: string; label: string; element: () => ReactElement }

// The first route of each role is its default section. Role guards below only let a user reach their own routes.
const EMPLOYEE_ROUTES: Route[] = [
  { path: '/clock', label: 'Clock', element: () => <div className="narrow-panel"><ClockPanel /></div> },
  { path: '/timesheet', label: 'Timesheet', element: () => <MonthReport refreshKey={0} /> },
]

const ADMIN_ROUTES: Route[] = [
  { path: '/attendance', label: 'Attendance', element: () => <AttendanceAdmin /> },
  { path: '/employees', label: 'Employees', element: () => <Employees /> },
  { path: '/settings', label: 'Settings', element: () => <ShiftSettings /> },
]

export const routesFor = (role: Role): Route[] => (role === 'Admin' ? ADMIN_ROUTES : EMPLOYEE_ROUTES)

/** The route to show for a path, or null when the path is not one of this role's sections (caller redirects). */
export const matchRoute = (role: Role, path: string): Route | null => routesFor(role).find((r) => r.path === path) ?? null
