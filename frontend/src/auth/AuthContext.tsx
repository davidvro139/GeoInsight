import { useMemo, useState, type ReactNode } from 'react'
import { getStoredToken, setStoredToken } from '@/api/client'
import { AuthContext, type AuthContextValue, type CurrentUser } from './auth-context'

const USER_STORAGE_KEY = 'geoinsight.user'

function loadStoredUser(): CurrentUser | null {
  const raw = localStorage.getItem(USER_STORAGE_KEY)
  if (!raw) return null
  try {
    return JSON.parse(raw) as CurrentUser
  } catch {
    return null
  }
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<CurrentUser | null>(() =>
    getStoredToken() ? loadStoredUser() : null,
  )

  const value = useMemo<AuthContextValue>(
    () => ({
      user,
      isAuthenticated: user !== null,
      signIn: (response) => {
        const currentUser: CurrentUser = {
          userId: response.userId,
          email: response.email,
          displayName: response.displayName,
        }
        setStoredToken(response.token)
        localStorage.setItem(USER_STORAGE_KEY, JSON.stringify(currentUser))
        setUser(currentUser)
      },
      signOut: () => {
        setStoredToken(null)
        localStorage.removeItem(USER_STORAGE_KEY)
        setUser(null)
      },
    }),
    [user],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}
