import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import '@testing-library/jest-dom/vitest'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, Navigate, Outlet, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError, createChecklist, getChecklist, getChecklistHistory, getChecklistSubmission, getChecklistSubmissions, getChecklists, getInspectionConstructionObjects, getInspections, rejectChecklist, startChecklist, updateChecklistItems } from '../api/client'
import type { ChecklistDetail } from '../api/types'
import { AuthProvider, useAuth } from '../auth/AuthContext'
import { AppShell } from '../components/AppShell'
import { ChecklistDetailPage } from './ChecklistDetailPage'
import { ChecklistsPage } from './ChecklistsPage'
import { NewChecklistPage } from './NewChecklistPage'

vi.mock('../api/client', async importOriginal => {
  const actual = await importOriginal<typeof import('../api/client')>()
  return { ...actual, getChecklists: vi.fn(), getChecklist: vi.fn(), createChecklist: vi.fn(), startChecklist: vi.fn(), updateChecklistItems: vi.fn(), replaceChecklistInspections: vi.fn(), submitChecklist: vi.fn(), startChecklistReview: vi.fn(), approveChecklist: vi.fn(), rejectChecklist: vi.fn(), requestChecklistRecheck: vi.fn(), getChecklistHistory: vi.fn(), getChecklistSubmissions: vi.fn(), getChecklistSubmission: vi.fn(), getInspectionConstructionObjects: vi.fn(), getInspectionReviewerOptions: vi.fn(), getInspections: vi.fn() }
})

const emptyPage = { items: [], pageNumber: 1, pageSize: 20, totalCount: 0, totalPages: 0 }
const detail: ChecklistDetail = {
  id: 'c1', number: 'CHK-2026-000001', constructionObjectId: 'o1', constructionObjectCode: 'ROOM-01', constructionObjectName: 'Room 01', title: 'Door safety', description: 'Daily check', status: 'Draft', createdByUserId: 'u1', reviewerUserId: null, currentSubmissionId: null, nextSubmissionNumber: 1, createdAt: '2026-10-21T01:00:00Z', updatedAt: null,
  items: [{ id: 'item-1', sequence: 1, criterion: 'Door closes', isRequired: true, result: 'NotChecked', note: null, rowVersion: 'item-rv' }], inspections: [], checklistRowVersion: 'checklist-rv', processRowVersion: 'process-rv',
}

beforeEach(() => {
  vi.mocked(getChecklistHistory).mockResolvedValue(emptyPage)
  vi.mocked(getChecklistSubmissions).mockResolvedValue(emptyPage)
  vi.mocked(getInspections).mockResolvedValue(emptyPage)
})
afterEach(() => { cleanup(); sessionStorage.clear(); vi.resetAllMocks(); vi.restoreAllMocks() })

