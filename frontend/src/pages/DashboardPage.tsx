import { useQuery } from '@tanstack/react-query'
import { ArrowRight, Plus } from 'lucide-react'
import { Link } from 'react-router-dom'
import { listAllDocuments } from '../api/documents'
import { useAuth } from '../auth/useAuth'
import { DocumentTable } from '../components/DocumentTable'
import { EmptyState, ErrorNotice, LoadingState, PageHeading } from '../components/Ui'

export function DashboardPage() {
  const { user } = useAuth()
  const documents = useQuery({ queryKey: ['documents', 'all', user?.id], queryFn: listAllDocuments })
  const all = documents.data ?? []
  const recent = [...all].sort((a, b) => Date.parse(b.createdAtUtc) - Date.parse(a.createdAtUtc)).slice(0, 5)
  const drafts = all.filter((document) => document.status === 'Draft').length

  return <>
    <PageHeading title="Genel Bakış" description="Doküman çalışma alanınızın güncel durumu."
      action={<Link className="button button-primary" to="/documents/new"><Plus size={17} />Yeni belge</Link>} />
    {documents.isPending && <LoadingState label="Belgeler yükleniyor…" />}
    {documents.isError && <ErrorNotice onRetry={() => void documents.refetch()}>Özet bilgileri alınamadı.</ErrorNotice>}
    {documents.data && <>
      <section className="summary-strip" aria-label="Belge özeti">
        <div className="summary-item"><span className="summary-label">Toplam belge</span><strong>{documents.data.length}</strong><span className="summary-footnote">Erişiminiz olan kayıtlar</span></div>
        <div className="summary-divider" />
        <div className="summary-item"><span className="summary-label">Taslak</span><strong>{drafts}</strong><span className="summary-footnote">Düzenlemeye açık belgeler</span></div>
      </section>
      <section className="recent-section" aria-labelledby="recent-title">
        <div className="section-heading"><div><h2 id="recent-title">Son belgeler</h2><p>En son oluşturulan dokümanlar</p></div><Link to="/documents" className="quiet-link">Tüm belgeler <ArrowRight size={15} /></Link></div>
        {recent.length ? <DocumentTable documents={recent} /> : <EmptyState title="Henüz belge oluşturmadınız." description="İlk belgenizi yükleyerek çalışma alanınızı başlatın." action={<Link className="button button-secondary" to="/documents/new">İlk belgenizi oluşturun</Link>} />}
      </section>
    </>}
  </>
}
