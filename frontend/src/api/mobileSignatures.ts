import { api } from './client'
import type { MobileSignatureRequest } from '../types/api'

export const mobileSignaturesApi = {
  async create(documentId: string, versionId: string): Promise<MobileSignatureRequest> {
    return (await api.post<MobileSignatureRequest>(`/api/documents/${documentId}/versions/${versionId}/mobile-signature-requests`)).data
  },
  async list(): Promise<MobileSignatureRequest[]> {
    return (await api.get<MobileSignatureRequest[]>('/api/mobile-signature-requests')).data
  },
  async approve(id: string, password: string): Promise<MobileSignatureRequest> {
    return (await api.post<MobileSignatureRequest>(`/api/mobile-signature-requests/${id}/approve`, { password })).data
  },
  async reject(id: string): Promise<MobileSignatureRequest> {
    return (await api.post<MobileSignatureRequest>(`/api/mobile-signature-requests/${id}/reject`)).data
  },
}
