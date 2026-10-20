import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import '@testing-library/jest-dom/vitest'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, Navigate, Outlet, Route, Routes, useParams } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError, createConstructionObject, deactivateConstructionObject, getConstructionObject, getConstructionObjectPage, updateConstructionObject } from '../api/client'
import type { ConstructionObjectDetail, PagedResult, ConstructionObjectListItem } from '../api/types'
import { AuthProvider, useAuth } from '../auth/AuthContext'
import { AppShell } from '../components/AppShell'
import { ConstructionObjectManagementRoute } from '../components/ConstructionObjectManagementRoute'
import { ConstructionObjectDetailPage } from './ConstructionObjectDetailPage'
import { ConstructionObjectsPage } from './ConstructionObjectsPage'
import { NewConstructionObjectPage } from './NewConstructionObjectPage'

vi.mock('../api/client', async importOriginal => {
  const actual = await importOriginal<typeof import('../api/client')>()
  return { ...actual, getConstructionObjectPage: vi.fn(), getConstructionObject: vi.fn(), createConstructionObject: vi.fn(), updateConstructionObject: vi.fn(), deactivateConstructionObject: vi.fn() }
})

const item: ConstructionObjectListItem = { id: 'o1', parentId: null, code: 'DEMO-CO-01', name: 'Demo Project', objectType: 'Project', location: '<script>alert(1)</script>', isActive: true, rowVersion: 'rv1' }
const emptyPage: PagedResult<ConstructionObjectListItem> = { items: [], pageNumber: 1, pageSize: 20, totalCount: 0, totalPages: 0 }
const detail: ConstructionObjectDetail = {
  ...item, description: '<img src=x onerror=alert(1)>', parent: null,
  breadcrumb: [{ id: 'o1', code: 'DEMO-CO-01', name: 'Demo Project', objectType: 'Project' }],
  hasChildren: false, directChildrenCount: 0, createdByUserId: 'admin-id', createdAt: '2026-10-20T01:00:00Z', updatedByUserId: null, updatedAt: null,
}

afterEach(() => { cleanup(); sessionStorage.clear(); vi.resetAllMocks(); vi.restoreAllMocks() })
beforeEach(() => { vi.mocked(getConstructionObjectPage).mockResolvedValue(emptyPage); vi.mocked(getConstructionObject).mockResolvedValue(detail) })

