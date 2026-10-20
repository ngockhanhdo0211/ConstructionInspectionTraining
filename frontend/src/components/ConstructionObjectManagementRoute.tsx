import { Navigate, Outlet } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { canViewConstructionObjectManagement } from '../constructionObjects/access'

export function ConstructionObjectManagementRoute() {
  const { user } = useAuth()
  const fallback = user?.roles.includes('INSPECTOR') || user?.roles.includes('REVIEWER') ? '/inspections' : '/drawings'
  return canViewConstructionObjectManagement(user) ? <Outlet /> : <Navigate to={fallback} replace />
}
