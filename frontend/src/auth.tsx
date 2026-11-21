import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { api, errorText, onUnauthorized, tokenStore } from './api'

export type Role = 'Admin' | 'Employee'
export type User = { role: Role; fullName: string }

type AuthState = {
  user: User | null
  login: (email: string, password: string) => Promise<string | null> // returns an error message, or null on success
  logout: () => void
}

const USER_KEY = 'clockapp.user'
const AuthContext = createContext<AuthState | null>(null)

function loadUser(): User | null {
  try {
    return tokenStore.get() ? (JSON.parse(sessionStorage.getItem(USER_KEY) ?? 'null') as User | null) : null
  } catch {
    return null
  }
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(loadUser)

  const logout = useCallback(() => {
    tokenStore.clear()
    sessionStorage.removeItem(USER_KEY)
    setUser(null)
  }, [])

  // Any 401 (expired token, deactivated account) ends the session.
  useEffect(() => {
    onUnauthorized(logout)
    return () => onUnauthorized(null)
  }, [logout])

  const login = useCallback(async (email: string, password: string) => {
    const res = await api<{ token: string; role: Role; fullName: string }>('/auth/login', { body: { email, password } })
    if (!res.ok || !res.data) return errorText(res, 'Could not sign in.')
    const next = { role: res.data.role, fullName: res.data.fullName }
    tokenStore.set(res.data.token)
    sessionStorage.setItem(USER_KEY, JSON.stringify(next))
    setUser(next)
    return null
  }, [])

  const value = useMemo(() => ({ user, login, logout }), [user, login, logout])
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth(): AuthState {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used inside AuthProvider')
  return ctx
}
