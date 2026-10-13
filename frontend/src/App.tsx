import { Navigate, Route, Routes } from 'react-router-dom'
import { ProtectedLayout } from './auth/ProtectedLayout'
import { LoginPage } from './pages/LoginPage'
import { NotFoundPage } from './pages/NotFoundPage'
import { DashboardPage } from './pages/DashboardPage'
import { DocumentsPage } from './pages/DocumentsPage'
import { NewDocumentPage } from './pages/NewDocumentPage'
import { DocumentDetailsPage } from './pages/DocumentDetailsPage'
import { ApprovalsPage } from './pages/ApprovalsPage'
import { ApprovalDetailsPage } from './pages/ApprovalDetailsPage'
import { AuditPage } from './pages/AuditPage'
import { AdminOnly } from './auth/AdminOnly'
import { ManagerOnly } from './auth/ManagerOnly'
import { MobileSignPage } from './pages/MobileSignPage'

export function App() {
  return <Routes>
    <Route path="/login" element={<LoginPage />} />
    <Route element={<ProtectedLayout />}>
      <Route path="/" element={<DashboardPage />} />
      <Route path="/documents" element={<DocumentsPage />} />
      <Route path="/documents/new" element={<NewDocumentPage />} />
      <Route path="/documents/:id" element={<DocumentDetailsPage />} />
      <Route path="/approvals" element={<ApprovalsPage />} />
      <Route path="/approvals/:id" element={<ApprovalDetailsPage />} />
      <Route path="/audit" element={<AdminOnly><AuditPage /></AdminOnly>} />
      <Route path="/mobile-sign" element={<ManagerOnly><MobileSignPage /></ManagerOnly>} />
      <Route path="/404" element={<NotFoundPage />} />
      <Route path="*" element={<Navigate to="/404" replace />} />
    </Route>
  </Routes>
}
