import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { ArrowUpRight, ChevronLeft, ChevronRight } from 'lucide-react'
import { Link } from 'react-router-dom'
import { approvalsApi } from '../api/approvals'
import { useAuth } from '../auth/useAuth'
import { ErrorNotice, LoadingState, PageHeading } from '../components/Ui'
import { formatDate } from '../utils/format'
import type { ApprovalStatus } from '../types/api'

const labels: Record<ApprovalStatus, string> = { Pending: 'Bekliyor', Approved: 'Onaylandı', Rejected: 'Reddedildi', RevisionRequested: 'Revizyon istendi' }
export function ApprovalsPage() {
  const { user } = useAuth()
  const [page, setPage] = useState(1)
  const query = useQuery({ queryKey: ['approvals', page, user?.id], queryFn: () => approvalsApi.list(page) })
  const data = query.data
  const pageCount = Math.max(1, Math.ceil((data?.totalCount ?? 0) / (data?.pageSize ?? 20)))
  return <>
    <PageHeading title={user?.role === 'Admin' ? 'Onay talepleri' : 'Onaylarım'} description={user?.role === 'Admin' ? 'Kurumdaki onay süreçlerinin görünümü.' : 'Size atanmış belge onay talepleri.'} />
    {query.isPending && <LoadingState label="Onay talepleri yükleniyor…" />}
    {query.isError && <ErrorNotice onRetry={() => void query.refetch()}>Onay talepleri alınamadı.</ErrorNotice>}
    {data && !data.items.length && <div className="empty-state"><h2>Henüz onay talebi yok</h2><p>Size atanan talepler burada görünecek.</p></div>}
    {data && data.items.length > 0 && <>
      <div className="list-toolbar"><span>{data.totalCount} talep</span><span>Sayfa {page} / {pageCount}</span></div>
      <div className="table-wrap"><table className="data-table approval-table"><thead><tr><th>Belge No</th><th>Başlık</th><th>Versiyon</th><th>Talep eden</th><th>Tarih</th><th>Durum</th><th><span className="sr-only">İşlem</span></th></tr></thead><tbody>
        {data.items.map((item) => <tr key={item.id}><td><Link className="table-link doc-number" to={`/approvals/${item.id}`}>{item.documentNumber}</Link></td><td><Link className="table-link doc-title" to={`/approvals/${item.id}`}>{item.documentTitle}</Link></td><td>v{item.versionNumber}</td><td>{item.requestedBy}</td><td>{formatDate(item.createdAtUtc)}</td><td><span className={`status-badge approval-${item.status.toLowerCase()}`}>{labels[item.status]}</span></td><td><Link className="icon-link" aria-label="Onay detayını aç" to={`/approvals/${item.id}`}><ArrowUpRight size={16} /></Link></td></tr>)}
      </tbody></table></div>
      <nav className="pagination" aria-label="Onay sayfaları"><button className="button button-secondary button-small" onClick={() => setPage((p) => p - 1)} disabled={page <= 1 || query.isFetching}><ChevronLeft size={16} />Önceki</button><span>{page} / {pageCount}</span><button className="button button-secondary button-small" onClick={() => setPage((p) => p + 1)} disabled={page >= pageCount || query.isFetching}>Sonraki<ChevronRight size={16} /></button></nav>
    </>}
  </>
}
