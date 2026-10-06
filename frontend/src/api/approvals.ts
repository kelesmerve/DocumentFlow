import { api } from './client'
import type { ApprovalDetails, ApprovalInput, ApprovalPage, Manager, WorkflowEvent } from '../types/api'

export const approvalsApi = {
  async managers(): Promise<Manager[]> { return (await api.get<Manager[]>('/api/users/managers')).data },
  async list(page = 1, pageSize = 20): Promise<ApprovalPage> { return (await api.get<ApprovalPage>('/api/approvals', { params: { page, pageSize } })).data },
  async get(id: string): Promise<ApprovalDetails> { return (await api.get<ApprovalDetails>(`/api/approvals/${id}`)).data },
  async submit(documentId: string, managerId: string, comment: string): Promise<ApprovalDetails> {
    return (await api.post<ApprovalDetails>(`/api/documents/${documentId}/submit-for-approval`, { managerId, comment })).data
  },
  async decide(id: string, decision: 'approve' | 'reject' | 'request-revision', input: ApprovalInput): Promise<ApprovalDetails> {
    return (await api.post<ApprovalDetails>(`/api/approvals/${id}/${decision}`, input)).data
  },
  async timeline(documentId: string): Promise<WorkflowEvent[]> { return (await api.get<WorkflowEvent[]>(`/api/documents/${documentId}/workflow`)).data },
  async download(documentId: string, versionNumber: number): Promise<void> {
    const response = await api.get<Blob>(`/api/documents/${documentId}/versions/${versionNumber}/download`, { responseType: 'blob' })
    const url = URL.createObjectURL(response.data)
    const anchor = document.createElement('a')
    anchor.href = url
    anchor.download = response.headers['content-disposition']?.match(/filename="?([^";]+)/i)?.[1] ?? 'belge'
    anchor.click()
    window.setTimeout(() => URL.revokeObjectURL(url), 1000)
  },
}
