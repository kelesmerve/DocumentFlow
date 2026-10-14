import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { integrationsApi } from '../api/integrations'
import { apiErrorMessage } from '../api/client'
import { ErrorNotice, LoadingState } from '../components/Ui'
import { formatDate } from '../utils/format'
import type { IntegrationEventType, WebhookEndpoint } from '../types/api'

const eventLabels: Record<IntegrationEventType, string> = {
  DocumentApproved: 'Belge onaylandı', DocumentSigned: 'Belge imzalandı',
  MobileSignatureCompleted: 'Mobil imza tamamlandı',
}
const eventTypes = Object.keys(eventLabels) as IntegrationEventType[]
const statusLabels = { Pending: 'Bekliyor', Delivered: 'Başarılı', DeadLetterPending: 'DLQ bekliyor', DeadLetter: 'Başarısız (DLQ)' } as const

export function IntegrationsPage() {
  const queryClient = useQueryClient()
  const [open, setOpen] = useState(false)
  const [url, setUrl] = useState('')
  const [secret, setSecret] = useState('')
  const [selected, setSelected] = useState<IntegrationEventType[]>([...eventTypes])
  const [deleteTarget, setDeleteTarget] = useState<WebhookEndpoint | null>(null)
  const hooks = useQuery({ queryKey: ['webhooks'], queryFn: integrationsApi.list })
  const deliveries = useQuery({ queryKey: ['webhook-deliveries'], queryFn: integrationsApi.deliveries, refetchInterval: 15000 })
  const create = useMutation({ mutationFn: integrationsApi.create, onSuccess: async () => { setOpen(false); setUrl(''); setSecret(''); await queryClient.invalidateQueries({ queryKey: ['webhooks'] }) } })
  const active = useMutation({ mutationFn: ({ id, isActive }: { id: string; isActive: boolean }) => integrationsApi.setActive(id, isActive), onSuccess: async () => { await queryClient.invalidateQueries({ queryKey: ['webhooks'] }) } })
  const remove = useMutation({ mutationFn: integrationsApi.remove, onSuccess: async () => { setDeleteTarget(null); await queryClient.invalidateQueries({ queryKey: ['webhooks'] }) } })

  return <>
    <div className="page-heading"><div><h1>Entegrasyonlar</h1><p className="page-description">Belge olaylarını güvenli webhook endpoint’lerine iletin.</p></div><button type="button" className="button button-primary" onClick={() => setOpen(true)}>Webhook Ekle</button></div>
    {hooks.isPending && <LoadingState label="Webhook kayıtları yükleniyor…" />}
    {hooks.isError && <ErrorNotice onRetry={() => void hooks.refetch()}>Webhook kayıtları alınamadı.</ErrorNotice>}
    {hooks.data?.length === 0 && <div className="empty-state"><h2>Webhook tanımlanmadı</h2><p>Olay abonelikleri burada görünecek.</p></div>}
    {hooks.data && hooks.data.length > 0 && <div className="table-wrap"><table className="data-table integration-table"><thead><tr><th>Endpoint</th><th>Abonelikler</th><th>Durum</th><th>Oluşturulma</th><th>İşlem</th></tr></thead><tbody>{hooks.data.map((hook) => <tr key={hook.id}><td className="integration-url">{hook.url}</td><td>{hook.eventTypes.map((type) => eventLabels[type]).join(', ')}</td><td><span className={`status-badge ${hook.isActive ? 'approval-approved' : 'approval-rejected'}`}>{hook.isActive ? 'Aktif' : 'Pasif'}</span></td><td>{formatDate(hook.createdAtUtc)}</td><td><div className="integration-actions"><button type="button" className="text-button" disabled={active.isPending} onClick={() => active.mutate({ id: hook.id, isActive: !hook.isActive })}>{hook.isActive ? 'Pasifleştir' : 'Etkinleştir'}</button><button type="button" className="text-button integration-delete" onClick={() => setDeleteTarget(hook)}>Sil</button></div></td></tr>)}</tbody></table></div>}
    {active.isError && <p className="signature-error" role="alert">{apiErrorMessage(active.error, 'Durum değiştirilemedi.')}</p>}
    <section className="integration-deliveries"><h2>Son teslimatlar</h2>{deliveries.isPending && <LoadingState label="Teslimatlar yükleniyor…" />}{deliveries.isError && <ErrorNotice onRetry={() => void deliveries.refetch()}>Teslimatlar alınamadı.</ErrorNotice>}{deliveries.data?.length === 0 && <p className="signature-muted">Henüz teslimat yok.</p>}{deliveries.data && deliveries.data.length > 0 && <div className="table-wrap"><table className="data-table integration-table"><thead><tr><th>Tarih</th><th>Olay</th><th>Durum</th><th>Deneme</th><th>Hata</th></tr></thead><tbody>{deliveries.data.map((item) => <tr key={item.id}><td>{formatDate(item.createdAtUtc)}</td><td>{eventLabels[item.eventType]}</td><td>{statusLabels[item.status]}</td><td>{item.attemptCount}</td><td>{item.lastError ?? '—'}</td></tr>)}</tbody></table></div>}</section>
    {open && <div className="dialog-backdrop" role="presentation"><section className="workflow-dialog" role="dialog" aria-modal="true" aria-labelledby="webhook-add-title"><header><h2 id="webhook-add-title">Webhook ekle</h2></header><form className="workflow-form" onSubmit={(event) => { event.preventDefault(); create.mutate({ url: url.trim(), eventTypes: selected, secret }) }}><label htmlFor="webhook-url">HTTPS URL</label><input id="webhook-url" type="url" required value={url} onChange={(event) => setUrl(event.target.value)} placeholder="https://example.org/webhook" /><label htmlFor="webhook-secret">HMAC secret (en az 32 karakter)</label><input id="webhook-secret" type="password" required minLength={32} autoComplete="new-password" value={secret} onChange={(event) => setSecret(event.target.value)} /><span className="field-hint">Secret yalnızca bu formda girilir; listede tekrar gösterilmez.</span><fieldset className="integration-events"><legend>Olay abonelikleri</legend>{eventTypes.map((type) => <label key={type}><input type="checkbox" checked={selected.includes(type)} onChange={() => setSelected((current) => current.includes(type) ? current.filter((item) => item !== type) : [...current, type])} />{eventLabels[type]}</label>)}</fieldset>{create.isError && <div className="form-error" role="alert">{apiErrorMessage(create.error, 'Webhook eklenemedi.')}</div>}<footer><button type="button" className="button button-quiet" onClick={() => { setOpen(false); setSecret('') }}>Vazgeç</button><button type="submit" className="button button-primary" disabled={create.isPending || !selected.length || secret.length < 32}>Kaydet</button></footer></form></section></div>}
    {deleteTarget && <div className="dialog-backdrop" role="presentation"><section className="workflow-dialog" role="dialog" aria-modal="true" aria-labelledby="webhook-delete-title"><header><h2 id="webhook-delete-title">Webhook sil</h2></header><div className="workflow-form"><p className="dialog-intro">{deleteTarget.url} kaydı pasifleştirilecek ve listeden kaldırılacak. Teslimat geçmişi korunacak.</p>{remove.isError && <div className="form-error" role="alert">{apiErrorMessage(remove.error, 'Webhook silinemedi.')}</div>}<footer><button type="button" className="button button-quiet" onClick={() => setDeleteTarget(null)}>Vazgeç</button><button type="button" className="button button-danger" disabled={remove.isPending} onClick={() => remove.mutate(deleteTarget.id)}>Sil</button></footer></div></section></div>}
  </>
}
