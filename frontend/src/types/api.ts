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
  id: string
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
export type AuditAction = 'DocumentCreated' | 'DocumentVersionUploaded' | 'ApprovalSubmitted' | 'ApprovalApproved' | 'ApprovalRejected' | 'RevisionRequested' | 'DocumentDownloaded' | 'DocumentSigned' | 'TimestampCreated' | 'MobileSignatureRequested' | 'MobileSignatureApproved' | 'MobileSignatureRejected' | 'MobileSignatureCompleted'
export interface AuditEntry { id: string; action: AuditAction; actionDisplayName: string; userId: string; userName: string; userEmail: string; documentId: string | null; documentNumber: string | null; documentTitle: string | null; documentVersionId: string | null; versionNumber: number | null; approvalRequestId: string | null; description: string | null; createdAtUtc: string }
export interface AuditPage { items: AuditEntry[]; page: number; pageSize: number; totalCount: number }
export interface SignatureSummary { id: string; documentId: string; documentVersionId: string; signedByUserId: string; signedByName: string; signatureAlgorithm: string; hashAlgorithm: string; documentHash: string; publicKeyFingerprint: string; signedAtUtc: string; method: 'Direct' | 'MobileSimulation' }
export type MobileSignatureStatus = 'Pending' | 'Approved' | 'Rejected' | 'Expired'
export interface MobileSignatureRequest { id: string; documentId: string; documentVersionId: string; documentNumber: string; documentTitle: string; versionNumber: number; status: MobileSignatureStatus; createdAtUtc: string; expiresAtUtc: string; completedAtUtc: string | null; documentSignatureId: string | null }
export interface SignatureList { items: SignatureSummary[]; canSign: boolean }
export interface SignatureVerification { signatureId: string; signedByUserId: string; signedByName: string; signatureAlgorithm: string; hashAlgorithm: string; publicKeyFingerprint: string; signedAtUtc: string; result: 'Valid' | 'Invalid' | 'Unverifiable'; message: string }
export interface TimestampSummary { id: string; documentSignatureId: string; timestampAuthority: string; serialNumber: string; messageImprintAlgorithm: string; messageImprint: string; genTimeUtc: string; createdAtUtc: string; providerType: string }
export interface TimestampVerification { result: 'Valid' | 'Invalid' | 'Unverifiable'; message: string; authority: string | null; serialNumber: string | null; algorithm: string | null; imprint: string | null; genTimeUtc: string | null }
export interface TimestampCreateResponse { timestamp: TimestampSummary; verification: TimestampVerification }

export interface ApiErrorBody {
  message?: string
  title?: string
  errors?: Record<string, string[]>
}