describe('Construction Object list and access', () => {
  it('shows management navigation to Administrator/Manager but not Inspector', async () => {
    setSession('ADMINISTRATOR'); const admin = renderPage(<Routes><Route element={<AppShell />}><Route path="/construction-objects" element={<ConstructionObjectsPage />} /></Route></Routes>)
    expect(screen.getByRole('link', { name: 'Construction Objects' })).toBeInTheDocument(); expect(await screen.findByRole('link', { name: 'Tạo Construction Object' })).toBeInTheDocument()
    admin.unmount(); sessionStorage.clear(); setSession('MANAGER'); renderPage(<Routes><Route element={<AppShell />}><Route path="/construction-objects" element={<ConstructionObjectsPage />} /></Route></Routes>)
    expect(screen.getByRole('link', { name: 'Construction Objects' })).toBeInTheDocument(); expect(screen.queryByRole('link', { name: 'Tạo Construction Object' })).not.toBeInTheDocument()
    cleanup(); sessionStorage.clear(); setSession('INSPECTOR'); renderPage(<Routes><Route element={<AppShell />}><Route path="/construction-objects" element={<ConstructionObjectsPage />} /></Route></Routes>)
    expect(screen.queryByRole('link', { name: 'Construction Objects' })).not.toBeInTheDocument()
  })

  it('redirects anonymous and non-management direct routes without loading data', async () => {
    renderPage(<Routes><Route path="/login" element={<h1>Login</h1>} /><Route element={<ProtectedForTest />}><Route element={<ConstructionObjectManagementRoute />}><Route path="/construction-objects" element={<ConstructionObjectsPage />} /></Route></Route></Routes>)
    expect(await screen.findByRole('heading', { name: 'Login' })).toBeInTheDocument(); expect(getConstructionObjectPage).not.toHaveBeenCalled()
    cleanup(); setSession('REVIEWER'); renderPage(<Routes><Route path="/inspections" element={<h1>Inspections</h1>} /><Route element={<ConstructionObjectManagementRoute />}><Route path="/construction-objects" element={<ConstructionObjectsPage />} /></Route></Routes>)
    expect(await screen.findByRole('heading', { name: 'Inspections' })).toBeInTheDocument(); expect(getConstructionObjectPage).not.toHaveBeenCalled()
  })

  it('renders loading, empty, error with retry, and escaped list text', async () => {
    setSession('MANAGER'); vi.mocked(getConstructionObjectPage).mockImplementation(() => new Promise(() => undefined))
    const loading = renderPage(<ConstructionObjectsPage />); expect(screen.getByRole('status')).toHaveTextContent('Đang tải'); loading.unmount()
    vi.mocked(getConstructionObjectPage).mockResolvedValue(emptyPage); const empty = renderPage(<ConstructionObjectsPage />); expect(await screen.findByText('Không có Construction Object phù hợp')).toBeInTheDocument(); empty.unmount()
    vi.mocked(getConstructionObjectPage).mockRejectedValue(new Error('Network unavailable')); const error = renderPage(<ConstructionObjectsPage />); expect(await screen.findByRole('alert')).toHaveTextContent('Network unavailable'); expect(screen.getByRole('button', { name: 'Thử lại' })).toBeInTheDocument(); error.unmount()
    vi.mocked(getConstructionObjectPage).mockResolvedValue({ ...emptyPage, items: [item], totalCount: 1, totalPages: 1 }); renderPage(<ConstructionObjectsPage />)
    expect(await screen.findByText('<script>alert(1)</script>')).toBeInTheDocument(); expect(document.querySelector('script')).toBeNull(); expect(screen.getByRole('listitem')).toHaveTextContent('Active')
  })

  it('forwards search, status, type and pagination and cancels the old GET', async () => {
    const user = userEvent.setup(); setSession('MANAGER'); let signal: AbortSignal | undefined
    vi.mocked(getConstructionObjectPage).mockImplementation((_parameters, nextSignal) => { signal = nextSignal; return Promise.resolve({ ...emptyPage, totalCount: 21, totalPages: 2 }) })
    const view = renderPage(<ConstructionObjectsPage />); await screen.findByText('Không có Construction Object phù hợp')
    await user.type(screen.getByLabelText('Tìm theo code, name hoặc location'), 'DEMO'); await user.selectOptions(screen.getByLabelText('Trạng thái'), 'false'); await user.selectOptions(screen.getByLabelText('Object Type'), 'Room'); await user.click(screen.getByRole('button', { name: 'Tìm' }))
    await waitFor(() => expect(getConstructionObjectPage).toHaveBeenLastCalledWith(expect.objectContaining({ pageNumber: 1, search: 'DEMO', isActive: 'false', objectType: 'Room' }), expect.any(AbortSignal)))
    await user.click(screen.getByRole('button', { name: 'Trang sau' })); await waitFor(() => expect(getConstructionObjectPage).toHaveBeenLastCalledWith(expect.objectContaining({ pageNumber: 2 }), expect.any(AbortSignal)))
    vi.mocked(getConstructionObjectPage).mockImplementation((_parameters, nextSignal) => { signal = nextSignal; return new Promise(() => undefined) }); await user.selectOptions(screen.getByLabelText('Object Type'), 'Floor'); await waitFor(() => expect(signal).toBeDefined()); view.unmount(); expect(signal?.aborted).toBe(true)
  })
})

