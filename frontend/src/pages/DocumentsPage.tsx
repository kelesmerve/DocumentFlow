import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { ChevronLeft, ChevronRight, Plus } from 'lucide-react'
import { Link } from 'react-router-dom'
import { documentsApi } from '../api/documents'
import { useAuth } from '../auth/useAuth'
import { DocumentTable } from '../components/DocumentTable'
import { EmptyState, ErrorNotice, LoadingState, PageHeading } from '../components/Ui'

const pageSize = 20

export function DocumentsPage() {
  const { user } = useAuth()
  const [page, setPage] = useState(1)
  const documents = useQuery({
    queryKey: ['documents', page, pageSize, user?.id],
    queryFn: () => documentsApi.list({ page, pageSize }),
    placeholderData: (previous) => previous,
  })
  const pageCount = Math.max(1, Math.ceil((documents.data?.totalCount ?? 0) / pageSize))

  return <>
    <PageHeading title="Belgelerim" description="Oluşturduğunuz ve erişebildiğiniz belgeler."
      action={<Link className="button button-primary" to="/documents/new"><Plus size={17} />Yeni belge</Link>} />
    {documents.isPending && <LoadingState label="Belgeler getiriliyor…" />}
    {documents.isError && <ErrorNotice onRetry={() => void documents.refetch()}>Belgeler yüklenemedi. Bağlantınızı kontrol edip yeniden deneyin.</ErrorNotice>}
    {documents.data && documents.data.totalCount === 0 && <EmptyState title="Henüz belge oluşturmadınız." description="İlk belgeniz çalışma alanınızda burada görünecek." action={<Link className="button button-primary" to="/documents/new">İlk belgenizi oluşturun</Link>} />}
    {documents.data && documents.data.totalCount > 0 && <>
      <div className="list-toolbar"><span>{documents.data.totalCount} belge</span><span>Sayfa {documents.data.page} / {pageCount}</span></div>
      <DocumentTable documents={documents.data.items} showAction />
      <nav className="pagination" aria-label="Belge sayfaları">
        <button className="button button-secondary button-small" onClick={() => setPage((current) => Math.max(1, current - 1))} disabled={page <= 1 || documents.isFetching}><ChevronLeft size={16} />Önceki</button>
        <span>{page} / {pageCount}</span>
        <button className="button button-secondary button-small" onClick={() => setPage((current) => Math.min(pageCount, current + 1))} disabled={page >= pageCount || documents.isFetching}>Sonraki<ChevronRight size={16} /></button>
      </nav>
    </>}
  </>
}
