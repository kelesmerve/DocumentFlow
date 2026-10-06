import { useState, type FormEvent } from 'react'
import { ArrowRight, Files } from 'lucide-react'
import { Navigate, useLocation, useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/useAuth'
import { apiErrorMessage } from '../api/client'

export function LoginPage() {
  const { token, user, login, isLoading } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const destination = (location.state as { from?: string } | null)?.from ?? '/'

  if (token && (user || isLoading)) return <Navigate to="/" replace />

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setError('')
    setSubmitting(true)
    try {
      await login(email.trim(), password)
      navigate(destination, { replace: true })
    } catch (submitError) {
      setError(apiErrorMessage(submitError, 'Giriş yapılamadı. E-posta ve şifrenizi kontrol edin.'))
    } finally {
      setSubmitting(false)
    }
  }

  return <main className="login-page">
    <section className="login-panel" aria-labelledby="login-title">
      <div className="login-brand"><span className="brand-mark"><Files size={18} /></span><span className="brand-name">Document<span>Flow</span></span></div>
      <p className="eyebrow login-eyebrow">GÜVENLİ ÇALIŞMA ALANI</p>
      <h1 id="login-title">Hesabınıza giriş yapın</h1>
      <p className="login-subtitle">Belgelerinize güvenli erişim için kurumsal hesabınızla devam edin.</p>
      {error && <div className="form-error" role="alert">{error}</div>}
      <form className="login-form" onSubmit={handleSubmit}>
        <label htmlFor="email">E-posta</label>
        <input id="email" type="email" autoComplete="username" value={email} onChange={(event) => setEmail(event.target.value)} placeholder="ad.soyad@kurum.com" required autoFocus />
        <label htmlFor="password">Şifre</label>
        <input id="password" type="password" autoComplete="current-password" value={password} onChange={(event) => setPassword(event.target.value)} placeholder="Şifrenizi girin" required />
        <button className="button button-primary login-submit" type="submit" disabled={submitting}>
          {submitting ? 'Kontrol ediliyor…' : 'Giriş yap'} {!submitting && <ArrowRight size={16} />}
        </button>
      </form>
      <p className="login-footnote">Erişim sorunu yaşıyorsanız sistem yöneticinizle iletişime geçin.</p>
    </section>
    <div className="login-side-note"><span className="side-note-line" /><span>Dokümanlarınız, tek bir güvenli yerde.</span></div>
  </main>
}
