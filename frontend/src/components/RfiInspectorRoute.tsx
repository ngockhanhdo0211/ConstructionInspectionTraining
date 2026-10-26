import { Navigate, Outlet } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { canCreateRfi } from '../rfis/access'

export function RfiInspectorRoute() {
  const { user } = useAuth()
  return canCreateRfi(user) ? <Outlet /> : <Navigate to="/rfis" replace />
}