describe('Checklist UI', () => {
  it.each(['INSPECTOR', 'REVIEWER', 'MANAGER', 'ADMINISTRATOR'])('shows Checklist navigation to %s', role => {
    setSession(role); renderPage(<Routes><Route element={<AppShell />}><Route path="/" element={<p>Home</p>} /></Route></Routes>, ['/'])
    expect(screen.getByRole('link', { name: 'Checklists' })).toBeInTheDocument()
  })

  it('protects anonymous list through the existing authenticated shell', async () => {
    renderPage(<Routes><Route path="/login" element={<h1>Login</h1>} /><Route element={<ProtectedForTest />}><Route path="/checklists" element={<ChecklistsPage />} /></Route></Routes>, ['/checklists'])
    expect(await screen.findByRole('heading', { name: 'Login' })).toBeInTheDocument(); expect(getChecklists).not.toHaveBeenCalled()
  })

  it('renders scoped list metadata, stores filters in URL and cancels an old request', async () => {
    const user = userEvent.setup(); setSession('INSPECTOR')
    vi.mocked(getChecklists).mockResolvedValue({ items: [{ id: 'c1', number: 'CHK-2026-000001', title: 'Door safety', constructionObjectId: 'o1', constructionObjectCode: 'ROOM-01', constructionObjectName: 'Room 01', status: 'InProgress', createdByUserId: 'u1', reviewerUserId: null, itemCount: 4, passCount: 2, failCount: 1, notApplicableCount: 0, notCheckedCount: 1, createdAt: '2026-10-21T01:00:00Z', updatedAt: null }], pageNumber: 1, pageSize: 20, totalCount: 1, totalPages: 1 })
    renderPage(<ChecklistsPage />); expect(await screen.findByText('CHK-2026-000001')).toBeInTheDocument(); expect(screen.getByText(/2 Pass · 1 Fail/)).toBeInTheDocument()
    await user.type(screen.getByLabelText('Tìm theo số hoặc tiêu đề'), 'door'); await user.selectOptions(screen.getByLabelText('Trạng thái'), 'InProgress'); await user.click(screen.getByRole('button', { name: 'Tìm' }))
    await waitFor(() => expect(getChecklists).toHaveBeenLastCalledWith(expect.objectContaining({ search: 'door', status: 'InProgress' }), expect.any(AbortSignal)))
    cleanup(); let signal: AbortSignal | undefined; vi.mocked(getChecklists).mockImplementation((_input, nextSignal) => { signal = nextSignal; return new Promise(() => undefined) }); const view = renderPage(<ChecklistsPage />); await waitFor(() => expect(signal).toBeDefined()); view.unmount(); expect(signal?.aborted).toBe(true)
  })

  it('creates ordered items once, validates duplicate criteria and hides create from non-Inspector', async () => {
    const user = userEvent.setup(); setSession('INSPECTOR')
    vi.mocked(getInspectionConstructionObjects).mockResolvedValue({ items: [{ id: 'o1', parentId: null, code: 'ROOM-01', name: 'Room 01', objectType: 'Room', isActive: true, rowVersion: 'rv' }], pageNumber: 1, pageSize: 20, totalCount: 1, totalPages: 1 })
    vi.mocked(createChecklist).mockResolvedValue({ checklistId: 'c1', status: 'Draft', reviewerUserId: null, currentSubmissionId: null, submissionNumber: null, completedAt: null, checklistRowVersion: 'c-rv', processRowVersion: 'p-rv' })
    renderPage(<Routes><Route path="/checklists/new" element={<NewChecklistPage />} /><Route path="/checklists/:id" element={<h1>Created Checklist</h1>} /></Routes>, ['/checklists/new'])
    await screen.findByRole('option', { name: /ROOM-01/ }); await user.selectOptions(screen.getByLabelText('Construction Object *'), 'o1'); await user.type(screen.getByLabelText('Tiêu đề *'), '  Door safety  '); const criterion = screen.getByLabelText('Tiêu chí *'); await user.type(criterion, ' Door closes '); await user.click(screen.getByRole('button', { name: 'Thêm tiêu chí' })); await user.type(screen.getAllByLabelText('Tiêu chí *')[1], 'door closes'); await user.click(screen.getByRole('button', { name: 'Tạo Draft Checklist' })); expect(await screen.findByRole('alert')).toHaveTextContent('không được trùng'); expect(createChecklist).not.toHaveBeenCalled()
    await user.clear(screen.getAllByLabelText('Tiêu chí *')[1]); await user.type(screen.getAllByLabelText('Tiêu chí *')[1], 'Fire seal'); await user.click(screen.getByRole('button', { name: 'Tạo Draft Checklist' })); expect(createChecklist).toHaveBeenCalledOnce(); expect(createChecklist).toHaveBeenCalledWith({ constructionObjectId: 'o1', title: 'Door safety', description: null, items: [{ criterion: 'Door closes', isRequired: true }, { criterion: 'Fire seal', isRequired: true }] }); expect(await screen.findByRole('heading', { name: 'Created Checklist' })).toBeInTheDocument()
    cleanup(); sessionStorage.clear(); setSession('MANAGER'); renderPage(<Routes><Route path="/checklists" element={<h1>List</h1>} /><Route path="/checklists/new" element={<NewChecklistPage />} /></Routes>, ['/checklists/new']); expect(await screen.findByRole('heading', { name: 'List' })).toBeInTheDocument(); expect(getInspectionConstructionObjects).toHaveBeenCalledOnce()
  })

  it('starts and saves owner items with all rowVersions, result and note', async () => {
    const user = userEvent.setup(); setSession('INSPECTOR'); vi.mocked(getChecklist).mockResolvedValue({ ...detail, status: 'InProgress' }); vi.mocked(startChecklist).mockResolvedValue({ checklistId: 'c1', status: 'InProgress', reviewerUserId: null, currentSubmissionId: null, submissionNumber: null, completedAt: null, checklistRowVersion: 'c2', processRowVersion: 'p2' }); vi.mocked(updateChecklistItems).mockResolvedValue({ items: detail.items, checklistRowVersion: 'c2', processRowVersion: 'p2' })
    renderDetail(); const result = await screen.findByLabelText('Kết quả'); await user.selectOptions(result, 'Fail'); await user.type(screen.getByLabelText(/Ghi chú/), 'Door is blocked'); await user.click(screen.getByRole('button', { name: 'Lưu mục Checklist' }))
    expect(updateChecklistItems).toHaveBeenCalledWith('c1', { checklistRowVersion: 'checklist-rv', processRowVersion: 'process-rv', items: [{ id: 'item-1', rowVersion: 'item-rv', criterion: 'Door closes', isRequired: true, result: 'Fail', note: 'Door is blocked' }] })
  })

  it('keeps item input on 409, does not retry and asks before reload', async () => {
    const user = userEvent.setup(); setSession('INSPECTOR'); vi.mocked(getChecklist).mockResolvedValue({ ...detail, status: 'InProgress' }); vi.mocked(updateChecklistItems).mockRejectedValue(new ApiError(409, { status: 409, code: 'checklist.concurrency_conflict' })); vi.spyOn(window, 'confirm').mockReturnValue(false)
    renderDetail(); const criterion = await screen.findByLabelText('Tiêu chí'); await user.clear(criterion); await user.type(criterion, 'Local criterion'); await user.click(screen.getByRole('button', { name: 'Lưu mục Checklist' })); expect(await screen.findByRole('alert')).toHaveTextContent(/thay đổi|changed/i); expect(criterion).toHaveValue('Local criterion'); expect(updateChecklistItems).toHaveBeenCalledOnce(); await user.click(screen.getByRole('button', { name: 'Tải lại dữ liệu mới nhất' })); expect(window.confirm).toHaveBeenCalled(); expect(criterion).toHaveValue('Local criterion')
  })

  it('locks item structure after first submit and keeps Manager read-only', async () => {
    setSession('MANAGER'); vi.mocked(getChecklist).mockResolvedValue({ ...detail, status: 'InProgress', currentSubmissionId: 'submission-1' }); renderDetail(); expect(await screen.findByText(/Cấu trúc tiêu chí đã khóa/)).toBeInTheDocument(); expect(screen.getByLabelText('Tiêu chí')).toHaveAttribute('readonly'); expect(screen.getByLabelText('Kết quả')).toBeDisabled(); expect(screen.queryByRole('button', { name: 'Thêm tiêu chí' })).not.toBeInTheDocument(); expect(screen.queryByRole('button', { name: 'Lưu mục Checklist' })).not.toBeInTheDocument()
  })

  it('shows only assigned Reviewer workflow and requires a reject reason', async () => {
    const user = userEvent.setup(); setSession('REVIEWER'); vi.mocked(getChecklist).mockResolvedValue({ ...detail, status: 'UnderReview', reviewerUserId: 'u1', currentSubmissionId: 's1' }); renderDetail(); expect(await screen.findByRole('button', { name: 'Approve' })).toBeInTheDocument(); await user.click(screen.getByRole('button', { name: 'Reject' })); expect(screen.getByRole('button', { name: 'Xác nhận' })).toBeDisabled(); await user.type(screen.getByLabelText('Lý do *'), 'Evidence incomplete'); expect(screen.getByRole('button', { name: 'Xác nhận' })).toBeEnabled(); expect(rejectChecklist).not.toHaveBeenCalled()
  })

  it('loads immutable snapshot on demand and renders untrusted text as text', async () => {
    const user = userEvent.setup(); setSession('ADMINISTRATOR'); vi.mocked(getChecklist).mockResolvedValue({ ...detail, status: 'Approved', currentSubmissionId: 's1' }); vi.mocked(getChecklistSubmissions).mockResolvedValue({ items: [{ id: 's1', submissionNumber: 1, submittedByUserId: 'u1', submittedByDisplayName: '<img onerror=alert(1)>', submittedAt: '2026-10-21T02:00:00Z', itemCount: 1, inspectionCount: 0, isCurrent: true }], pageNumber: 1, pageSize: 10, totalCount: 1, totalPages: 1 }); vi.mocked(getChecklistSubmission).mockResolvedValue({ id: 's1', submissionNumber: 1, submittedByUserId: 'u1', submittedByDisplayName: '<img onerror=alert(1)>', submittedAt: '2026-10-21T02:00:00Z', isCurrent: true, inspections: [], items: [{ sourceChecklistItemId: 'item-1', sequence: 1, criterion: '<script>alert(1)</script>', isRequired: true, result: 'Pass', note: null }] })
    renderDetail(); expect(getChecklistSubmission).not.toHaveBeenCalled(); await user.click(await screen.findByRole('button', { name: /Submission 1/ })); expect(await screen.findByText('<script>alert(1)</script>')).toBeInTheDocument(); expect(document.querySelector('script')).toBeNull(); expect(getChecklistSubmission).toHaveBeenCalledWith('c1', 1, expect.any(AbortSignal))
  })
})

function ProtectedForTest() { const { token } = useAuth(); return token ? <Outlet /> : <Navigate to="/login" replace /> }
function setSession(role: string) { sessionStorage.setItem('construction-inspection.auth', JSON.stringify({ token: 'opaque-test-token', user: { id: 'u1', email: 'user@local.test', fullName: 'User', roles: [role] } })) }
function renderDetail() { return renderPage(<Routes><Route path="/checklists/:id" element={<ChecklistDetailPage />} /></Routes>, ['/checklists/c1']) }
function renderPage(node: React.ReactNode, initialEntries = ['/checklists']) { const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } }); return render(<MemoryRouter initialEntries={initialEntries}><QueryClientProvider client={client}><AuthProvider>{node}</AuthProvider></QueryClientProvider></MemoryRouter>) }
