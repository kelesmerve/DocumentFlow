import type { DocumentStatus } from '../types/api'

const labels: Record<DocumentStatus, string> = {
  Draft: 'Taslak',
  PendingApproval: 'Onay bekliyor',
  Approved: 'Onaylandı',
  Rejected: 'Reddedildi',
  Signed: 'İmzalandı',
  Archived: 'Arşivlendi',
}

export function DocumentStatusBadge({ status }: { status: DocumentStatus }) {
  return <span className={`status-badge status-${status.toLowerCase()}`}>{labels[status] ?? status}</span>
}
