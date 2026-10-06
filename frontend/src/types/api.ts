export type UserRole = 'Employee' | 'Manager' | 'Admin'
export type DocumentStatus = 'Draft' | 'PendingApproval' | 'RevisionRequested' | 'Approved' | 'Rejected' | 'Signed' | 'Archived'
export type ApprovalStatus = 'Pending' | 'Approved' | 'Rejected' | 'RevisionRequested'

export interface User {
  id: string
  email: string
  firstName: string
  lastName: string
  role: UserRole
}

export interface LoginResponse {
  accessToken: string
  expiresAtUtc: string
  user: User
}

export interface DocumentSummary {
  id: string
  documentNumber: string
  title: string
  description: string | null
  category: string
  status: DocumentStatus
  ownerId: string
  createdAtUtc: string
  updatedAtUtc: string | null
}

export interface DocumentVersion {
  versionNumber: number
  originalFileName: string
  contentType: string
  fileSize: number
  fileHash: string
  uploadedByUserId: string
  createdAtUtc: string
}

export interface DocumentDetails {
  document: DocumentSummary
  versions: DocumentVersion[]
}

export interface DocumentPage {
  items: DocumentSummary[]
  page: number
  pageSize: number
  totalCount: number
}

export interface DocumentInput {
  title: string
  description: string
  category: string
  file: File
}

export interface Manager { id: string; firstName: string; lastName: string; email: string }
export interface ApprovalSummary {
  id: string; documentId: string; documentNumber: string; documentTitle: string; category: string
  versionNumber: number; requestedBy: string; requestedByEmail: string; requestComment: string | null
  status: ApprovalStatus; createdAtUtc: string
}
export interface ApprovalDetails {
  approval: ApprovalSummary; description: string; originalFileName: string; contentType: string
  fileSize: number; fileHash: string; assignedTo: string; decisionComment: string | null; decidedAtUtc: string | null
}
export interface ApprovalInput { comment: string }
export interface ApprovalPage { items: ApprovalSummary[]; page: number; pageSize: number; totalCount: number }
export interface WorkflowEvent { kind: string; title: string; actor: string; detail: string | null; versionNumber: number | null; atUtc: string }

export interface ApiErrorBody {
  message?: string
  title?: string
  errors?: Record<string, string[]>
}
