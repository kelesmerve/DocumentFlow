import { useState, type FormEvent } from 'react'
import axios from 'axios'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ArrowDownToLine, ArrowLeft, Check, Copy, FileText, LoaderCircle, Plus, Send, Upload } from 'lucide-react'
import { Link, useParams } from 'react-router-dom'
import { apiErrorMessage } from '../api/client'
import { documentsApi } from '../api/documents'
import { approvalsApi } from '../api/approvals'
import { useAuth } from '../auth/useAuth'
import { DocumentStatusBadge } from '../components/DocumentStatusBadge'
import { FilePicker } from '../components/FilePicker'
import { SubmitApprovalDialog } from '../components/ApprovalDialogs'
import { WorkflowTimeline } from '../components/WorkflowTimeline'
import { EmptyState, ErrorNotice, LoadingState } from '../components/Ui'
import { formatDate, formatFileSize } from '../utils/format'

type DetailTab = 'overview' | 'versions' | 'process'

export function DocumentDetailsPage() {
  const { id = '' } = useParams()
  const { user } = useAuth()
  const queryClient = useQueryClient()
  const [tab, setTab] = useState<DetailTab>('overview')
  const [submitOpen, setSubmitOpen] = useState(false)
  const [uploadOpen, setUploadOpen] = useState(false)
  const [file, setFile] = useState<File | null>(null)
  const [fileError, setFileError] = useState('')
  const [uploadProgress, setUploadProgress] = useState<number | null>(null)
  const [downloadError, setDownloadError] = useState('')
  const [copiedVersion, setCopiedVersion] = useState<number | null>(null)
  const detailKey = ['document', user?.id, id]
  const documentQuery = useQuery({ queryKey: detailKey, queryFn: () => documentsApi.get(id), enabled: Boolean(id) })
  const details = documentQuery.data
  const document = details?.document
  const latest = details?.versions.at(-1)
  const isOwner = Boolean(user && document && user.id.toLowerCase() === document.ownerId.toLowerCase())

  const addVersion = useMutation({
    mutationFn: () => {
      if (!file) throw new Error('Yüklemek için bir dosya seçin.')
      return documentsApi.addVersion(id, file, setUploadProgress)
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: detailKey })
      await queryClient.invalidateQueries({ queryKey: ['documents'] })
      setFile(null)
      setFileError('')
      setUploadOpen(false)
      setUploadProgress(null)
    },
  })
  const submitApproval = useMutation({
    mutationFn: ({ managerId, comment }: { managerId: string; comment: string }) => approvalsApi.submit(id, managerId, comment),
    onSuccess: async () => {
      setSubmitOpen(false)
      await Promise.all([queryClient.invalidateQueries({ queryKey: detailKey }), queryClient.invalidateQueries({ queryKey: ['documents'] }), queryClient.invalidateQueries({ queryKey: ['workflow', id] })])
    },
  })

  async function download(versionNumber: number, fallbackName: string) {
    setDownloadError('')
    try {
      const fileResult = await documentsApi.download(id, versionNumber)
      const url = URL.createObjectURL(fileResult.blob)
      const anchor = window.document.createElement('a')
      anchor.href = url
      anchor.download = fileResult.fileName || fallbackName
      anchor.click()
      window.setTimeout(() => URL.revokeObjectURL(url), 1000)
    } catch (error) {
      setDownloadError(apiErrorMessage(error, 'Dosya indirilemedi. Yeniden deneyin.'))
    }
  }

  async function copyHash(hash: string, versionNumber: number) {
    try {
      await navigator.clipboard.writeText(hash)
      setCopiedVersion(versionNumber)
      window.setTimeout(() => setCopiedVersion(null), 1800)
    } catch {
      setDownloadError('Hash panoya kopyalanamadı. Metni seçerek kopyalayabilirsiniz.')
    }
  }

  function submitVersion(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setFileError(file ? '' : 'Bir dosya seçmeniz gerekiyor.')
    if (file) addVersion.mutate()
  }

  if (documentQuery.isPending) return <><Link className="back-link" to="/documents"><ArrowLeft size={16} />Belgelerim</Link><LoadingState label="Belge yükleniyor…" /></>
  if (documentQuery.isError) {
    const notFound = axios.isAxiosError(documentQuery.error) && documentQuery.error.response?.status === 404
    return <><Link className="back-link" to="/documents"><ArrowLeft size={16} />Belgelerim</Link>{notFound ? <EmptyState title="Belge bulunamadı" description="Belge kaldırılmış veya erişim izniniz olmayabilir." /> : <ErrorNotice onRetry={() => void documentQuery.refetch()}>Belge bilgileri alınamadı.</ErrorNotice>}</>
  }
  if (!details || !document) return <EmptyState title="Belge bulunamadı" />

  return <>
    <Link className="back-link" to="/documents"><ArrowLeft size={16} />Belgelerim</Link>
    <div className="detail-header">
      <div className="detail-identification"><span className="document-number-large">{document.documentNumber}</span><DocumentStatusBadge status={document.status} />{isOwner && document.status === 'Draft' && <button className="button button-primary button-small" onClick={() => setSubmitOpen(true)}><Send size={14} />Onaya gönder</button>}</div>
      <h1>{document.title}</h1>
      <div className="detail-meta"><span>{document.category}</span><span className="meta-dot" /><span>Oluşturulma {formatDate(document.createdAtUtc)}</span></div>
    </div>
    <div className="detail-tabs" role="tablist" aria-label="Belge bölümleri">
      <button role="tab" aria-selected={tab === 'overview'} className={tab === 'overview' ? 'detail-tab active' : 'detail-tab'} onClick={() => setTab('overview')}>Genel</button>
      <button role="tab" aria-selected={tab === 'versions'} className={tab === 'versions' ? 'detail-tab active' : 'detail-tab'} onClick={() => setTab('versions')}>Versiyonlar <span className="tab-count">{details.versions.length}</span></button>
      <button role="tab" aria-selected={tab === 'process'} className={tab === 'process' ? 'detail-tab active' : 'detail-tab'} onClick={() => setTab('process')}>Süreç</button>
    </div>
    {downloadError && <div className="inline-error" role="alert">{downloadError}</div>}

    {tab === 'overview' && <section className="detail-overview" role="tabpanel">
      <div className="overview-main"><p className="section-kicker">BELGE BİLGİSİ</p><h2>Açıklama</h2><p className={`description-copy${document.description ? '' : ' description-empty'}`}>{document.description || 'Bu belge için açıklama eklenmemiş.'}</p></div>
      <div className="current-file-block"><div className="section-heading compact-heading"><div><p className="section-kicker">GÜNCEL DOSYA</p><h2>v{latest?.versionNumber ?? '—'}</h2></div>{latest && <button className="button button-secondary button-small" onClick={() => void download(latest.versionNumber, latest.originalFileName)}><ArrowDownToLine size={15} />İndir</button>}</div>
        {latest ? <div className="current-file-row"><span className="file-icon"><FileText size={19} /></span><div className="file-row-copy"><strong>{latest.originalFileName}</strong><span>{formatFileSize(latest.fileSize)} · Yüklenme {formatDate(latest.createdAtUtc)}</span></div></div> : <p className="muted-cell">Dosya sürümü bulunamadı.</p>}
        {latest && <HashLine hash={latest.fileHash} version={latest.versionNumber} copied={copiedVersion === latest.versionNumber} onCopy={() => void copyHash(latest.fileHash, latest.versionNumber)} />}
      </div>
      <div className="overview-facts"><div><span>Kategori</span><strong>{document.category}</strong></div><div><span>Durum</span><DocumentStatusBadge status={document.status} /></div><div><span>Belge sahibi</span><strong>{isOwner ? 'Siz' : 'Kurum kullanıcısı'}</strong></div></div>
    </section>}

    {tab === 'versions' && <section className="versions-section" role="tabpanel">
      <div className="section-heading versions-heading"><div><h2>Dosya versiyonları</h2><p>Önceki dosyalar korunur ve indirilebilir.</p></div>{isOwner && (document.status === 'Draft' || document.status === 'RevisionRequested') && <button className="button button-secondary" onClick={() => setUploadOpen((open) => !open)}><Plus size={16} />Yeni versiyon yükle</button>}</div>
      {uploadOpen && <form className="version-upload-form" onSubmit={submitVersion}>
        <div><h3>Yeni dosya sürümü</h3><p>Yeni yükleme ayrı bir versiyon olarak saklanır.</p></div>
        <FilePicker id="new-version-file" file={file} onChange={(next) => { setFile(next); setFileError('') }} error={fileError} disabled={addVersion.isPending} />
        {addVersion.isError && <div className="form-error" role="alert">{apiErrorMessage(addVersion.error, 'Yeni versiyon yüklenemedi.')}</div>}
        {addVersion.isPending && <div className="upload-progress" role="status"><div className="progress-copy"><span>Dosya yükleniyor</span>{uploadProgress !== null && <span>%{uploadProgress}</span>}</div><div className="progress-track"><span style={{ width: `${uploadProgress ?? 8}%` }} /></div></div>}
        <div className="form-actions compact-actions"><button className="button button-quiet" type="button" onClick={() => setUploadOpen(false)} disabled={addVersion.isPending}>Vazgeç</button><button className="button button-primary" type="submit" disabled={addVersion.isPending}>{addVersion.isPending ? <LoaderCircle size={16} className="spin" /> : <Upload size={16} />}{addVersion.isPending ? 'Yükleniyor…' : 'Yükle'}</button></div>
      </form>}
      {details.versions.length ? <div className="version-list">{[...details.versions].sort((a, b) => b.versionNumber - a.versionNumber).map((version) => <article className="version-row" key={version.versionNumber}>
        <div className="version-number">v{version.versionNumber}</div><span className="file-icon"><FileText size={18} /></span><div className="version-file-copy"><strong>{version.originalFileName}</strong><span>{formatFileSize(version.fileSize)} <span className="meta-dot" /> {formatDate(version.createdAtUtc)}</span><HashLine hash={version.fileHash} version={version.versionNumber} copied={copiedVersion === version.versionNumber} onCopy={() => void copyHash(version.fileHash, version.versionNumber)} /></div><button className="button button-secondary button-small version-download" onClick={() => void download(version.versionNumber, version.originalFileName)}><ArrowDownToLine size={15} />İndir</button>
      </article>)}</div> : <EmptyState title="Henüz dosya sürümü yok." />}
    </section>}
    {tab === 'process' && <section className="process-section" role="tabpanel"><div className="section-heading"><div><h2>Belge süreci</h2><p>Belge sürümleri ve onay kararlarından oluşan geçmiş.</p></div></div><WorkflowTimeline documentId={id} /></section>}
    {submitOpen && <SubmitApprovalDialog onClose={() => setSubmitOpen(false)} onSubmit={async (managerId, comment) => { await submitApproval.mutateAsync({ managerId, comment }) }} />}
  </>
}

function HashLine({ hash, version, copied, onCopy }: { hash: string; version: number; copied: boolean; onCopy: () => void }) {
  return <div className="hash-line"><span className="hash-label">SHA-256</span><code title={hash}>{hash}</code><button className="hash-copy" onClick={onCopy} aria-label={`v${version} SHA-256 değerini kopyala`} title={copied ? 'Kopyalandı' : 'Kopyala'}>{copied ? <Check size={14} /> : <Copy size={14} />}</button></div>
}
