import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ArrowDownToLine, ArrowLeft, FileText } from 'lucide-react'
import { Link, useParams } from 'react-router-dom'
import { approvalsApi } from '../api/approvals'
import { apiErrorMessage } from '../api/client'
import { useAuth } from '../auth/useAuth'
import { DecisionDialog } from '../components/ApprovalDialogs'
import { ErrorNotice, LoadingState } from '../components/Ui'
import { formatDate, formatFileSize } from '../utils/format'
import type { ApprovalStatus } from '../types/api'

type Decision = 'approve' | 'reject' | 'request-revision'
const statusLabels: Record<ApprovalStatus, string> = { Pending: 'Bekliyor', Approved: 'Onaylandı', Rejected: 'Reddedildi', RevisionRequested: 'Revizyon istendi' }
export function ApprovalDetailsPage() {
  const { id = '' } = useParams()
  const { user } = useAuth()
  const cache = useQueryClient()
  const [decision, setDecision] = useState<Decision | null>(null)
  const [downloadError, setDownloadError] = useState('')
  const query = useQuery({ queryKey: ['approval', id], queryFn: () => approvalsApi.get(id), enabled: Boolean(id) })
  const mutation = useMutation({ mutationFn: ({ decision: action, comment }: { decision: Decision; comment: string }) => approvalsApi.decide(id, action, { comment }), onSuccess: async () => { setDecision(null); await Promise.all([cache.invalidateQueries({ queryKey: ['approval', id] }), cache.invalidateQueries({ queryKey: ['approvals'] }), cache.invalidateQueries({ queryKey: ['documents'] }), cache.invalidateQueries({ queryKey: ['document'] }), cache.invalidateQueries({ queryKey: ['workflow'] })]) } })
  if (query.isPending) return <LoadingState />
  if (query.isError || !query.data) return <ErrorNotice onRetry={() => void query.refetch()}>{apiErrorMessage(query.error, 'Onay talebi bulunamadı veya erişim izniniz yok.')}</ErrorNotice>
  const { approval, description } = query.data
  const mayDecide = user?.role === 'Manager' && approval.status === 'Pending'
  async function downloadVersion() {
    setDownloadError('')
    try { await approvalsApi.download(approval.documentId, approval.versionNumber) }
    catch (error) { setDownloadError(apiErrorMessage(error, 'Dosya indirilemedi. Yeniden deneyin.')) }
  }
  return <>
    <Link className="back-link" to="/approvals"><ArrowLeft size={16} />Onaylarım</Link>
    <div className="detail-header"><div className="detail-identification"><span className="document-number-large">{approval.documentNumber}</span><span className={`status-badge approval-${approval.status.toLowerCase()}`}>{statusLabels[approval.status]}</span></div><h1>{approval.documentTitle}</h1><div className="detail-meta"><span>{approval.category}</span><span className="meta-dot" /><span>Talep {formatDate(approval.createdAtUtc)}</span></div></div>
    <section className="approval-review-card"><div><p className="section-kicker">İNCELEME TALEBİ</p><h2>v{approval.versionNumber} · {query.data.originalFileName}</h2><p>{query.data.fileSize ? formatFileSize(query.data.fileSize) : ''} · SHA-256 {query.data.fileHash}</p></div><button className="button button-secondary" onClick={() => void downloadVersion()}><ArrowDownToLine size={16} />Dosyayı indir</button></section>
    {downloadError && <div className="inline-error" role="alert">{downloadError}</div>}
    <section className="approval-request-card"><div className="section-heading"><div><h2>Talep bilgileri</h2><p>{approval.requestedBy} · {approval.requestedByEmail}</p></div><Link className="quiet-link" to={`/documents/${approval.documentId}`}>Belge detayına git <ArrowDownToLine size={14} /></Link></div>{approval.requestComment && <blockquote>{approval.requestComment}</blockquote>}<p className="review-description"><FileText size={16} />{description || 'Belge açıklaması eklenmemiş.'}</p></section>
    {query.data.decisionComment && <section className="approval-request-card"><h2>Karar notu</h2><p>{query.data.decisionComment}</p><small>{query.data.decidedAtUtc && formatDate(query.data.decidedAtUtc)}</small></section>}
    {mutation.isError && <div className="form-error" role="alert">{apiErrorMessage(mutation.error, 'Karar kaydedilemedi.')}</div>}
    {mayDecide && <div className="decision-actions"><button className="button button-primary" disabled={mutation.isPending} onClick={() => setDecision('approve')}>Onayla</button><button className="button button-secondary" disabled={mutation.isPending} onClick={() => setDecision('request-revision')}>Revizyon iste</button><button className="button button-danger" disabled={mutation.isPending} onClick={() => setDecision('reject')}>Reddet</button></div>}
    {decision && <DecisionDialog decision={decision} onClose={() => setDecision(null)} onSubmit={async (comment) => { await mutation.mutateAsync({ decision, comment }) }} />}
  </>
}
