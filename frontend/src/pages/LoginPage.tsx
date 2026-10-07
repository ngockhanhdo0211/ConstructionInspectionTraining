import { useState, type FormEvent } from 'react'
import { useAuth } from '../auth/AuthContext'

export function LoginPage() {
  const { login } = useAuth(); const [email, setEmail] = useState(''); const [password, setPassword] = useState(''); const [error, setError] = useState(''); const [busy, setBusy] = useState(false)
  async function submit(event: FormEvent) { event.preventDefault(); setBusy(true); setError(''); try { await login(email, password) } catch { setError('Email hoặc mật khẩu không đúng.'); setBusy(false) } }
  return <main className="login-page"><section className="login-copy"><div className="brand large">CI</div><h1>Hồ sơ bản vẽ, rõ ràng từ nguồn.</h1><p>Đăng nhập để tải lên và tra cứu metadata PDF của dự án.</p></section>
    <form className="login-card" onSubmit={submit}><h2>Đăng nhập</h2><label>Email<input type="email" autoComplete="username" required disabled={busy} value={email} onChange={e => setEmail(e.target.value)} /></label><label>Mật khẩu<input type="password" autoComplete="current-password" required disabled={busy} value={password} onChange={e => setPassword(e.target.value)} /></label>{error && <p className="notice error" role="alert">{error}</p>}<button className="button primary" disabled={busy}>{busy ? 'Đang xác thực…' : 'Đăng nhập'}</button></form>
  </main>
}
