import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { authApi } from '../api/auth'
import { tokenStore } from './tokenStore'
import { AuthContext, type AuthContextValue } from './AuthContext'

export function AuthProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient()
  const [token, setToken] = useState(() => tokenStore.get())
  useEffect(() => tokenStore.subscribe(() => setToken(tokenStore.get())), [])
  const userQuery = useQuery({
    queryKey: ['current-user', token],
    queryFn: authApi.me,
    enabled: Boolean(token),
    retry: false,
    staleTime: 60_000,
  })

  const login = useCallback(async (email: string, password: string) => {
    const result = await authApi.login(email, password)
    tokenStore.set(result.accessToken)
    await queryClient.fetchQuery({
      queryKey: ['current-user', result.accessToken],
      queryFn: authApi.me,
      staleTime: 0,
      retry: false,
    })
    setToken(result.accessToken)
  }, [queryClient])

  const logout = useCallback(() => {
    tokenStore.clear()
    setToken(null)
    queryClient.removeQueries({ queryKey: ['current-user'] })
  }, [queryClient])

  const retryUser = useCallback(() => { void userQuery.refetch() }, [userQuery])
  const value = useMemo<AuthContextValue>(() => ({
    token,
    user: userQuery.data,
    isLoading: Boolean(token) && userQuery.isPending,
    error: userQuery.error,
    login,
    logout,
    retryUser,
  }), [token, userQuery.data, userQuery.isPending, userQuery.error, login, logout, retryUser])

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}
