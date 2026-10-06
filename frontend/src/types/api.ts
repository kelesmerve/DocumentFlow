export type UserRole = 'Employee' | 'Manager' | 'Admin'
export type DocumentStatus = 'Draft' | 'PendingApproval' | 'Approved' | 'Rejected' | 'Signed' | 'Archived'

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

export interface ApiErrorBody {
  message?: string
  title?: string
  errors?: Record<string, string[]>
}
