import { createContext } from 'react'
import type { User } from '../types/api'

export interface AuthContextValue {
  token: string | null
  user: User | undefined
  isLoading: boolean
  error: Error | null
  login: (email: string, password: string) => Promise<void>
  logout: () => void
  retryUser: () => void
}

export const AuthContext = createContext<AuthContextValue | null>(null)
