import type { ReactNode } from 'react'
import { Navigate } from 'react-router-dom'
import { useAuth } from './useAuth'

export function ManagerOnly({ children }: { children: ReactNode }) {
  const { user } = useAuth()
  return user?.role === 'Manager' ? children : <Navigate to="/" replace />
}
