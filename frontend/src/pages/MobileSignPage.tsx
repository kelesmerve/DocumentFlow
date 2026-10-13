import { useEffect, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { mobileSignaturesApi } from '../api/mobileSignatures'
import { apiErrorMessage } from '../api/client'
import { ErrorNotice, LoadingState, PageHeading } from '../components/Ui'
import { formatDate } from '../utils/format'
import type { MobileSignatureRequest, MobileSignatureStatus } from '../types/api'

const statusNames: Record<MobileSignatureStatus, string> = {
  Pending: 'Bekliyor', Approved: 'İmzalandı', Rejected: 'Reddedildi', Expired: 'Süresi doldu',
}

export function MobileSignPage() {
  const [now, setNow] = useState(Date.now())
  const [selected, setSelected] = useState<MobileSignatureRequest | null>(null)
  const [password, setPassword] = useState('')
  const [mode, setMode] = useState<'approve' | 'reject'>('approve')
  const queryClient = useQueryClient()
  useEffect(() => { const timer = window.setInterval(() => setNow(Date.now()), 1000); return () => window.clearInterval(timer) }, [])
  const requests = useQuery({ queryKey: ['mobile-signature-requests'], queryFn: mobileSignaturesApi.list, refetchInterval: 15000 })
  const approve = useMutation({ mutationFn: ({ id, password }: { id: string; password: string }) => mobileSignaturesApi.approve(id, password), onSuccess: refresh })
  const reject = useMutation({ mutationFn: (id: string) => mobileSignaturesApi.reject(id), onSuccess: refresh })

  async function refresh() {
    setPassword('')
    setSelected(null)
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: ['mobile-signature-requests'] }),
      queryClient.invalidateQueries({ queryKey: ['signatures'] }),
      queryClient.invalidateQueries({ queryKey: ['signature-verification'] }),
      queryClient.invalidateQueries({ queryKey: ['documents'] }),
    ])
  }

  function open(request: MobileSignatureRequest, action: 'approve' | 'reject') { setPassword(''); setMode(action); setSelected(request); approve.reset(); reject.reset() }
  function close() { setPassword(''); setSelected(null) }

  return <>
    <PageHeading title="Mobil İmza" description="Size ait belge imza taleplerini inceleyin ve karar verin." />
    <p className="mobile-simulation-note">Bu ekran geliştirme amaçlı simülasyondur; gerçek operatör tabanlı mobil imza veya SMS onayı içermez.</p>
    {requests.isPending && <LoadingState label="Mobil imza talepleri yükleniyor…" />}
    {requests.isError && <ErrorNotice onRetry={() => void requests.refetch()}>Talepler alınamadı.</ErrorNotice>}
    {requests.data?.length === 0 && <div className="empty-state"><h2>Henüz mobil imza talebi yok</h2><p>Onayladığınız bir belge versiyonundan talep oluşturabilirsiniz.</p></div>}
    {requests.data && requests.data.length > 0 && <div className="mobile-request-list">
      {requests.data.map((request) => {
        const remaining = Math.max(0, Math.ceil((new Date(request.expiresAtUtc).getTime() - now) / 1000))
        const canDecide = request.status === 'Pending' && remaining > 0
        return <article className="mobile-request-card" key={request.id}>
          <div className="mobile-request-head"><div><Link className="table-link doc-number" to={`/documents/${request.documentId}`}>{request.documentNumber}</Link><h2>{request.documentTitle}</h2></div><span className={`status-badge approval-${request.status.toLowerCase()}`}>{request.status === 'Pending' && remaining === 0 ? statusNames.Expired : statusNames[request.status]}</span></div>
          <div className="mobile-request-meta"><span>Versiyon v{request.versionNumber}</span><span>Talep: {formatDate(request.createdAtUtc)}</span><span>{canDecide ? `Kalan süre: ${Math.floor(remaining / 60)}:${String(remaining % 60).padStart(2, '0')}` : `Son geçerlilik: ${formatDate(request.expiresAtUtc)}`}</span></div>
          {canDecide && <div className="mobile-request-actions"><button type="button" className="button button-primary" onClick={() => open(request, 'approve')}>Onayla ve İmzala</button><button type="button" className="button button-secondary" onClick={() => open(request, 'reject')}>Reddet</button></div>}
        </article>
      })}
    </div>}
    {selected && <div className="dialog-backdrop" role="presentation"><section className="workflow-dialog" role="dialog" aria-modal="true" aria-labelledby="mobile-decision-title">
      <header><h2 id="mobile-decision-title">{mode === 'approve' ? 'Mobil imzayı onayla' : 'Talebi reddet'}</h2></header>
      <div className="workflow-form"><p className="dialog-intro">{selected.documentNumber} · v{selected.versionNumber}. {mode === 'approve' ? 'Kimliğinizi parolanızla yeniden doğrulayın. Ardından RSA-PSS imzası oluşturulacak.' : 'Bu talep reddedilecek ve tekrar kullanılamayacak.'}</p>
        {mode === 'approve' && <><label htmlFor="mobile-password">Parola</label><input id="mobile-password" type="password" autoComplete="current-password" value={password} onChange={(event) => setPassword(event.target.value)} /></>}
        {(approve.isError || reject.isError) && <div className="form-error" role="alert">{apiErrorMessage(approve.error ?? reject.error, 'İşlem tamamlanamadı.')}</div>}
        <footer><button type="button" className="button button-quiet" onClick={close} disabled={approve.isPending || reject.isPending}>Vazgeç</button><button type="button" className={mode === 'reject' ? 'button button-danger' : 'button button-primary'} disabled={approve.isPending || reject.isPending || (mode === 'approve' && !password)} onClick={() => { if (mode === 'approve') approve.mutate({ id: selected.id, password }); else reject.mutate(selected.id) }}>{mode === 'approve' ? 'Onayla ve İmzala' : 'Reddet'}</button></footer>
      </div>
    </section></div>}
  </>
}
