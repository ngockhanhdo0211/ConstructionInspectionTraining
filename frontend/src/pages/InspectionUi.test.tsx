import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import '@testing-library/jest-dom/vitest'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, Navigate, Outlet, Route, Routes } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError, createInspection, getInspection, getInspectionConstructionObjects, getInspectionFormTemplateOptions, getInspections, saveInspectionDraft } from '../api/client'
import type { InspectionDetail } from '../api/types'
import { AuthProvider, useAuth } from '../auth/AuthContext'
import { AppShell } from '../components/AppShell'
import { InspectionDetailPage } from './InspectionDetailPage'
import { InspectionsPage } from './InspectionsPage'
import { NewInspectionPage } from './NewInspectionPage'

vi.mock('../api/client', async importOriginal => {
  const actual = await importOriginal<typeof import('../api/client')>()
  return { ...actual, getInspections: vi.fn(), getInspection: vi.fn(), getInspectionConstructionObjects: vi.fn(), getInspectionFormTemplateOptions: vi.fn(), createInspection: vi.fn(), saveInspectionDraft: vi.fn() }
})

const emptyPage = { items: [], pageNumber: 1, pageSize: 20, totalCount: 0, totalPages: 0 }
const schemaJson = JSON.stringify({ components: [
  { type: 'textfield', key: 'summary', label: 'Summary', validate: { required: true } },
  { type: 'number', key: 'score', label: 'Score', validate: { integer: true, min: 0, max: 10 } },
  { type: 'checkbox', key: 'ok', label: 'OK' },
] })
const detail: InspectionDetail = {
  id: 'i1', number: 'INS-2026-000001', constructionObjectId: 'o1', constructionObjectCode: 'ROOM-01', constructionObjectName: 'Room 01', title: 'Safety inspection', description: 'Daily',
  template: { id: 't1', code: 'SAFE', name: 'Safety', versionId: 'v2', versionNumber: 2, schemaJson }, status: 'Draft', createdByUserId: 'u1', reviewerUserId: null,
  createdAt: '2026-10-18T01:00:00Z', updatedAt: null, currentRevision: { id: 's1', revisionNumber: 1, dataJson: '{"summary":"old"}', createdAt: '2026-10-18T01:00:00Z', updatedAt: null, submittedAt: null, rowVersion: 'submission-rv' }, rowVersion: 'inspection-rv', processRowVersion: 'process-rv',
}

afterEach(() => { cleanup(); sessionStorage.clear(); vi.resetAllMocks(); vi.restoreAllMocks() })

