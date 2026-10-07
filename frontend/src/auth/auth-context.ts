import { createContext, useContext } from 'react'

export interface CurrentUser {
  userId: number
  email: string
  displayName: string
}

export interface AuthContextValue {
  user: CurrentUser | null
  isAuthenticated: boolean
  signIn: (response: import('@/api/auth').AuthResponse) => void
  signOut: () => void
}

export const AuthContext = createContext<AuthContextValue | null>(null)

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext)
  if (!context) {
    throw new Error('useAuth must be used within an AuthProvider')
  }
  return context
}
