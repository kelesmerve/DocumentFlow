import type { ReactNode } from 'react'
import { AlertCircle, LoaderCircle } from 'lucide-react'

export function LoadingState({ label = 'Yükleniyor…' }: { label?: string }) {
  return <div className="loading-state" role="status"><LoaderCircle size={18} className="spin" aria-hidden="true" />{label}</div>
}

export function ErrorNotice({ children, onRetry }: { children: ReactNode; onRetry?: () => void }) {
  return <div className="notice notice-error" role="alert"><AlertCircle size={18} aria-hidden="true" /><div className="notice-content">{children}{onRetry && <button className="text-button" onClick={onRetry}>Yeniden dene</button>}</div></div>
}

export function EmptyState({ title, description, action }: { title: string; description?: string; action?: ReactNode }) {
  return <div className="empty-state"><span className="empty-mark" aria-hidden="true">—</span><h2>{title}</h2>{description && <p>{description}</p>}{action}</div>
}

export function PageHeading({ eyebrow, title, description, action }: { eyebrow?: string; title: string; description?: string; action?: ReactNode }) {
  return <div className="page-heading"><div>{eyebrow && <p className="eyebrow">{eyebrow}</p>}<h1>{title}</h1>{description && <p className="page-description">{description}</p>}</div>{action && <div className="page-heading-action">{action}</div>}</div>
}
