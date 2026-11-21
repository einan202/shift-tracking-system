import { useState } from 'react'
import { AuthProvider, useAuth } from './auth'
import { takeInviteToken } from './inviteLink'
import Admin from './pages/Admin'
import Employee from './pages/Employee'
import Login from './pages/Login'
import SetPassword from './pages/SetPassword'

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
        <strong className="brand">Shift tracking</strong>
        <span className="topbar__user">{user.fullName} <span className="tag">{user.role}</span></span>
        <button className="btn btn--small" onClick={logout}>Sign out</button>
      </header>
      <main className="page">{user.role === 'Admin' ? <Admin /> : <Employee />}</main>
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
