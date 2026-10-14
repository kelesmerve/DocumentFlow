import { api } from './client'
import type { IntegrationEventType, WebhookDelivery, WebhookEndpoint } from '../types/api'

const root = '/api/integrations/webhooks'
export const integrationsApi = {
  async list(): Promise<WebhookEndpoint[]> { return (await api.get<WebhookEndpoint[]>(root)).data },
  async create(input: { url: string; eventTypes: IntegrationEventType[]; secret: string }): Promise<WebhookEndpoint> {
    return (await api.post<WebhookEndpoint>(root, input)).data
  },
  async setActive(id: string, isActive: boolean): Promise<WebhookEndpoint> {
    return (await api.patch<WebhookEndpoint>(`${root}/${id}/active`, { isActive })).data
  },
  async remove(id: string): Promise<void> { await api.delete(`${root}/${id}`) },
  async deliveries(): Promise<WebhookDelivery[]> { return (await api.get<WebhookDelivery[]>(`${root}/deliveries?page=1&pageSize=30`)).data },
}
