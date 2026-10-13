import { useState } from 'react'
import { BadgeCheck, LoaderCircle, ShieldCheck, ShieldX, ShieldQuestion, PenLine } from 'lucide-react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { signaturesApi } from '../api/signatures'
import { apiErrorMessage } from '../api/client'
import { useAuth } from '../auth/useAuth'
import { formatDate } from '../utils/format'
import type { DocumentVersion, SignatureVerification } from '../types/api'

export function VersionSignatures({ documentId, version }: { documentId: string; version: DocumentVersion }) {
  const { user } = useAuth()
  const queryClient = useQueryClient()
  const [confirmOpen, setConfirmOpen] = useState(false)
  const signatureKey = ['signatures', documentId, version.id]
  const verifyKey = ['signature-verification', documentId, version.id]
  const signatures = useQuery({ queryKey: signatureKey, queryFn: () => signaturesApi.list(documentId, version.id) })
  const verification = useQuery({ queryKey: verifyKey, queryFn: () => signaturesApi.verify(documentId, version.id), enabled: Boolean(signatures.data?.items.length) })
  const sign = useMutation({
    mutationFn: () => signaturesApi.sign(documentId, version.id),
    onSuccess: async () => {
      setConfirmOpen(false)
      await Promise.all([queryClient.invalidateQueries({ queryKey: signatureKey }), queryClient.invalidateQueries({ queryKey: verifyKey })])
    },
  })
  const alreadySigned = Boolean(signatures.data?.items.some((item) => item.signedByUserId.toLowerCase() === user?.id.toLowerCase()))

  return <div className="signature-panel">
    <div className="signature-panel-heading"><strong><BadgeCheck size={15} />Dijital imza</strong>
      {signatures.data?.canSign && !alreadySigned && <button type="button" className="button button-secondary button-small" onClick={() => setConfirmOpen(true)} disabled={sign.isPending}><PenLine size={14} />Dijital Olarak İmzala</button>}
    </div>
    {signatures.isPending && <span className="signature-muted">İmza durumu yükleniyor…</span>}
    {signatures.isError && <span className="signature-error">İmza durumu alınamadı.</span>}
    {signatures.data && signatures.data.items.length === 0 && <span className="signature-muted">Bu versiyonda dijital imza yok.</span>}
    {signatures.data?.items.map((item) => {
      const result = verification.data?.find((entry) => entry.signatureId === item.id)
      return <div className="signature-entry" key={item.id}>
        <div className="signature-person"><strong>{item.signedByName}</strong><span>{formatDate(item.signedAtUtc)} · {item.signatureAlgorithm} · {item.hashAlgorithm}</span></div>
        <div className="signature-entry-actions"><VerificationResult result={result} loading={verification.isFetching} /><button type="button" className="text-button" onClick={() => void verification.refetch()} disabled={verification.isFetching}>İmzayı doğrula</button></div>
      </div>
    })}
    {sign.isError && <div className="signature-error" role="alert">{apiErrorMessage(sign.error, 'Belge imzalanamadı.')}</div>}
    {sign.isSuccess && <div className="signature-success" role="status">Belge versiyonu imzalandı.</div>}
    {verification.isError && signatures.data?.items.length ? <div className="signature-error" role="alert">İmza doğrulaması alınamadı.</div> : null}
    {signatures.data?.items.length ? <p className="signature-disclaimer">Doğrulama dosya bütünlüğü ve kayıtlı anahtarla ilişkiyi kontrol eder; gerçek kişi kimliği veya hukuki geçerlilik garantisi vermez.</p> : null}
    {confirmOpen && <div className="dialog-backdrop" role="presentation"><section className="workflow-dialog signature-dialog" role="dialog" aria-modal="true" aria-labelledby={`sign-title-${version.id}`}>
      <header><h2 id={`sign-title-${version.id}`}>Versiyonu dijital olarak imzala</h2></header>
      <div className="workflow-form"><p className="dialog-intro">v{version.versionNumber} versiyonu RSA-PSS ve SHA-256 kullanılarak imzalanacak. Bu işlem mevcut dosya içeriğini değiştirmez.</p>
        {sign.isError && <div className="form-error" role="alert">{apiErrorMessage(sign.error, 'Belge imzalanamadı.')}</div>}
        <footer><button className="button button-quiet" onClick={() => setConfirmOpen(false)} disabled={sign.isPending}>Vazgeç</button><button className="button button-primary" onClick={() => sign.mutate()} disabled={sign.isPending}>{sign.isPending ? <LoaderCircle size={15} className="spin" /> : <PenLine size={15} />}İmzala</button></footer>
      </div>
    </section></div>}
  </div>
}

function VerificationResult({ result, loading }: { result?: SignatureVerification; loading: boolean }) {
  if (loading) return <span className="signature-status muted"><LoaderCircle size={14} className="spin" />Doğrulanıyor</span>
  if (!result) return <span className="signature-status muted"><ShieldQuestion size={14} />Doğrulanmadı</span>
  if (result.result === 'Valid') return <span className="signature-status valid" title={result.message}><ShieldCheck size={14} />Geçerli</span>
  if (result.result === 'Invalid') return <span className="signature-status invalid" title={result.message}><ShieldX size={14} />Geçersiz</span>
  return <span className="signature-status muted" title={result.message}><ShieldQuestion size={14} />Doğrulanamıyor</span>
}
