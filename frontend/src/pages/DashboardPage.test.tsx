import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import '@testing-library/jest-dom/vitest'
import { MemoryRouter } from 'react-router-dom'
import { StrictMode, useState } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import App from '../App'
import { getDashboard, getRfis, login } from '../api/client'
import type { DashboardResponse } from '../api/types'
import { AuthProvider } from '../auth/AuthContext'
import { DashboardPage } from './DashboardPage'

vi.mock('../api/client', async importOriginal => {
  const actual = await importOriginal<typeof import('../api/client')>()
  return { ...actual, getDashboard: vi.fn(), getRfis: vi.fn(), login: vi.fn() }
})

const dashboard: DashboardResponse = {
  generatedAtUtc: '2026-10-28T08:30:00Z',
  roles: ['INSPECTOR'],
  scopeMode: 'Own',
  inspectionSummary: { total: 12, openWork: 5, awaitingMyAction: 2, statusCounts: { draft: 2, submitted: 2, underReview: 1, approved: 5, recheck: 1, rejected: 1 } },
  checklistSummary: { total: 8, openWork: 4, awaitingMyAction: 1, statusCounts: { draft: 1, inProgress: 1, submitted: 1, underReview: 1, approved: 2, recheck: 1, rejected: 1 } },
  rfiSummary: { total: 6, openWork: 3, awaitingMyAction: 2, awaitingResponse: 2, awaitingInspectorAction: 1, overdueOpen: 1, statusCounts: { draft: 1, open: 2, responded: 1, closed: 1, cancelled: 1 } },
  actionQueue: [
    { module: 'Inspection', entityId: '11111111-1111-4111-8111-111111111111', number: 'INS-2026-000001', displayTitle: 'Fire inspection', status: 'Draft', actionKind: 'Edit', dueAtUtc: null, updatedAtUtc: '2026-10-28T08:00:00Z', constructionObject: { id: 'a', code: 'FIRE-01', name: 'Fire system' }, isOverdue: false },
    { module: 'Checklist', entityId: '22222222-2222-4222-8222-222222222222', number: 'CHK-2026-000001', displayTitle: 'Fire checklist', status: 'InProgress', actionKind: 'Edit', dueAtUtc: null, updatedAtUtc: '2026-10-28T08:05:00Z', constructionObject: { id: 'a', code: 'FIRE-01', name: 'Fire system' }, isOverdue: false },
    { module: 'Rfi', entityId: '33333333-3333-4333-8333-333333333333', number: 'RFI-2026-000001', displayTitle: 'Clarify valve location', status: 'Open', actionKind: 'Respond', dueAtUtc: '2026-10-27T08:00:00Z', updatedAtUtc: '2026-10-28T08:10:00Z', constructionObject: { id: 'a', code: 'FIRE-01', name: 'Fire system' }, isOverdue: true },
  ],
}

beforeEach(() => {
  vi.mocked(getDashboard).mockResolvedValue(dashboard)
  vi.mocked(getRfis).mockResolvedValue({ items: [], pageNumber: 1, pageSize: 20, totalCount: 0, totalPages: 0 })
  vi.mocked(login).mockResolvedValue({ accessToken: 'opaque-test-token', tokenType: 'Bearer', expiresAt: '2026-10-29T00:00:00Z', user: testUser('INSPECTOR') })
})
afterEach(async () => { cleanup(); await new Promise(resolve => window.setTimeout(resolve, 0)); sessionStorage.clear(); vi.resetAllMocks() })

