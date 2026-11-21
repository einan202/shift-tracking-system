import { useEffect, useState } from 'react'
import { AuthProvider, useAuth } from './auth'
import { takeInviteToken } from './inviteLink'
import NavTabs from './components/NavTabs'
import Login from './pages/Login'
import SetPassword from './pages/SetPassword'
import { navigate, usePath } from './router'
import { matchRoute, routesFor } from './routes'

/** Signed-in area: tabs per role, role guard (a path outside the user's sections redirects to their default). */
function Workspace({ role }: { role: 'Admin' | 'Employee' }) {
  const path = usePath()
  const routes = routesFor(role)
  const route = matchRoute(role, path)
  useEffect(() => {
    if (!route) navigate(routes[0].path, true)
  }, [route, routes])

  const shown = route ?? routes[0]
  return (
    <>
      <NavTabs routes={routes} current={shown.path} />
      <div className="stack">{shown.element()}</div>
    </>
  )
}

function Shell() {
  const { user, logout } = useAuth()
  // Invite / reset links open /set-password#token=...; the token is read once and removed from the URL.
  const [inviteToken] = useState(() => (window.location.pathname === '/set-password' ? takeInviteToken() : null))

  if (window.location.pathname === '/set-password') {
    return <SetPassword token={inviteToken ?? ''} />
  }
  if (!user) return <Login />

  return (
    <>
      <header className="topbar">
        <strong className="brand">Shift Tracking</strong>
        <span className="topbar__user">{user.fullName} <span className="tag">{user.role}</span></span>
        <button className="btn btn--small" onClick={logout}>Sign out</button>
      </header>
      <main className="page"><Workspace role={user.role} /></main>
    </>
  )
}

export default function App() {
  return (
    <AuthProvider>
      <Shell />
    </AuthProvider>
  )
}
