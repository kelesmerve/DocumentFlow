import { api } from './client'
import type { AuditAction, AuditPage } from '../types/api'

export const auditApi = {
  async list(filters: { action?: AuditAction; userId?: string; documentNumber?: string; page: number; pageSize: number }): Promise<AuditPage> {
    const params = Object.fromEntries(Object.entries(filters).filter(([, value]) => value !== undefined && value !== ''))
    return (await api.get<AuditPage>('/api/audit', { params })).data
  },
}
