import { api } from './client'
import type { DeliveryPage, IntegrationEventType, WebhookEndpoint, WebhookPage } from '../types/api'

const root = '/api/integrations/webhooks'
export const integrationsApi = {
  async list(page = 1): Promise<WebhookPage> { return (await api.get<WebhookPage>(`${root}?page=${page}&pageSize=20`)).data },
  async create(input: { url: string; eventTypes: IntegrationEventType[]; secret: string }): Promise<WebhookEndpoint> {
    return (await api.post<WebhookEndpoint>(root, input)).data
  },
  async setActive(id: string, isActive: boolean): Promise<WebhookEndpoint> {
    return (await api.patch<WebhookEndpoint>(`${root}/${id}/active`, { isActive })).data
  },
  async remove(id: string): Promise<void> { await api.delete(`${root}/${id}`) },
  async deliveries(page = 1): Promise<DeliveryPage> { return (await api.get<DeliveryPage>(`${root}/deliveries?page=${page}&pageSize=20`)).data },
}
