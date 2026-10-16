import { NavLink, Outlet } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { isFormTemplateAdministrator } from '../formTemplates/access'

export function AppShell() {
  const { user, logout } = useAuth()
  return <div className="shell">
    <header className="mobile-bar"><span className="brand">CI</span><span>Drawing registry</span></header>
    <aside className="rail" aria-label="Điều hướng chính">
      <div className="brand" aria-label="Construction Inspection">CI</div>
      <nav><NavLink to="/drawings">Drawings</NavLink>{isFormTemplateAdministrator(user?.roles) && <NavLink to="/form-templates">Form Templates</NavLink>}</nav>
      <div className="rail-user"><span title={user?.email}>{user?.fullName}</span><button className="button ghost" onClick={logout}>Đăng xuất</button></div>
    </aside>
    <main className="workspace"><Outlet /></main>
  </div>
}
