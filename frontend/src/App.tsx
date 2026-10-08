import { Navigate, Route, Routes } from 'react-router-dom'
import { lazy, Suspense } from 'react'
import './App.css'
import { useAuth } from './auth/AuthContext'
import { AppShell } from './components/AppShell'
import { DrawingDetailPage } from './pages/DrawingDetailPage'
import { DrawingsPage } from './pages/DrawingsPage'
import { LoginPage } from './pages/LoginPage'

const DrawingViewerPage = lazy(() => import('./pages/DrawingViewerPage').then(module => ({ default: module.DrawingViewerPage })))

export default function App() {
  return <Routes>
    <Route path="/login" element={<LoginRoute />} />
    <Route element={<ProtectedShell />}>
      <Route path="/drawings" element={<DrawingsPage />} />
      <Route path="/drawings/:id" element={<DrawingDetailPage />} />
      <Route path="/drawings/:id/viewer" element={<Suspense fallback={<p className="state" role="status">Đang mở trình xem PDF…</p>}><DrawingViewerPage /></Suspense>} />
    </Route>
    <Route path="*" element={<Navigate to="/drawings" replace />} />
  </Routes>
}

function LoginRoute() { const { token } = useAuth(); return token ? <Navigate to="/drawings" replace /> : <LoginPage /> }
function ProtectedShell() { const { token } = useAuth(); return token ? <AppShell /> : <Navigate to="/login" replace /> }