describe('Inspection Draft UI', () => {
  it('shows Inspection navigation and Create only to Inspector', async () => {
    vi.mocked(getInspections).mockResolvedValue(emptyPage)
    setSession('INSPECTOR'); const view = renderPage(<Routes><Route element={<AppShell />}><Route path="/inspections" element={<InspectionsPage />} /></Route></Routes>, ['/inspections'])
    expect(screen.getByRole('link', { name: 'Inspections' })).toBeInTheDocument()
    expect(await screen.findByRole('link', { name: 'Tạo Inspection' })).toBeInTheDocument()
    view.unmount(); sessionStorage.clear(); setSession('MANAGER'); vi.mocked(getInspections).mockResolvedValue(emptyPage)
    renderPage(<InspectionsPage />)
    expect(await screen.findByText('Không có Inspection phù hợp')).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Tạo Inspection' })).not.toBeInTheDocument()
  })

  it('protects anonymous routes through the existing shell', async () => {
    renderPage(<Routes><Route path="/login" element={<h1>Login</h1>} /><Route element={<ProtectedForTest />}><Route path="/inspections" element={<InspectionsPage />} /></Route></Routes>, ['/inspections'])
    expect(await screen.findByRole('heading', { name: 'Login' })).toBeInTheDocument()
    expect(getInspections).not.toHaveBeenCalled()
  })

  it('renders loading, empty, error and actual list metadata without DataJson', async () => {
    setSession('INSPECTOR'); vi.mocked(getInspections).mockImplementation(() => new Promise(() => undefined))
    const loading = renderPage(<InspectionsPage />); expect(screen.getByRole('status')).toHaveTextContent('Đang tải'); loading.unmount()
    vi.mocked(getInspections).mockResolvedValue(emptyPage); const empty = renderPage(<InspectionsPage />); expect(await screen.findByText('Không có Inspection phù hợp')).toBeInTheDocument(); empty.unmount()
    vi.mocked(getInspections).mockRejectedValue(new Error('Network unavailable')); const error = renderPage(<InspectionsPage />); expect(await screen.findByRole('alert')).toHaveTextContent('Network unavailable'); error.unmount()
    vi.mocked(getInspections).mockResolvedValue({ items: [{ id: 'i1', number: 'INS-2026-000001', title: 'Safety', constructionObjectId: 'o1', formTemplateVersionId: 'v2', formTemplateVersionNumber: 2, status: 'Draft', createdByUserId: 'u1', reviewerUserId: null, createdAt: '2026-10-18T01:00:00Z', updatedAt: null, rowVersion: 'rv' }], pageNumber: 1, pageSize: 20, totalCount: 1, totalPages: 1 })
    renderPage(<InspectionsPage />); expect(await screen.findByText('INS-2026-000001')).toBeInTheDocument(); expect(screen.getByText('Template v2')).toBeInTheDocument(); expect(screen.queryByText(/dataJson/i)).not.toBeInTheDocument()
  })

  it('cancels list query on unmount and does not loop requests', async () => {
    setSession('INSPECTOR'); let signal: AbortSignal | undefined
    vi.mocked(getInspections).mockImplementation((_parameters, nextSignal) => { signal = nextSignal; return new Promise(() => undefined) })
    const view = renderPage(<InspectionsPage />); await waitFor(() => expect(signal).toBeDefined()); expect(getInspections).toHaveBeenCalledOnce(); view.unmount(); expect(signal?.aborted).toBe(true)
  })

  it('forwards list search, status and pagination without exposing form data', async () => {
    const user = userEvent.setup(); setSession('MANAGER')
    vi.mocked(getInspections).mockResolvedValue({ ...emptyPage, pageNumber: 1, totalCount: 21, totalPages: 2 })
    renderPage(<InspectionsPage />); await screen.findByText('Không có Inspection phù hợp')
    await user.type(screen.getByLabelText('Tìm theo số hoặc tiêu đề'), 'INS-2026'); await user.selectOptions(screen.getByLabelText('Trạng thái'), 'Draft'); await user.click(screen.getByRole('button', { name: 'Tìm' })); await waitFor(() => expect(getInspections).toHaveBeenLastCalledWith(expect.objectContaining({ pageNumber: 1, search: 'INS-2026', status: 'Draft' }), expect.any(AbortSignal)))
    await user.click(screen.getByRole('button', { name: 'Trang sau' })); await waitFor(() => expect(getInspections).toHaveBeenLastCalledWith(expect.objectContaining({ pageNumber: 2 }), expect.any(AbortSignal)))
  })

  it('loads both creation lookups and sends only the allowed create fields once', async () => {
    const user = userEvent.setup(); setSession('INSPECTOR')
    vi.mocked(getInspectionConstructionObjects).mockResolvedValue({ items: [{ id: 'o1', parentId: null, code: 'ROOM-01', name: 'Room', objectType: 'Room', isActive: true, rowVersion: 'rv' }], pageNumber: 1, pageSize: 10, totalCount: 1, totalPages: 1 })
    vi.mocked(getInspectionFormTemplateOptions).mockResolvedValue({ items: [{ formTemplateId: 't1', code: 'SAFE', name: 'Safety', latestPublishedVersionNumber: 2 }], pageNumber: 1, pageSize: 10, totalCount: 1, totalPages: 1 })
    let resolveCreate!: (value: InspectionDetail) => void; vi.mocked(createInspection).mockImplementation(() => new Promise(resolve => { resolveCreate = resolve }))
    renderPage(<Routes><Route path="/inspections/new" element={<NewInspectionPage />} /><Route path="/inspections/:id" element={<h1>Created detail</h1>} /></Routes>, ['/inspections/new'])
    await screen.findByRole('option', { name: /ROOM-01/ }); await screen.findByRole('option', { name: /SAFE/ }); await user.selectOptions(screen.getByLabelText('Construction Object'), 'o1'); await user.selectOptions(screen.getByLabelText('Form Template'), 't1'); await user.type(screen.getByLabelText('Tiêu đề *'), 'Draft title'); await user.click(screen.getByRole('button', { name: 'Tạo Draft Inspection' })); await user.click(screen.getByRole('button', { name: 'Đang tạo…' }))
    expect(createInspection).toHaveBeenCalledOnce(); expect(vi.mocked(createInspection).mock.calls[0][0]).toEqual({ constructionObjectId: 'o1', formTemplateId: 't1', title: 'Draft title', description: null, data: {} })
    expect(getInspectionFormTemplateOptions).toHaveBeenCalledOnce(); resolveCreate(detail); expect(await screen.findByRole('heading', { name: 'Created detail' })).toBeInTheDocument()
  })

  it('redirects non-Inspector create route before lookup calls', async () => {
    setSession('ADMINISTRATOR'); vi.mocked(getInspections).mockResolvedValue(emptyPage)
    renderPage(<Routes><Route path="/inspections" element={<InspectionsPage />} /><Route path="/inspections/new" element={<NewInspectionPage />} /></Routes>, ['/inspections/new'])
    expect(await screen.findByRole('heading', { name: 'Inspections' })).toBeInTheDocument(); expect(getInspectionConstructionObjects).not.toHaveBeenCalled(); expect(getInspectionFormTemplateOptions).not.toHaveBeenCalled()
  })

  it('keeps creation selections after a failed request', async () => {
    const user = userEvent.setup(); setSession('INSPECTOR')
    vi.mocked(getInspectionConstructionObjects).mockResolvedValue({ items: [{ id: 'o1', parentId: null, code: 'ROOM-01', name: 'Room', objectType: 'Room', isActive: true, rowVersion: 'rv' }], pageNumber: 1, pageSize: 10, totalCount: 1, totalPages: 1 })
    vi.mocked(getInspectionFormTemplateOptions).mockResolvedValue({ items: [{ formTemplateId: 't1', code: 'SAFE', name: 'Safety', latestPublishedVersionNumber: 2 }], pageNumber: 1, pageSize: 10, totalCount: 1, totalPages: 1 })
    vi.mocked(createInspection).mockRejectedValue(new ApiError(409, { status: 409, code: 'inspection.form_template_unavailable' }))
    renderPage(<NewInspectionPage />, ['/inspections/new']); await screen.findByRole('option', { name: /ROOM-01/ }); await user.selectOptions(screen.getByLabelText('Construction Object'), 'o1'); await user.selectOptions(screen.getByLabelText('Form Template'), 't1'); await user.type(screen.getByLabelText('Tiêu đề *'), 'Keep me'); await user.click(screen.getByRole('button', { name: 'Tạo Draft Inspection' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('không còn khả dụng'); expect(screen.getByLabelText('Construction Object')).toHaveValue('o1'); expect(screen.getByLabelText('Form Template')).toHaveValue('t1'); expect(screen.getByLabelText('Tiêu đề *')).toHaveValue('Keep me')
  })

  it('saves missing-required Draft with all three rowVersions and refreshes them', async () => {
    const user = userEvent.setup(); setSession('INSPECTOR'); vi.mocked(getInspection).mockResolvedValue(detail)
    vi.mocked(saveInspectionDraft).mockResolvedValue({ id: 'i1', revisionNumber: 1, dataJson: '{}', updatedAt: '2026-10-18T02:00:00Z', inspectionRowVersion: 'inspection-new', submissionRowVersion: 'submission-new', processRowVersion: 'process-new' })
    renderDetail(); const summary = await screen.findByLabelText(/Summary/); await user.clear(summary); await user.click(screen.getByRole('button', { name: 'Lưu Draft' }))
    expect(saveInspectionDraft).toHaveBeenCalledWith('i1', { data: {}, inspectionRowVersion: 'inspection-rv', submissionRowVersion: 'submission-rv', processRowVersion: 'process-rv' })
    expect(await screen.findByText(/Ba rowVersion đã được cập nhật/)).toBeInTheDocument()
    await user.type(screen.getByLabelText(/Summary/), 'second save'); await user.click(screen.getByRole('button', { name: 'Lưu Draft' })); await waitFor(() => expect(saveInspectionDraft).toHaveBeenLastCalledWith('i1', { data: { summary: 'second save' }, inspectionRowVersion: 'inspection-new', submissionRowVersion: 'submission-new', processRowVersion: 'process-new' }))
  })

  it.each(['MANAGER', 'ADMINISTRATOR', 'REVIEWER'])('renders detail read-only without Save for %s', async role => {
    setSession(role); vi.mocked(getInspection).mockResolvedValue(detail); renderDetail()
    expect(await screen.findByText('Hồ sơ đang ở chế độ chỉ đọc.')).toBeInTheDocument(); expect(screen.queryByRole('button', { name: 'Lưu Draft' })).not.toBeInTheDocument(); expect(screen.getByLabelText(/Summary/)).toBeDisabled()
  })

  it('keeps local edits on 409, does not retry and warns before server reload', async () => {
    const user = userEvent.setup(); setSession('INSPECTOR'); vi.mocked(getInspection).mockResolvedValue(detail); vi.mocked(saveInspectionDraft).mockRejectedValue(new ApiError(409, { status: 409, code: 'inspection.concurrency_conflict' })); vi.spyOn(window, 'confirm').mockReturnValue(false)
    renderDetail(); const summary = await screen.findByLabelText(/Summary/); await user.clear(summary); await user.type(summary, 'local edit'); await user.click(screen.getByRole('button', { name: 'Lưu Draft' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Nội dung bạn đang nhập vẫn được giữ'); expect(summary).toHaveValue('local edit'); expect(saveInspectionDraft).toHaveBeenCalledOnce(); await user.click(screen.getByRole('button', { name: 'Tải lại bản máy chủ' })); expect(window.confirm).toHaveBeenCalled(); expect(summary).toHaveValue('local edit')
  })

  it('keeps local input after a failed Save', async () => {
    const user = userEvent.setup(); setSession('INSPECTOR'); vi.mocked(getInspection).mockResolvedValue(detail)
    vi.mocked(saveInspectionDraft).mockRejectedValue(new ApiError(500, { status: 500 }))
    renderDetail(); const summary = await screen.findByLabelText(/Summary/); await user.clear(summary); await user.type(summary, 'keep after failure'); await user.click(screen.getByRole('button', { name: 'Lưu Draft' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Máy chủ gặp lỗi'); expect(summary).toHaveValue('keep after failure'); expect(saveInspectionDraft).toHaveBeenCalledOnce()
  })

  it('blocks an invalid typed value before calling the Save API', async () => {
    const user = userEvent.setup(); setSession('INSPECTOR'); vi.mocked(getInspection).mockResolvedValue({ ...detail, currentRevision: { ...detail.currentRevision, dataJson: '{"summary":"old","score":"not-a-number"}' } })
    renderDetail(); const summary = await screen.findByLabelText(/Summary/); await user.type(summary, ' changed'); await user.click(screen.getByRole('button', { name: 'Lưu Draft' }))
    expect(await screen.findByText('Giá trị phải là số hữu hạn.')).toBeInTheDocument(); expect(saveInspectionDraft).not.toHaveBeenCalled()
  })

  it('reloads a conflict as read-only when the process is no longer Draft', async () => {
    const user = userEvent.setup(); const submitted = { ...detail, status: 'Submitted' as const, rowVersion: 'inspection-submitted', processRowVersion: 'process-submitted' }
    setSession('INSPECTOR'); vi.mocked(getInspection).mockResolvedValueOnce(detail).mockResolvedValueOnce(submitted); vi.mocked(saveInspectionDraft).mockRejectedValue(new ApiError(409, { status: 409, code: 'inspection.invalid_state' })); vi.spyOn(window, 'confirm').mockReturnValue(true)
    renderDetail(); const summary = await screen.findByLabelText(/Summary/); await user.type(summary, ' changed'); await user.click(screen.getByRole('button', { name: 'Lưu Draft' })); await screen.findByRole('alert'); await user.click(screen.getByRole('button', { name: 'Tải lại bản máy chủ' }))
    expect(await screen.findByText('Hồ sơ đang ở chế độ chỉ đọc.')).toBeInTheDocument(); expect(screen.queryByRole('button', { name: 'Lưu Draft' })).not.toBeInTheDocument(); expect(screen.getByLabelText(/Summary/)).toBeDisabled()
  })

  it('registers a browser warning while Draft is dirty', async () => {
    const user = userEvent.setup(); setSession('INSPECTOR'); vi.mocked(getInspection).mockResolvedValue(detail); renderDetail(); const summary = await screen.findByLabelText(/Summary/); await user.type(summary, ' changed')
    const event = new Event('beforeunload', { cancelable: true }); window.dispatchEvent(event); expect(event.defaultPrevented).toBe(true)
  })

  it('fails closed when the pinned schema contains an unsupported component', async () => {
    setSession('INSPECTOR'); vi.mocked(getInspection).mockResolvedValue({ ...detail, template: { ...detail.template, schemaJson: '{"components":[{"type":"html","key":"x","label":"X"}]}' } }); renderDetail()
    expect(await screen.findByRole('alert')).toHaveTextContent('Pinned schema không thể hiển thị an toàn'); expect(screen.queryByRole('button', { name: 'Lưu Draft' })).not.toBeInTheDocument()
  })
})

function ProtectedForTest() { const { token } = useAuth(); return token ? <Outlet /> : <Navigate to="/login" replace /> }
function setSession(role: string) { sessionStorage.setItem('construction-inspection.auth', JSON.stringify({ token: 'opaque-test-token', user: { id: 'u1', email: 'user@local.test', fullName: 'User', roles: [role] } })) }
function renderDetail() { return renderPage(<Routes><Route path="/inspections/:id" element={<InspectionDetailPage />} /></Routes>, ['/inspections/i1']) }
function renderPage(node: React.ReactNode, initialEntries = ['/inspections']) { const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } }); return render(<MemoryRouter initialEntries={initialEntries}><QueryClientProvider client={client}><AuthProvider>{node}</AuthProvider></QueryClientProvider></MemoryRouter>) }