describe('Dashboard UI', () => {
  it('protects the route and does not request Dashboard anonymously', async () => {
    renderApp(['/dashboard'])
    expect(await screen.findByRole('heading', { name: 'Đăng nhập' })).toBeInTheDocument()
    expect(getDashboard).not.toHaveBeenCalled()
  })

  it('renders a stable loading state', () => {
    setSession('INSPECTOR'); vi.mocked(getDashboard).mockImplementation(() => new Promise(() => undefined))
    renderDashboard()
    expect(screen.getByRole('status')).toHaveTextContent('Đang tải dữ liệu tổng quan')
    expect(document.querySelectorAll('.dashboard-skeleton')).toHaveLength(3)
  })

  it('renders all summaries, RFI metrics, scope and role from one response', async () => {
    setSession('INSPECTOR'); renderDashboard(true)
    expect(await screen.findByText('Hồ sơ do tôi tạo')).toBeInTheDocument()
    expect(screen.getByText('Vai trò:').parentElement).toHaveTextContent('Inspector')
    expect(screen.getAllByRole('heading', { name: 'Inspection' })).toHaveLength(2)
    expect(screen.getAllByRole('heading', { name: 'Checklist' })).toHaveLength(2)
    expect(screen.getAllByRole('heading', { name: 'RFI' })).toHaveLength(2)
    expect(screen.getByText('Chờ phản hồi').nextElementSibling).toHaveTextContent('2')
    expect(screen.getByText('Chờ Inspector xử lý').nextElementSibling).toHaveTextContent('1')
    expect(screen.getByText('Đang mở quá hạn').nextElementSibling).toHaveTextContent('1')
    expect(getDashboard).toHaveBeenCalledOnce()
    expect(getDashboard).toHaveBeenCalledWith(expect.any(AbortSignal))
  })

  it('renders safe action queues with canonical detail links and overdue text', async () => {
    setSession('INSPECTOR'); renderDashboard()
    expect(await screen.findByText('INS-2026-000001')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /INS-2026-000001/ })).toHaveAttribute('href', '/inspections/11111111-1111-4111-8111-111111111111')
    expect(screen.getByRole('link', { name: /CHK-2026-000001/ })).toHaveAttribute('href', '/checklists/22222222-2222-4222-8222-222222222222')
    expect(screen.getByRole('link', { name: /RFI-2026-000001/ })).toHaveAttribute('href', '/rfis/33333333-3333-4333-8333-333333333333')
    expect(screen.getByText('Quá hạn')).toBeInTheDocument()
  })

  it('shows the Administrator empty queue without inventing work', async () => {
    setSession('ADMINISTRATOR'); vi.mocked(getDashboard).mockResolvedValue({ ...dashboard, roles: ['ADMINISTRATOR'], scopeMode: 'Full', actionQueue: [] })
    renderDashboard()
    expect(await screen.findByText('Toàn bộ dữ liệu vận hành')).toBeInTheDocument()
    expect(screen.getAllByText('Không có công việc cần xử lý.')).toHaveLength(3)
  })

  it('renders the all-zero empty state', async () => {
    setSession('MANAGER'); vi.mocked(getDashboard).mockResolvedValue(emptyDashboard)
    renderDashboard()
    expect(await screen.findByText('Chưa có dữ liệu trong phạm vi này')).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Tổng quan ba module' })).toBeInTheDocument()
  })

  it('issues exactly one additional request for a manual refresh', async () => {
    const user = userEvent.setup(); setSession('MANAGER'); renderDashboard()
    await screen.findByText('INS-2026-000001'); expect(getDashboard).toHaveBeenCalledOnce()
    await user.click(screen.getByRole('button', { name: 'Làm mới' }))
    await waitFor(() => expect(getDashboard).toHaveBeenCalledTimes(2))
  })

  it('shows a generic error and retries only when requested', async () => {
    const user = userEvent.setup(); setSession('REVIEWER')
    vi.mocked(getDashboard).mockRejectedValueOnce(new Error('database details')).mockResolvedValueOnce(dashboard)
    renderDashboard()
    expect(await screen.findByRole('alert')).toHaveTextContent('Không thể tải Dashboard')
    expect(screen.queryByText('database details')).not.toBeInTheDocument()
    expect(getDashboard).toHaveBeenCalledOnce()
    await user.click(screen.getByRole('button', { name: 'Thử lại' }))
    expect(await screen.findByText('INS-2026-000001')).toBeInTheDocument()
    expect(getDashboard).toHaveBeenCalledTimes(2)
  })

  it('does not render sensitive fields even if a malformed fixture adds them', async () => {
    setSession('MANAGER')
    const unsafe = { ...dashboard, dataJson: 'SENSITIVE_DATA_JSON', schemaJson: 'SENSITIVE_SCHEMA_JSON', storageKey: 'SENSITIVE_STORAGE_KEY', question: 'SENSITIVE_RFI_QUESTION' } as DashboardResponse
    vi.mocked(getDashboard).mockResolvedValue(unsafe); renderDashboard(); await screen.findByText('INS-2026-000001')
    expect(screen.queryByText(/SENSITIVE_/)).not.toBeInTheDocument()
  })

  it('cancels the request when the route unmounts', async () => {
    setSession('INSPECTOR'); let signal: AbortSignal | undefined
    vi.mocked(getDashboard).mockImplementation(next => { signal = next; return new Promise(() => undefined) })
    const view = renderDashboard(); await waitFor(() => expect(signal).toBeDefined()); view.unmount()
    await waitFor(() => expect(signal?.aborted).toBe(true))
  })

  it('keeps a shared in-flight request alive while another valid consumer remains', async () => {
    const user = userEvent.setup(); setSession('INSPECTOR'); let signal: AbortSignal | undefined; let resolve!: (value: DashboardResponse) => void
    const pending = new Promise<DashboardResponse>(complete => { resolve = complete })
    vi.mocked(getDashboard).mockImplementation(next => { signal = next; return pending })
    const firstClient = testClient(); const secondClient = testClient()
    function Consumers() {
      const [firstVisible, setFirstVisible] = useState(true)
      return <><button onClick={() => setFirstVisible(false)}>Remove first consumer</button>{firstVisible && <QueryClientProvider client={firstClient}><DashboardPage /></QueryClientProvider>}<QueryClientProvider client={secondClient}><DashboardPage /></QueryClientProvider></>
    }
    render(<MemoryRouter><AuthProvider><Consumers /></AuthProvider></MemoryRouter>)
    await waitFor(() => expect(getDashboard).toHaveBeenCalledOnce()); await user.click(screen.getByRole('button', { name: 'Remove first consumer' })); await new Promise(complete => window.setTimeout(complete, 0))
    expect(signal?.aborted).toBe(false)
    await act(async () => resolve(dashboard)); expect(await screen.findByText('INS-2026-000001')).toBeInTheDocument()
  })

  it.each(['INSPECTOR', 'REVIEWER', 'MANAGER', 'ADMINISTRATOR'])('shows Dashboard navigation to %s', async role => {
    setSession(role); renderApp(['/dashboard'])
    expect(await screen.findByRole('link', { name: 'Dashboard' })).toBeInTheDocument()
  })

  it('lands on Dashboard after login and respects a valid intended URL', async () => {
    const user = userEvent.setup(); renderApp([{ pathname: '/login', state: { from: '/rfis' } }])
    await user.type(screen.getByLabelText('Email'), 'inspector@local.test'); await user.type(screen.getByLabelText('Mật khẩu'), 'safe-test-value'); await user.click(screen.getByRole('button', { name: 'Đăng nhập' }))
    expect(await screen.findByRole('heading', { name: 'RFIs' })).toBeInTheDocument()
    cleanup(); sessionStorage.clear(); vi.clearAllMocks(); vi.mocked(login).mockResolvedValue({ accessToken: 'opaque-test-token', tokenType: 'Bearer', expiresAt: '2026-10-29T00:00:00Z', user: testUser('INSPECTOR') }); vi.mocked(getDashboard).mockResolvedValue(dashboard)
    renderApp(['/login']); await user.type(screen.getByLabelText('Email'), 'inspector@local.test'); await user.type(screen.getByLabelText('Mật khẩu'), 'safe-test-value'); await user.click(screen.getByRole('button', { name: 'Đăng nhập' }))
    expect(await screen.findByRole('heading', { name: 'Dashboard' })).toBeInTheDocument()
  })

  it.each(['https://example.invalid/steal', '//example.invalid/steal', '/\\example.invalid', '/%2F%2Fexample.invalid', '/%255Cexample.invalid'])('rejects unsafe intended URL %s and falls back to Dashboard', async from => {
    setSession('INSPECTOR'); renderApp([{ pathname: '/login', state: { from } }])
    expect(await screen.findByRole('heading', { name: 'Dashboard' })).toBeInTheDocument()
  })

  it('does not flash or reuse an Inspector snapshot after logout and Manager login', async () => {
    const user = userEvent.setup(); setSession('INSPECTOR'); let resolveManager!: (value: DashboardResponse) => void
    const managerResponse = new Promise<DashboardResponse>(complete => { resolveManager = complete })
    vi.mocked(getDashboard).mockResolvedValueOnce(dashboard).mockReturnValueOnce(managerResponse)
    vi.mocked(login).mockResolvedValue({ accessToken: 'manager-token-placeholder', tokenType: 'Bearer', expiresAt: '2026-10-29T00:00:00Z', user: testUser('MANAGER') })
    renderApp(['/dashboard']); expect(await screen.findByText('Fire inspection')).toBeInTheDocument(); await user.click(screen.getByRole('button', { name: 'Đăng xuất' }))
    await user.type(screen.getByLabelText('Email'), 'manager@local.test'); await user.type(screen.getByLabelText('Mật khẩu'), 'safe-test-value'); await user.click(screen.getByRole('button', { name: 'Đăng nhập' }))
    expect(await screen.findByText('Đang tải dữ liệu tổng quan…')).toBeInTheDocument(); expect(screen.queryByText('Fire inspection')).not.toBeInTheDocument()
    await act(async () => resolveManager({ ...dashboard, roles: ['MANAGER'], scopeMode: 'Full', actionQueue: [] }))
    expect(await screen.findByText('Toàn bộ dữ liệu vận hành')).toBeInTheDocument(); expect(getDashboard).toHaveBeenCalledTimes(2)
  })
})

