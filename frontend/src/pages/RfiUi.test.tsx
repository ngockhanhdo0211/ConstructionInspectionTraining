import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import '@testing-library/jest-dom/vitest'
import { MemoryRouter, Navigate, Outlet, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError, createRfi, getInspectionConstructionObjects, getRfi, getRfiHistory, getRfiResponderOptions, getRfis, reassignRfi, respondRfi, saveRfiDraft, submitRfi } from '../api/client'
import type { RfiDetail } from '../api/types'
import { AuthProvider, useAuth } from '../auth/AuthContext'
import { AppShell } from '../components/AppShell'
import { NewRfiPage } from './NewRfiPage'
import { RfiDetailPage } from './RfiDetailPage'
import { RfisPage } from './RfisPage'

vi.mock('../api/client', async importOriginal => {
  const actual = await importOriginal<typeof import('../api/client')>()
  return { ...actual, getRfis: vi.fn(), getRfi: vi.fn(), getRfiHistory: vi.fn(), getRfiResponderOptions: vi.fn(), createRfi: vi.fn(), saveRfiDraft: vi.fn(), submitRfi: vi.fn(), reassignRfi: vi.fn(), respondRfi: vi.fn(), requestRfiClarification: vi.fn(), closeRfi: vi.fn(), cancelRfi: vi.fn(), getInspectionConstructionObjects: vi.fn() }
})

const id = '11111111-1111-4111-8111-111111111111'
const objectId = '22222222-2222-4222-8222-222222222222'
const emptyPage = { items: [], pageNumber: 1, pageSize: 20, totalCount: 0, totalPages: 0 }
const detail: RfiDetail = {
  id, number: 'RFI-2026-000001', constructionObjectId: objectId, constructionObjectCode: 'FIRE-01', constructionObjectName: 'Fire system', subject: 'Confirm sprinkler', question: 'Please confirm location.', status: 'Draft', createdByUserId: 'u1', assignedToUserId: null, assignedToDisplayName: null, dueAtUtc: null, completedAt: null, createdAt: '2026-10-26T01:00:00Z', updatedAt: null, entries: [], rfiRowVersion: 'r1', processRowVersion: 'p1',
}

beforeEach(() => {
  vi.mocked(getRfis).mockResolvedValue(emptyPage)
  vi.mocked(getRfiHistory).mockResolvedValue(emptyPage)
  vi.mocked(getRfiResponderOptions).mockResolvedValue({ ...emptyPage, items: [{ userId: 'u2', displayName: 'Demo Reviewer' }] })
})
afterEach(() => { cleanup(); sessionStorage.clear(); vi.resetAllMocks(); vi.restoreAllMocks() })

