import { Navigate, Route, Routes } from 'react-router-dom'
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

const DrawingViewerPage = lazy(() => import('./pages/DrawingViewerPage').then(module => ({ default: module.DrawingViewerPage })))

export default function App() {
  return <Routes>
    <Route path="/login" element={<LoginRoute />} />
    <Route element={<ProtectedShell />}>
      <Route path="/drawings" element={<DrawingsPage />} />
      <Route path="/drawings/:id" element={<DrawingDetailPage />} />
      <Route path="/drawings/:id/viewer" element={<Suspense fallback={<p className="state" role="status">Đang mở trình xem PDF…</p>}><DrawingViewerPage /></Suspense>} />
      <Route path="/inspections" element={<InspectionsPage />} />
      <Route path="/inspections/new" element={<NewInspectionPage />} />
      <Route path="/inspections/:id" element={<InspectionDetailPage />} />
      <Route path="/checklists" element={<ChecklistsPage />} />
      <Route path="/checklists/new" element={<NewChecklistPage />} />
      <Route path="/checklists/:id" element={<ChecklistDetailPage />} />
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

function LoginRoute() { const { token } = useAuth(); return token ? <HomeRedirect /> : <LoginPage /> }
function ProtectedShell() { const { token } = useAuth(); return token ? <AppShell /> : <Navigate to="/login" replace /> }
function HomeRedirect() { const { user } = useAuth(); return <Navigate to={user?.roles.includes('INSPECTOR') || user?.roles.includes('REVIEWER') ? '/inspections' : '/drawings'} replace /> }
