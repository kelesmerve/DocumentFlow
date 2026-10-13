import { api } from './client'
import type { SignatureList, SignatureSummary, SignatureVerification, TimestampCreateResponse, TimestampSummary, TimestampVerification } from '../types/api'

const versionPath = (documentId: string, versionId: string) => `/api/documents/${documentId}/versions/${versionId}`

export const signaturesApi = {
  async list(documentId: string, versionId: string): Promise<SignatureList> {
    return (await api.get<SignatureList>(`${versionPath(documentId, versionId)}/signatures`)).data
  },
  async verify(documentId: string, versionId: string): Promise<SignatureVerification[]> {
    return (await api.get<SignatureVerification[]>(`${versionPath(documentId, versionId)}/verify`)).data
  },
  async sign(documentId: string, versionId: string): Promise<SignatureSummary> {
    return (await api.post<SignatureSummary>(`${versionPath(documentId, versionId)}/sign`)).data
  },
  async createTimestamp(signatureId: string): Promise<TimestampCreateResponse> {
    return (await api.post<TimestampCreateResponse>(`/api/signatures/${signatureId}/timestamp`)).data
  },
  async listTimestamps(signatureId: string): Promise<TimestampSummary[]> {
    return (await api.get<TimestampSummary[]>(`/api/signatures/${signatureId}/timestamps`)).data
  },
  async verifyTimestamp(signatureId: string, timestampId: string): Promise<TimestampVerification> {
    return (await api.get<TimestampVerification>(`/api/signatures/${signatureId}/timestamps/${timestampId}/verify`)).data
  },
}