describe('Construction Object create', () => {
  it('validates required fields, prevents duplicate submit and navigates after success', async () => {
    const user = userEvent.setup(); setSession('ADMINISTRATOR'); let resolve!: (value: ConstructionObjectDetail) => void
    vi.mocked(createConstructionObject).mockImplementation(() => new Promise(next => { resolve = next }))
    renderManagement(['/construction-objects/new']); await user.click(screen.getByRole('button', { name: 'Tạo Construction Object' })); expect(await screen.findByRole('alert')).toHaveTextContent('kiểm tra')
    await user.type(screen.getByLabelText(/^Code/), ' demo-co-02 '); await user.type(screen.getByLabelText(/^Name/), ' Demo 2 '); await user.click(screen.getByRole('button', { name: 'Tạo Construction Object' })); await user.click(screen.getByRole('button', { name: 'Đang tạo…' }))
    expect(createConstructionObject).toHaveBeenCalledOnce(); expect(createConstructionObject).toHaveBeenCalledWith(expect.objectContaining({ code: 'demo-co-02', name: 'Demo 2', parentId: null, objectType: 'Project' }))
    resolve({ ...detail, id: 'o2', code: 'DEMO-CO-02', name: 'Demo 2' }); expect(await screen.findByTestId('detail-route')).toHaveTextContent('o2')
  })

  it('keeps input and explains backend duplicate/parent conflicts', async () => {
    const user = userEvent.setup(); setSession('ADMINISTRATOR'); vi.mocked(createConstructionObject).mockRejectedValue(new ApiError(409, { code: 'construction_object.code_duplicate' }))
    renderManagement(['/construction-objects/new']); await user.type(screen.getByLabelText('Code *'), 'DEMO-CO-03'); await user.type(screen.getByLabelText('Name *'), 'Keep me'); await user.click(screen.getByRole('button', { name: 'Tạo Construction Object' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Code đã được sử dụng'); expect(screen.getByLabelText('Name *')).toHaveValue('Keep me')
    vi.mocked(getConstructionObjectPage).mockResolvedValue({ ...emptyPage, items: [item], totalCount: 1, totalPages: 1 }); vi.mocked(createConstructionObject).mockRejectedValue(new ApiError(409, { code: 'construction_object.parent_inactive' })); await user.selectOptions(screen.getByLabelText('Object Type *'), 'Building'); await screen.findByRole('option', { name: /DEMO-CO-01/ }); await user.selectOptions(screen.getByRole('combobox', { name: 'Parent' }), 'o1'); await user.click(screen.getByRole('button', { name: 'Tạo Construction Object' })); expect(await screen.findByRole('alert')).toHaveTextContent('Parent đã ngừng hoạt động'); expect(createConstructionObject).toHaveBeenLastCalledWith(expect.objectContaining({ objectType: 'Building', parentId: 'o1' }))
  })
})

describe('Construction Object detail, update and deactivate', () => {
  it('loads detail, updates with rowVersion, locks Object Type and stores fresh response', async () => {
    const user = userEvent.setup(); setSession('ADMINISTRATOR'); vi.mocked(updateConstructionObject).mockResolvedValue({ ...detail, name: 'Updated', rowVersion: 'rv2', updatedAt: '2026-10-20T02:00:00Z' })
    renderPage(<Routes><Route path="/construction-objects/:id" element={<ConstructionObjectDetailPage />} /></Routes>, ['/construction-objects/o1'])
    expect(await screen.findByText('<img src=x onerror=alert(1)>')).toBeInTheDocument(); expect(document.querySelector('img')).toBeNull(); await user.click(screen.getByRole('button', { name: 'Chỉnh sửa' }))
    expect(screen.getByLabelText('Object Type *')).toBeDisabled(); const name = screen.getByLabelText('Name *'); await user.clear(name); await user.type(name, 'Updated'); await user.click(screen.getByRole('button', { name: 'Lưu thay đổi' }))
    expect(updateConstructionObject).toHaveBeenCalledWith('o1', expect.objectContaining({ name: 'Updated', rowVersion: 'rv1', objectType: 'Project' })); expect(await screen.findByText('Đã cập nhật Construction Object.')).toBeInTheDocument(); expect(screen.getByRole('heading', { name: /Updated/ })).toBeInTheDocument()
  })

  it('keeps edits on stale 409 and reloads only after confirmation', async () => {
    const user = userEvent.setup(); setSession('ADMINISTRATOR'); vi.mocked(updateConstructionObject).mockRejectedValue(new ApiError(409, { code: 'construction_object.concurrency_conflict' })); vi.spyOn(window, 'confirm').mockReturnValueOnce(false).mockReturnValueOnce(true)
    renderPage(<Routes><Route path="/construction-objects/:id" element={<ConstructionObjectDetailPage />} /></Routes>, ['/construction-objects/o1']); await screen.findByRole('heading', { name: /Demo Project/ }); await user.click(screen.getByRole('button', { name: 'Chỉnh sửa' }))
    const name = screen.getByLabelText('Name *'); await user.clear(name); await user.type(name, 'Local edit'); await user.click(screen.getByRole('button', { name: 'Lưu thay đổi' }))
    expect(await screen.findByRole('alert')).toHaveTextContent(/Nội dung đang nhập vẫn được giữ/i); await user.click(screen.getByRole('button', { name: 'Tải lại phiên bản mới' })); expect(name).toHaveValue('Local edit'); await user.click(screen.getByRole('button', { name: 'Tải lại phiên bản mới' })); await waitFor(() => expect(screen.queryByDisplayValue('Local edit')).not.toBeInTheDocument())
  })

  it('confirms deactivate, hides actions after success, and renders inactive read-only', async () => {
    const user = userEvent.setup(); setSession('ADMINISTRATOR'); vi.spyOn(window, 'confirm').mockReturnValue(true); vi.mocked(deactivateConstructionObject).mockResolvedValue({ id: 'o1', isActive: false, updatedByUserId: 'admin-id', updatedAt: '2026-10-20T03:00:00Z', rowVersion: 'rv2' })
    renderPage(<Routes><Route path="/construction-objects/:id" element={<ConstructionObjectDetailPage />} /></Routes>, ['/construction-objects/o1']); await screen.findByRole('heading', { name: /Demo Project/ }); await user.click(screen.getByRole('button', { name: 'Deactivate' }))
    expect(deactivateConstructionObject).toHaveBeenCalledWith('o1', 'rv1'); expect(await screen.findByText('Construction Object đã chuyển sang Inactive.')).toBeInTheDocument(); expect(screen.queryByRole('button', { name: 'Chỉnh sửa' })).not.toBeInTheDocument(); expect(screen.queryByRole('button', { name: 'Deactivate' })).not.toBeInTheDocument(); expect(screen.getByText(/không thể edit, reactivate/)).toBeInTheDocument()
  })

  it('shows hierarchy and stale deactivate conflicts without retrying', async () => {
    const user = userEvent.setup(); setSession('ADMINISTRATOR'); vi.spyOn(window, 'confirm').mockReturnValue(true); vi.mocked(deactivateConstructionObject).mockRejectedValueOnce(new ApiError(409, { code: 'construction_object.active_descendants_exist' })).mockRejectedValueOnce(new ApiError(409, { code: 'construction_object.concurrency_conflict' }))
    renderPage(<Routes><Route path="/construction-objects/:id" element={<ConstructionObjectDetailPage />} /></Routes>, ['/construction-objects/o1']); await screen.findByRole('heading', { name: /Demo Project/ }); await user.click(screen.getByRole('button', { name: 'Deactivate' })); expect(await screen.findByRole('alert')).toHaveTextContent('descendant đang Active'); expect(deactivateConstructionObject).toHaveBeenCalledOnce()
    await user.click(screen.getByRole('button', { name: 'Deactivate' })); expect(await screen.findByRole('button', { name: 'Tải lại phiên bản mới' })).toBeInTheDocument(); expect(deactivateConstructionObject).toHaveBeenCalledTimes(2)
  })

  it('keeps Manager detail read-only and maps 401/403/404/500 safely', async () => {
    setSession('MANAGER'); renderPage(<Routes><Route path="/construction-objects/:id" element={<ConstructionObjectDetailPage />} /></Routes>, ['/construction-objects/o1']); await screen.findByRole('heading', { name: /Demo Project/ }); expect(screen.queryByRole('button', { name: 'Chỉnh sửa' })).not.toBeInTheDocument()
    for (const [status, message] of [[401, 'hết hạn'], [403, 'không có quyền'], [404, 'Không tìm thấy'], [500, 'Máy chủ gặp lỗi']] as const) {
      cleanup(); vi.mocked(getConstructionObject).mockRejectedValue(new ApiError(status, { status })); renderPage(<Routes><Route path="/construction-objects/:id" element={<ConstructionObjectDetailPage />} /></Routes>, ['/construction-objects/o1']); expect(await screen.findByRole('alert')).toHaveTextContent(message)
    }
  })
})

function ProtectedForTest() { const { token } = useAuth(); return token ? <Outlet /> : <Navigate to="/login" replace /> }
function setSession(role: string) { sessionStorage.setItem('construction-inspection.auth', JSON.stringify({ token: 'opaque-test-token', user: { id: 'u1', email: 'user@local.test', fullName: 'User', roles: [role] } })) }
function renderManagement(initialEntries = ['/construction-objects']) {
  return renderPage(<Routes><Route element={<ConstructionObjectManagementRoute />}><Route path="/construction-objects" element={<ConstructionObjectsPage />} /><Route path="/construction-objects/new" element={<NewConstructionObjectPage />} /><Route path="/construction-objects/:id" element={<DetailMarker />} /></Route></Routes>, initialEntries)
}
function DetailMarker() { return <output data-testid="detail-route">{useParams().id}</output> }
function renderPage(node: React.ReactNode, initialEntries = ['/construction-objects']) { const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } }); return render(<MemoryRouter initialEntries={initialEntries}><QueryClientProvider client={client}><AuthProvider>{node}</AuthProvider></QueryClientProvider></MemoryRouter>) }
