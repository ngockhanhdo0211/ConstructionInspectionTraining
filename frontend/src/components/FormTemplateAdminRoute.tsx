import { Navigate, Outlet } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { isFormTemplateAdministrator } from '../formTemplates/access'

export function FormTemplateAdminRoute() {
  const { user } = useAuth()
  return isFormTemplateAdministrator(user?.roles) ? <Outlet /> : <Navigate to="/drawings" replace />
}
