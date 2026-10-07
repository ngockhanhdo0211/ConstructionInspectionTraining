import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from 'react'
import { configureApi, login as loginRequest } from '../api/client'
import type { User } from '../api/types'

type AuthValue = { token: string | null; user: User | null; login(email: string, password: string): Promise<void>; logout(): void }
const AuthContext = createContext<AuthValue | null>(null)
const key = 'construction-inspection.auth'

export function AuthProvider({ children }: { children: ReactNode }) {
  const [session, setSession] = useState<{ token: string; user: User } | null>(() => {
    try { return JSON.parse(sessionStorage.getItem(key) ?? 'null') } catch { return null }
  })
  const logout = useCallback(() => { sessionStorage.removeItem(key); setSession(null) }, [])
  // Configure synchronously so child query effects cannot run with the default
  // anonymous token provider during the provider's first commit.
  configureApi(() => session?.token ?? null, logout)
  const value = useMemo<AuthValue>(() => ({ token: session?.token ?? null, user: session?.user ?? null,
    login: async (email, password) => { const response = await loginRequest(email, password); const next = { token: response.accessToken, user: response.user }; sessionStorage.setItem(key, JSON.stringify(next)); setSession(next) }, logout }), [session, logout])
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}
export function useAuth() { const value = useContext(AuthContext); if (!value) throw new Error('AuthProvider is required.'); return value }
