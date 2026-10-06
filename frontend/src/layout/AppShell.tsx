import { Archive, CheckCheck, ChevronRight, FileSignature, Files, LayoutDashboard, LogOut, ScrollText } from 'lucide-react'
import type { ReactNode } from 'react'
import { NavLink } from 'react-router-dom'
import { useAuth } from '../auth/useAuth'
import { initials } from '../utils/format'

const roleLabels = { Employee: 'Çalışan', Manager: 'Yönetici', Admin: 'Yönetici' } as const

export function AppShell({ children }: { children: ReactNode }) {
  const { user, logout } = useAuth()
  if (!user) return null

  return <div className="app-shell">
    <aside className="sidebar" aria-label="Ana menü">
      <NavLink className="brand" to="/" aria-label="DocumentFlow genel bakış">
        <span className="brand-mark"><Files size={18} strokeWidth={1.8} /></span>
        <span className="brand-name">Document<span>Flow</span></span>
      </NavLink>
      <div className="workspace-label">ÇALIŞMA ALANI</div>
      <nav className="primary-nav">
        <NavLink to="/" end className={({ isActive }) => `nav-link${isActive ? ' active' : ''}`}><LayoutDashboard size={17} /><span>Genel Bakış</span></NavLink>
        <div className="nav-section-label">DOSYALAR</div>
        <NavLink to="/documents" className={({ isActive }) => `nav-link${isActive ? ' active' : ''}`}><Files size={17} /><span>Belgelerim</span></NavLink>
        {(user.role === 'Manager' || user.role === 'Admin') ? <NavLink to="/approvals" className={({ isActive }) => `nav-link${isActive ? ' active' : ''}`}><CheckCheck size={17} /><span>{user.role === 'Admin' ? 'Onay talepleri' : 'Onaylarım'}</span></NavLink> : null}
        <button className="nav-link nav-disabled" disabled title="Bu özellik daha sonra eklenecek"><FileSignature size={17} /><span>İmzalarım</span><small>Yakında</small></button>
        <button className="nav-link nav-disabled" disabled title="Bu özellik daha sonra eklenecek"><Archive size={17} /><span>Arşiv</span><small>Yakında</small></button>
        <div className="nav-section-label management-label">YÖNETİM</div>
        <button className="nav-link nav-disabled" disabled title="Bu özellik daha sonra eklenecek"><ScrollText size={17} /><span>İşlem Geçmişi</span></button>
      </nav>
      <div className="sidebar-footer">
        <div className="profile-avatar" aria-hidden="true">{initials(user.firstName, user.lastName)}</div>
        <div className="profile-copy"><strong>{user.firstName} {user.lastName}</strong><span>{roleLabels[user.role]}</span></div>
        <button className="logout-button" onClick={logout} aria-label="Çıkış yap" title="Çıkış yap"><LogOut size={17} /></button>
      </div>
    </aside>
    <div className="workspace">
      <header className="topbar"><div className="breadcrumb"><span>DocumentFlow</span><ChevronRight size={14} /><span>Çalışma alanı</span></div><div className="topbar-user"><span>{user.firstName} {user.lastName}</span><span className="topbar-avatar">{initials(user.firstName, user.lastName)}</span></div></header>
      <main className="page-content">{children}</main>
    </div>
  </div>
}
