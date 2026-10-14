import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { ChevronLeft, ChevronRight } from 'lucide-react'
import { Link } from 'react-router-dom'
import { auditApi } from '../api/audit'
import { ErrorNotice, LoadingState, PageHeading } from '../components/Ui'
import { formatDate } from '../utils/format'
import type { AuditAction } from '../types/api'

const actions: { value: AuditAction; label: string }[] = [
  { value: 'DocumentCreated', label: 'Belge oluşturuldu' }, { value: 'DocumentVersionUploaded', label: 'Versiyon yüklendi' },
  { value: 'ApprovalSubmitted', label: 'Onaya gönderildi' }, { value: 'ApprovalApproved', label: 'Onaylandı' },
  { value: 'ApprovalRejected', label: 'Reddedildi' }, { value: 'RevisionRequested', label: 'Revizyon istendi' },
  { value: 'DocumentDownloaded', label: 'Dosya indirildi' },
  { value: 'DocumentSigned', label: 'Belge imzalandı' }, { value: 'TimestampCreated', label: 'Zaman damgası oluşturuldu' },
  { value: 'MobileSignatureRequested', label: 'Mobil imza talep edildi' }, { value: 'MobileSignatureApproved', label: 'Mobil imza onaylandı' },
  { value: 'MobileSignatureRejected', label: 'Mobil imza reddedildi' }, { value: 'MobileSignatureCompleted', label: 'Mobil imza tamamlandı' },
  { value: 'WebhookCreated', label: 'Webhook eklendi' }, { value: 'WebhookEnabled', label: 'Webhook etkinleştirildi' },
  { value: 'WebhookDisabled', label: 'Webhook devre dışı bırakıldı' }, { value: 'WebhookDeleted', label: 'Webhook silindi' },
  { value: 'WebhookSecretRotated', label: 'Webhook secret yenilendi' },
]

export function AuditPage() {
  const [action, setAction] = useState('')
  const [userId, setUserId] = useState('')
  const [documentNumber, setDocumentNumber] = useState('')
  const [page, setPage] = useState(1)
  const query = useQuery({ queryKey: ['audit', action, userId, documentNumber, page], queryFn: () => auditApi.list({ action: action as AuditAction | undefined, userId: userId || undefined, documentNumber: documentNumber || undefined, page, pageSize: 20 }) })
  const data = query.data
  const pageCount = Math.max(1, Math.ceil((data?.totalCount ?? 0) / (data?.pageSize ?? 20)))
  const resetPage = (change: () => void) => { change(); setPage(1) }
  return <>
    <PageHeading title="İşlem Geçmişi" description="Sistemde gerçekleştirilen önemli belge işlemlerinin denetim kayıtları." />
    <section className="audit-filters" aria-label="Denetim kayıtlarını filtrele">
      <label>İşlem türü<select value={action} onChange={(event) => resetPage(() => setAction(event.target.value))}><option value="">Tüm işlemler</option>{actions.map((item) => <option key={item.value} value={item.value}>{item.label}</option>)}</select></label>
      <label>Kullanıcı ID<input value={userId} onChange={(event) => resetPage(() => setUserId(event.target.value))} placeholder="Kullanıcı ID" /></label>
      <label>Belge numarası<input value={documentNumber} onChange={(event) => resetPage(() => setDocumentNumber(event.target.value))} placeholder="DOC-2026-…" /></label>
    </section>
    {query.isPending && <LoadingState label="İşlem geçmişi yükleniyor…" />}
    {query.isError && <ErrorNotice onRetry={() => void query.refetch()}>İşlem geçmişi alınamadı.</ErrorNotice>}
    {data && !data.items.length && <div className="empty-state"><h2>Kayıt bulunamadı</h2><p>Seçili filtrelere uygun işlem geçmişi yok.</p></div>}
    {data && data.items.length > 0 && <>
      <div className="list-toolbar"><span>{data.totalCount} kayıt</span><span>Sayfa {page} / {pageCount}</span></div>
      <div className="table-wrap"><table className="data-table audit-table"><thead><tr><th>Tarih</th><th>Kullanıcı</th><th>İşlem</th><th>Belge</th><th>Versiyon</th><th>Açıklama</th></tr></thead><tbody>
        {data.items.map((item) => <tr key={item.id}><td>{formatDate(item.createdAtUtc)}</td><td><span>{item.userName}</span><small className="audit-user-email">{item.userEmail}</small></td><td>{item.actionDisplayName}</td><td>{item.documentId && item.documentNumber ? <Link className="table-link doc-number" to={`/documents/${item.documentId}`}>{item.documentNumber}</Link> : '—'}{item.documentTitle && <small className="audit-user-email">{item.documentTitle}</small>}</td><td>{item.versionNumber ? `v${item.versionNumber}` : '—'}</td><td className="audit-description">{item.description ?? '—'}</td></tr>)}
      </tbody></table></div>
      <nav className="pagination" aria-label="İşlem geçmişi sayfaları"><button className="button button-secondary button-small" onClick={() => setPage((p) => p - 1)} disabled={page <= 1 || query.isFetching}><ChevronLeft size={16} />Önceki</button><span>{page} / {pageCount}</span><button className="button button-secondary button-small" onClick={() => setPage((p) => p + 1)} disabled={page >= pageCount || query.isFetching}>Sonraki<ChevronRight size={16} /></button></nav>
    </>}
  </>
}