describe('RFI UI', () => {
  it.each(['INSPECTOR', 'REVIEWER', 'MANAGER', 'ADMINISTRATOR'])('shows RFI navigation to %s', role => {
    setSession(role); renderPage(<Routes><Route element={<AppShell />}><Route path="/" element={<p>Home</p>} /></Route></Routes>, ['/'])
    expect(screen.getByRole('link', { name: 'RFIs' })).toBeInTheDocument()
  })

  it('protects anonymous list through the authenticated shell', async () => {
    renderPage(<Routes><Route path="/login" element={<h1>Login</h1>} /><Route element={<ProtectedForTest />}><Route path="/rfis" element={<RfisPage />} /></Route></Routes>, ['/rfis'])
    expect(await screen.findByRole('heading', { name: 'Login' })).toBeInTheDocument(); expect(getRfis).not.toHaveBeenCalled()
  })

  it('renders metadata, persists filters in URL and cancels list request', async () => {
    const user = userEvent.setup(); setSession('REVIEWER')
    vi.mocked(getRfis).mockResolvedValue({ items: [{ id, number: 'RFI-2026-000001', subject: 'Confirm sprinkler', constructionObjectId: objectId, constructionObjectCode: 'FIRE-01', constructionObjectName: 'Fire system', status: 'Open', createdByUserId: 'u1', assignedToUserId: 'u2', assignedToDisplayName: 'Demo Reviewer', dueAtUtc: '2026-10-25T01:00:00Z', isOverdue: true, createdAt: '2026-10-24T01:00:00Z', updatedAt: null, rfiRowVersion: 'r1', processRowVersion: 'p1' }], pageNumber: 1, pageSize: 20, totalCount: 1, totalPages: 1 })
    renderPage(<RfisPage />); expect(await screen.findByText('RFI-2026-000001')).toBeInTheDocument(); expect(screen.getByText(/Hạn:.*Quá hạn/)).toBeInTheDocument()
    await user.type(screen.getByLabelText('Tìm theo số hoặc tiêu đề'), 'sprinkler'); await user.selectOptions(screen.getByLabelText('Trạng thái'), 'Open'); await user.click(screen.getByLabelText('Giao cho tôi')); await user.click(screen.getByRole('button', { name: 'Tìm' }))
    await waitFor(() => expect(getRfis).toHaveBeenLastCalledWith(expect.objectContaining({ search: 'sprinkler', status: 'Open', assignedToMe: true }), expect.any(AbortSignal)))
    cleanup(); let signal: AbortSignal | undefined; vi.mocked(getRfis).mockImplementation((_input, next) => { signal = next; return new Promise(() => undefined) }); const view = renderPage(<RfisPage />); await waitFor(() => expect(signal).toBeDefined()); view.unmount(); expect(signal?.aborted).toBe(true)
  })

  it('creates a trimmed Draft once', async () => {
    const user = userEvent.setup(); setSession('INSPECTOR')
    vi.mocked(getInspectionConstructionObjects).mockResolvedValue({ items: [{ id: objectId, parentId: null, code: 'FIRE-01', name: 'Fire system', objectType: 'WorkItem', isActive: true, rowVersion: 'o1' }], pageNumber: 1, pageSize: 20, totalCount: 1, totalPages: 1 })
    vi.mocked(createRfi).mockResolvedValue({ rfiId: id, status: 'Draft', assignedToUserId: null, dueAtUtc: null, completedAt: null, rfiRowVersion: 'r1', processRowVersion: 'p1' })
    renderPage(<Routes><Route path="/rfis/new" element={<NewRfiPage />} /><Route path="/rfis/:id" element={<h1>Created RFI</h1>} /></Routes>, ['/rfis/new'])
    await screen.findByRole('option', { name: /FIRE-01/ }); await user.selectOptions(screen.getByLabelText('Construction Object *'), objectId); await user.type(screen.getByLabelText(/^Tiêu đề/), '  Confirm sprinkler  '); await user.type(screen.getByLabelText(/^Câu hỏi/), ' Please confirm. '); await user.click(screen.getByRole('button', { name: 'Tạo Draft RFI' }))
    expect(createRfi).toHaveBeenCalledOnce(); expect(createRfi).toHaveBeenCalledWith({ constructionObjectId: objectId, subject: 'Confirm sprinkler', question: 'Please confirm.' }); expect(await screen.findByRole('heading', { name: 'Created RFI' })).toBeInTheDocument()
  })

  it('keeps local Draft content on 409 and does not retry', async () => {
    const user = userEvent.setup(); setSession('INSPECTOR'); vi.mocked(getRfi).mockResolvedValue(detail); vi.mocked(saveRfiDraft).mockRejectedValue(new ApiError(409, { status: 409, code: 'rfi.concurrency_conflict' })); vi.spyOn(window, 'confirm').mockReturnValue(false)
    renderDetail(); const question = await screen.findByLabelText(/^Câu hỏi/); await user.clear(question); await user.type(question, 'Local unsaved question'); await user.click(screen.getByRole('button', { name: 'Lưu Draft' }))
    expect(await screen.findByRole('alert')).toHaveTextContent(/thay đổi|máy chủ/i); expect(question).toHaveValue('Local unsaved question'); expect(saveRfiDraft).toHaveBeenCalledOnce(); await user.click(screen.getByRole('button', { name: 'Tải lại dữ liệu mới nhất' })); expect(window.confirm).toHaveBeenCalled(); expect(question).toHaveValue('Local unsaved question')
  })

  it('submits only for the owner and sends both row versions', async () => {
    const user = userEvent.setup(); setSession('INSPECTOR'); vi.mocked(getRfi).mockResolvedValue(detail); vi.mocked(submitRfi).mockResolvedValue({ rfiId: id, status: 'Open', assignedToUserId: 'u2', dueAtUtc: null, completedAt: null, rfiRowVersion: 'r2', processRowVersion: 'p2' })
    renderDetail(); await user.click(await screen.findByRole('button', { name: 'Submit RFI' })); await user.selectOptions(await screen.findByLabelText('Reviewer *'), 'u2'); await user.click(screen.getByRole('button', { name: 'Xác nhận' })); await waitFor(() => expect(submitRfi).toHaveBeenCalledWith(id, 'u2', 'r1', 'p1'))
  })

  it('blocks workflow while a Draft has unsaved changes', async () => {
    const user = userEvent.setup(); setSession('INSPECTOR'); vi.mocked(getRfi).mockResolvedValue(detail); renderDetail()
    await user.type(await screen.findByLabelText(/^Câu hỏi/), ' changed')
    expect(screen.getByRole('button', { name: 'Submit RFI' })).toBeDisabled(); expect(screen.getByRole('button', { name: 'Cancel' })).toBeDisabled(); expect(screen.getByText(/lưu Draft trước/)).toBeInTheDocument()
  })

  it('shows Respond only to the assigned Reviewer and renders exchange text without HTML injection', async () => {
    const user = userEvent.setup(); setSession('REVIEWER'); vi.mocked(getRfi).mockResolvedValue({ ...detail, status: 'Open', assignedToUserId: 'u1', assignedToDisplayName: 'Reviewer', entries: [{ id: 'e1', entryNumber: 1, entryType: 'ClarificationRequest', content: '<script>alert(1)</script>', createdByUserId: 'u3', createdByDisplayName: '<img onerror=alert(1)>', createdAtUtc: '2026-10-26T02:00:00Z' }] }); vi.mocked(respondRfi).mockResolvedValue({ rfiId: id, status: 'Responded', assignedToUserId: 'u1', dueAtUtc: null, completedAt: null, rfiRowVersion: 'r2', processRowVersion: 'p2' })
    renderDetail(); expect(await screen.findByText('<script>alert(1)</script>')).toBeInTheDocument(); expect(document.querySelector('script')).toBeNull(); await user.click(screen.getByRole('button', { name: 'Respond' })); await user.type(screen.getByLabelText('Nội dung phản hồi *'), 'Installed per approved drawing.'); await user.click(screen.getByRole('button', { name: 'Xác nhận' })); await waitFor(() => expect(respondRfi).toHaveBeenCalledOnce())
  })

  it('does not expose Respond to an unassigned Reviewer', async () => {
    setSession('REVIEWER'); vi.mocked(getRfi).mockResolvedValue({ ...detail, status: 'Open', assignedToUserId: 'someone-else' }); renderDetail()
    await screen.findByText('RFI-2026-000001'); expect(screen.queryByRole('button', { name: 'Respond' })).not.toBeInTheDocument()
  })

  it('lets Manager reassign but excludes the current Reviewer and requires a reason', async () => {
    const user = userEvent.setup(); setSession('MANAGER'); vi.mocked(getRfi).mockResolvedValue({ ...detail, status: 'Open', assignedToUserId: 'u-current', assignedToDisplayName: 'Current Reviewer' }); vi.mocked(getRfiResponderOptions).mockResolvedValue({ items: [{ userId: 'u-current', displayName: 'Current Reviewer' }, { userId: 'u2', displayName: 'New Reviewer' }], pageNumber: 1, pageSize: 20, totalCount: 2, totalPages: 1 }); vi.mocked(reassignRfi).mockResolvedValue({ rfiId: id, status: 'Open', assignedToUserId: 'u2', dueAtUtc: '2026-10-29T01:00:00Z', completedAt: null, rfiRowVersion: 'r2', processRowVersion: 'p2' })
    renderDetail(); await user.click(await screen.findByRole('button', { name: 'Reassign' })); expect(screen.queryByRole('option', { name: 'Current Reviewer' })).not.toBeInTheDocument(); await user.selectOptions(screen.getByLabelText('Reviewer *'), 'u2'); expect(screen.getByRole('button', { name: 'Xác nhận' })).toBeDisabled(); await user.type(screen.getByLabelText('Lý do *'), 'Workload balancing'); await user.click(screen.getByRole('button', { name: 'Xác nhận' })); await waitFor(() => expect(reassignRfi).toHaveBeenCalledWith(id, 'u2', 'Workload balancing', 'r1', 'p1'))
  })

  it.each(['Closed', 'Cancelled'] as const)('keeps terminal %s details read-only for Administrator', async status => {
    setSession('ADMINISTRATOR'); vi.mocked(getRfi).mockResolvedValue({ ...detail, status }); renderDetail(); expect(await screen.findByText(status)).toBeInTheDocument(); expect(screen.getByLabelText(/^Tiêu đề/)).toHaveAttribute('readonly'); expect(screen.queryByRole('button', { name: /Submit|Respond|Reassign|Close|Cancel/ })).not.toBeInTheDocument(); expect(getRfiResponderOptions).not.toHaveBeenCalled()
  })

  it('closes an action modal with Escape and restores focus to its trigger', async () => {
    const user = userEvent.setup(); setSession('REVIEWER'); vi.mocked(getRfi).mockResolvedValue({ ...detail, status: 'Open', assignedToUserId: 'u1' }); renderDetail(); const trigger = await screen.findByRole('button', { name: 'Respond' }); trigger.focus(); await user.click(trigger); expect(screen.getByRole('dialog')).toBeInTheDocument(); expect(screen.getByLabelText('Nội dung phản hồi *')).toHaveFocus(); await user.keyboard('{Escape}'); expect(screen.queryByRole('dialog')).not.toBeInTheDocument(); expect(trigger).toHaveFocus()
  })

  it('renders a scoped not-found state without old detail content', async () => {
    setSession('REVIEWER'); vi.mocked(getRfi).mockRejectedValue(new ApiError(404, { status: 404 })); renderDetail(); expect(await screen.findByRole('alert')).toHaveTextContent(/Không tìm thấy RFI/); expect(screen.queryByText('Confirm sprinkler')).not.toBeInTheDocument()
  })

  it('rejects invalid ids without issuing an API request', async () => {
    setSession('ADMINISTRATOR'); renderPage(<Routes><Route path="/rfis/:id" element={<RfiDetailPage />} /></Routes>, ['/rfis/not-a-guid'])
    expect(await screen.findByRole('alert')).toHaveTextContent('không hợp lệ'); expect(getRfi).not.toHaveBeenCalled()
  })
})

function ProtectedForTest() { const { token } = useAuth(); return token ? <Outlet /> : <Navigate to="/login" replace /> }
function setSession(role: string) { sessionStorage.setItem('construction-inspection.auth', JSON.stringify({ token: 'opaque-test-token', user: { id: 'u1', email: 'user@local.test', fullName: 'User', roles: [role] } })) }
function renderDetail() { return renderPage(<Routes><Route path="/rfis/:id" element={<RfiDetailPage />} /></Routes>, [`/rfis/${id}`]) }
function renderPage(node: React.ReactNode, initialEntries = ['/rfis']) { const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } }); return render(<MemoryRouter initialEntries={initialEntries}><QueryClientProvider client={client}><AuthProvider>{node}</AuthProvider></QueryClientProvider></MemoryRouter>) }
