import { Navigate, Route, Routes, useLocation } from 'react-router-dom'
import { lazy, Suspense } from 'react'
import './App.css'
import { useAuth } from './auth/AuthContext'
import { AppShell } from './components/AppShell'
import { DrawingDetailPage } from './pages/DrawingDetailPage'
import { DrawingsPage } from './pages/DrawingsPage'
import { LoginPage } from './pages/LoginPage'
import { FormTemplateAdminRoute } from './components/FormTemplateAdminRoute'
import { FormTemplatesPage } from './pages/FormTemplatesPage'
import { NewFormTemplatePage } from './pages/NewFormTemplatePage'
import { FormTemplateDetailPage } from './pages/FormTemplateDetailPage'
import { InspectionsPage } from './pages/InspectionsPage'
import { NewInspectionPage } from './pages/NewInspectionPage'
import { InspectionDetailPage } from './pages/InspectionDetailPage'
import { ConstructionObjectManagementRoute } from './components/ConstructionObjectManagementRoute'
import { ConstructionObjectsPage } from './pages/ConstructionObjectsPage'
import { NewConstructionObjectPage } from './pages/NewConstructionObjectPage'
import { ConstructionObjectDetailPage } from './pages/ConstructionObjectDetailPage'
import { ChecklistsPage } from './pages/ChecklistsPage'
import { NewChecklistPage } from './pages/NewChecklistPage'
import { ChecklistDetailPage } from './pages/ChecklistDetailPage'
import { RfiInspectorRoute } from './components/RfiInspectorRoute'
import { RfisPage } from './pages/RfisPage'
import { NewRfiPage } from './pages/NewRfiPage'
import { RfiDetailPage } from './pages/RfiDetailPage'
import { DashboardPage } from './pages/DashboardPage'

const DrawingViewerPage = lazy(() => import('./pages/DrawingViewerPage').then(module => ({ default: module.DrawingViewerPage })))

export default function App() {
  return <Routes>
    <Route path="/login" element={<LoginRoute />} />
    <Route element={<ProtectedShell />}>
      <Route path="/dashboard" element={<DashboardPage />} />
      <Route path="/drawings" element={<DrawingsPage />} />
      <Route path="/drawings/:id" element={<DrawingDetailPage />} />
      <Route path="/drawings/:id/viewer" element={<Suspense fallback={<p className="state" role="status">Đang mở trình xem PDF…</p>}><DrawingViewerPage /></Suspense>} />
      <Route path="/inspections" element={<InspectionsPage />} />
      <Route path="/inspections/new" element={<NewInspectionPage />} />
      <Route path="/inspections/:id" element={<InspectionDetailPage />} />
      <Route path="/checklists" element={<ChecklistsPage />} />
      <Route path="/checklists/new" element={<NewChecklistPage />} />
      <Route path="/checklists/:id" element={<ChecklistDetailPage />} />
      <Route path="/rfis" element={<RfisPage />} />
      <Route path="/rfis/:id" element={<RfiDetailPage />} />
      <Route element={<RfiInspectorRoute />}>
        <Route path="/rfis/new" element={<NewRfiPage />} />
      </Route>
      <Route element={<ConstructionObjectManagementRoute />}>
        <Route path="/construction-objects" element={<ConstructionObjectsPage />} />
        <Route path="/construction-objects/new" element={<NewConstructionObjectPage />} />
        <Route path="/construction-objects/:id" element={<ConstructionObjectDetailPage />} />
      </Route>
      <Route element={<FormTemplateAdminRoute />}>
        <Route path="/form-templates" element={<FormTemplatesPage />} />
        <Route path="/form-templates/new" element={<NewFormTemplatePage />} />
        <Route path="/form-templates/:id" element={<FormTemplateDetailPage />} />
      </Route>
    </Route>
    <Route path="*" element={<HomeRedirect />} />
  </Routes>
}

function LoginRoute() {
  const { token } = useAuth(); const location = useLocation()
  const requestedPath = safeReturnPath((location.state as { from?: unknown } | null)?.from)
  return token ? <Navigate to={requestedPath ?? '/dashboard'} replace /> : <LoginPage />
}
function ProtectedShell() {
  const { token } = useAuth(); const location = useLocation()
  const from = `${location.pathname}${location.search}${location.hash}`
  return token ? <AppShell /> : <Navigate to="/login" replace state={{ from }} />
}
function HomeRedirect() { return <Navigate to="/dashboard" replace /> }

function safeReturnPath(value: unknown) {
  if (typeof value !== 'string' || !value.startsWith('/')) return null
  let decoded = value
  let stable = false
  for (let index = 0; index < 8; index++) {
    if (decoded.startsWith('//') || decoded.includes('\\')) return null
    try {
      const next = decodeURIComponent(decoded)
      if (next === decoded) { stable = true; break }
      decoded = next
    } catch { return null }
  }
  if (!stable || decoded.startsWith('//') || decoded.includes('\\')) return null
  return value === '/login' ? null : value
}
