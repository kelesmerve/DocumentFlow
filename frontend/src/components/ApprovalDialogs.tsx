import { useEffect, useState, type FormEvent } from 'react'
import { LoaderCircle } from 'lucide-react'
import { apiErrorMessage } from '../api/client'
import { approvalsApi } from '../api/approvals'
import type { Manager } from '../types/api'
import { Dialog } from './Dialog'

export function SubmitApprovalDialog({ onClose, onSubmit }: { onClose: () => void; onSubmit: (managerId: string, comment: string) => Promise<void> }) {
  const [managers, setManagers] = useState<Manager[]>([])
  const [managerId, setManagerId] = useState('')
  const [comment, setComment] = useState('')
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)
  const [managersLoading, setManagersLoading] = useState(true)
  useEffect(() => {
    let mounted = true
    void approvalsApi.managers().then((items) => { if (mounted) setManagers(items) })
      .catch((e: unknown) => { if (mounted) setError(apiErrorMessage(e, 'Yönetici listesi alınamadı.')) })
      .finally(() => { if (mounted) setManagersLoading(false) })
    return () => { mounted = false }
  }, [])
  async function submit(event: FormEvent) {
    event.preventDefault(); setError(''); setLoading(true)
    try { await onSubmit(managerId, comment) }
    catch (e) { setError(apiErrorMessage(e, 'Onaya gönderilemedi.')) }
    finally { setLoading(false) }
  }
  return <Dialog title="Onaya gönder" onClose={onClose} busy={loading}><form className="workflow-form" onSubmit={(event) => void submit(event)}>
    <p className="dialog-intro">Belgenin güncel versiyonu seçtiğiniz yöneticiye iletilecek.</p>
    <label htmlFor="manager-select">Onaylayacak yönetici <span aria-hidden="true">*</span></label>
    <select id="manager-select" required value={managerId} onChange={(e) => setManagerId(e.target.value)} disabled={loading || managersLoading || managers.length === 0}><option value="">Yönetici seçin</option>{managers.map((manager) => <option value={manager.id} key={manager.id}>{manager.firstName} {manager.lastName} — {manager.email}</option>)}</select>
    {managersLoading && <p className="field-hint" role="status">Yöneticiler yükleniyor…</p>}
    {!managersLoading && managers.length === 0 && !error && <p className="field-hint">Aktif yönetici bulunamadı.</p>}
    <label htmlFor="request-comment">Not <span className="optional-label">İsteğe bağlı</span></label><textarea id="request-comment" value={comment} maxLength={2000} rows={3} onChange={(e) => setComment(e.target.value)} />
    {error && <div className="form-error" role="alert">{error}</div>}
    <footer><button type="button" className="button button-quiet" onClick={onClose} disabled={loading}>Vazgeç</button><button className="button button-primary" disabled={loading || !managerId}>{loading && <LoaderCircle size={16} className="spin" />}Onaya gönder</button></footer>
  </form></Dialog>
}

export function DecisionDialog({ decision, onClose, onSubmit }: { decision: 'approve' | 'reject' | 'request-revision'; onClose: () => void; onSubmit: (comment: string) => Promise<void> }) {
  const [comment, setComment] = useState('')
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)
  const labels = { approve: 'Belgeyi onayla', reject: 'Belgeyi reddet', 'request-revision': 'Revizyon iste' }
  const needsComment = decision !== 'approve'
  async function submit(event: FormEvent) {
    event.preventDefault(); setError(''); setLoading(true)
    try { await onSubmit(comment) }
    catch (e) { setError(apiErrorMessage(e, 'Karar kaydedilemedi.')) }
    finally { setLoading(false) }
  }
  return <Dialog title={labels[decision]} onClose={onClose} busy={loading}><form className="workflow-form" onSubmit={(event) => void submit(event)}>
    <p className="dialog-intro">Karar, bu belge versiyonunun onay sürecine kaydedilecek.</p><label htmlFor="decision-comment">Karar notu {needsComment ? <span aria-hidden="true">*</span> : <span className="optional-label">İsteğe bağlı</span>}</label><textarea id="decision-comment" value={comment} maxLength={2000} rows={4} required={needsComment} onChange={(e) => setComment(e.target.value)} />
    {error && <div className="form-error" role="alert">{error}</div>}
    <footer><button type="button" className="button button-quiet" onClick={onClose} disabled={loading}>Vazgeç</button><button className={`button ${decision === 'reject' ? 'button-danger' : 'button-primary'}`} disabled={loading || (needsComment && !comment.trim())}>{loading && <LoaderCircle size={16} className="spin" />}{labels[decision]}</button></footer>
  </form></Dialog>
}
