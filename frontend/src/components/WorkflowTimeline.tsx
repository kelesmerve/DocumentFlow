import { useQuery } from '@tanstack/react-query'
import { approvalsApi } from '../api/approvals'
import { ErrorNotice, LoadingState } from './Ui'
import { formatDate } from '../utils/format'

export function WorkflowTimeline({ documentId }: { documentId: string }) {
  const query = useQuery({ queryKey: ['workflow', documentId], queryFn: () => approvalsApi.timeline(documentId) })
  if (query.isPending) return <LoadingState label="Süreç geçmişi yükleniyor…" />
  if (query.isError) return <ErrorNotice onRetry={() => void query.refetch()}>Süreç geçmişi alınamadı.</ErrorNotice>
  if (!query.data?.length) return <div className="empty-state compact-empty"><h2>Süreç kaydı yok</h2><p>Bu belge için henüz onay işlemi yapılmadı.</p></div>
  return <ol className="workflow-timeline">{query.data.map((event, index) => <li className="timeline-event" key={`${event.kind}-${event.atUtc}-${index}`}><span className={`timeline-marker timeline-${event.kind}`} aria-hidden="true" /><div className="timeline-copy"><strong>{event.title}</strong><span>{event.actor}</span>{event.detail && <p>{event.detail}</p>}<time dateTime={event.atUtc}>{formatDate(event.atUtc)}</time></div></li>)}</ol>
}
