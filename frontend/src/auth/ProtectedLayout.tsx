import { Navigate, Outlet, useLocation } from 'react-router-dom'
import axios from 'axios'
import { useAuth } from './useAuth'
import { AppShell } from '../layout/AppShell'
import { ErrorNotice, LoadingState } from '../components/Ui'

export function ProtectedLayout() {
  const auth = useAuth()
  const location = useLocation()
  if (!auth.token || (axios.isAxiosError(auth.error) && auth.error.response?.status === 401)) {
    return <Navigate to="/login" replace state={{ from: location.pathname }} />
  }
  if (auth.isLoading) return <div className="full-screen-state"><LoadingState label="Oturum doğrulanıyor…" /></div>
  if (auth.error) return <div className="full-screen-state"><ErrorNotice onRetry={auth.retryUser}>Kullanıcı bilgileri alınamadı. Bağlantınızı kontrol edin.</ErrorNotice><button className="button button-secondary" onClick={auth.logout}>Çıkış yap</button></div>
  if (!auth.user) return <Navigate to="/login" replace state={{ from: location.pathname }} />
  return <AppShell><Outlet /></AppShell>
}
