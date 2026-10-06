import { ArrowUpRight } from 'lucide-react'
import { Link } from 'react-router-dom'
import { DocumentStatusBadge } from './DocumentStatusBadge'
import { formatDate } from '../utils/format'
import type { DocumentSummary } from '../types/api'

export function DocumentTable({ documents, showAction = false }: { documents: DocumentSummary[]; showAction?: boolean }) {
  return <div className="table-wrap"><table className="data-table">
    <thead><tr><th>Belge No</th><th>Başlık</th><th>Kategori</th><th>Durum</th><th>Oluşturulma</th>{showAction && <th><span className="sr-only">İşlem</span></th>}</tr></thead>
    <tbody>{documents.map((document) => <tr key={document.id}>
      <td><Link className="table-link doc-number" to={`/documents/${document.id}`}>{document.documentNumber}</Link></td>
      <td><Link className="table-link doc-title" to={`/documents/${document.id}`}>{document.title}</Link></td>
      <td className="muted-cell">{document.category}</td>
      <td><DocumentStatusBadge status={document.status} /></td>
      <td className="muted-cell">{formatDate(document.createdAtUtc)}</td>
      {showAction && <td><Link className="icon-link" aria-label={`${document.title} detaylarını aç`} to={`/documents/${document.id}`}><ArrowUpRight size={16} /></Link></td>}
    </tr>)}</tbody>
  </table></div>
}