const emptyDashboard: DashboardResponse = {
  generatedAtUtc: '2026-10-28T08:30:00Z', roles: ['MANAGER'], scopeMode: 'Full',
  inspectionSummary: { total: 0, openWork: 0, awaitingMyAction: 0, statusCounts: { draft: 0, submitted: 0, underReview: 0, approved: 0, recheck: 0, rejected: 0 } },
  checklistSummary: { total: 0, openWork: 0, awaitingMyAction: 0, statusCounts: { draft: 0, inProgress: 0, submitted: 0, underReview: 0, approved: 0, recheck: 0, rejected: 0 } },
  rfiSummary: { total: 0, openWork: 0, awaitingMyAction: 0, awaitingResponse: 0, awaitingInspectorAction: 0, overdueOpen: 0, statusCounts: { draft: 0, open: 0, responded: 0, closed: 0, cancelled: 0 } }, actionQueue: [],
}

function testUser(role: string) { return { id: 'u1', email: 'user@local.test', fullName: 'Demo User', roles: [role] } }
function setSession(role: string) { sessionStorage.setItem('construction-inspection.auth', JSON.stringify({ token: 'opaque-test-token', user: testUser(role) })) }
function renderDashboard(strict = false) { return renderPage(strict ? <StrictMode><DashboardPage /></StrictMode> : <DashboardPage />, ['/dashboard']) }
function renderApp(initialEntries: Parameters<typeof MemoryRouter>[0]['initialEntries']) { return renderPage(<App />, initialEntries) }
function renderPage(node: React.ReactNode, initialEntries: Parameters<typeof MemoryRouter>[0]['initialEntries']) {
  const client = testClient()
  return render(<MemoryRouter initialEntries={initialEntries}><QueryClientProvider client={client}><AuthProvider>{node}</AuthProvider></QueryClientProvider></MemoryRouter>)
}
function testClient() { return new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } }) }
