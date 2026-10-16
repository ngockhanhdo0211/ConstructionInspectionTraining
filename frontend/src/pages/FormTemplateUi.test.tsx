import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import '@testing-library/jest-dom/vitest'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { createFormTemplate, getFormTemplates } from '../api/client'
import { AuthProvider } from '../auth/AuthContext'
import { FormTemplateAdminRoute } from '../components/FormTemplateAdminRoute'
import { FormSchemaEditor } from '../components/FormSchemaEditor'
import { AppShell } from '../components/AppShell'
import type { FormSchema } from '../formTemplates/schema'
import { NewFormTemplatePage } from './NewFormTemplatePage'
import { FormTemplatesPage } from './FormTemplatesPage'

vi.mock('../api/client', async importOriginal => {
  const actual = await importOriginal<typeof import('../api/client')>()
  return { ...actual, createFormTemplate: vi.fn(), getFormTemplates: vi.fn() }
})

afterEach(() => { cleanup(); sessionStorage.clear(); vi.resetAllMocks() })

describe('Form Template UI', () => {
  it.each(['MANAGER', 'INSPECTOR', 'REVIEWER'])('hides a direct Form Template route from %s without calling its API', async role => {
    setSession([role])
    renderWithProviders(<Routes><Route path="/drawings" element={<h1>Drawings</h1>} /><Route element={<FormTemplateAdminRoute />}><Route path="/form-templates" element={<FormTemplatesPage />} /></Route></Routes>, ['/form-templates'])
    expect(await screen.findByRole('heading', { name: 'Drawings' })).toBeInTheDocument()
    expect(getFormTemplates).not.toHaveBeenCalled()
  })

  it('shows administration navigation only to an Administrator', () => {
    setSession(['ADMINISTRATOR'])
    const { unmount } = renderWithProviders(<Routes><Route element={<AppShell />}><Route path="/" element={<p>Home</p>} /></Route></Routes>)
    expect(screen.getByRole('link', { name: 'Form Templates' })).toBeInTheDocument(); unmount(); sessionStorage.clear(); setSession(['MANAGER'])
    renderWithProviders(<Routes><Route element={<AppShell />}><Route path="/" element={<p>Home</p>} /></Route></Routes>)
    expect(screen.queryByRole('link', { name: 'Form Templates' })).not.toBeInTheDocument()
  })

  it('loads the administrator list and renders lifecycle metadata', async () => {
    setSession(['ADMINISTRATOR'])
    vi.mocked(getFormTemplates).mockResolvedValue({ items: [{ id: 't1', code: 'SAFE', name: 'Safety', description: 'Daily check', status: 'Active', latestPublishedVersionNumber: 2, draftVersionNumber: 3, rowVersion: 'rv' }], pageNumber: 1, pageSize: 100, totalCount: 1, totalPages: 1 })
    renderWithProviders(<FormTemplatesPage />)
    expect(await screen.findByText('Safety')).toBeInTheDocument()
    expect(screen.getByText('Published: v2')).toBeInTheDocument()
    expect(screen.getByText('Draft: v3')).toBeInTheDocument()
    expect(getFormTemplates).toHaveBeenCalledOnce()
  })

  it('cancels the list request on route unmount', async () => {
    setSession(['ADMINISTRATOR']); let capturedSignal: AbortSignal | undefined
    vi.mocked(getFormTemplates).mockImplementation(signal => { capturedSignal = signal; return new Promise(() => undefined) })
    const view = renderWithProviders(<FormTemplatesPage />)
    await waitFor(() => expect(capturedSignal).toBeDefined()); expect(capturedSignal?.aborted).toBe(false)
    view.unmount(); expect(capturedSignal?.aborted).toBe(true)
  })

  it('renders stable loading, empty and error states', async () => {
    setSession(['ADMINISTRATOR']); vi.mocked(getFormTemplates).mockImplementation(() => new Promise(() => undefined))
    const loading = renderWithProviders(<FormTemplatesPage />); expect(screen.getByRole('status')).toHaveTextContent('Đang tải'); loading.unmount()
    vi.mocked(getFormTemplates).mockResolvedValue({ items: [], pageNumber: 1, pageSize: 100, totalCount: 0, totalPages: 0 })
    const empty = renderWithProviders(<FormTemplatesPage />); expect(await screen.findByText('Chưa có Form Template')).toBeInTheDocument(); empty.unmount()
    vi.mocked(getFormTemplates).mockRejectedValue(new Error('Network unavailable'))
    renderWithProviders(<FormTemplatesPage />); expect(await screen.findByRole('alert')).toHaveTextContent('Network unavailable')
  })

  it('builds a component through the structured editor and shows safe JSON preview', async () => {
    const user = userEvent.setup(); let schema: FormSchema = { components: [] }
    const View = () => <FormSchemaEditor value={schema} onChange={next => { schema = next; rerender(<View />) }} />
    const { rerender } = render(<View />)
    await user.selectOptions(screen.getByLabelText('Loại component mới'), 'textfield')
    await user.clear(screen.getByLabelText('Label')); await user.type(screen.getByLabelText('Label'), 'Inspector note')
    expect(schema.components).toHaveLength(1)
    expect(schema.components[0]).toMatchObject({ type: 'textfield', label: 'Inspector note' })
    expect(screen.getByText(/"type": "textfield"/)).toBeInTheDocument()
  })

  it('disables additions at 200 components and warns about an oversized UTF-8 payload', () => {
    const components = Array.from({ length: 200 }, (_, index) => ({ type: 'checkbox' as const, key: `field${index + 1}`, label: index === 0 ? 'ữ'.repeat(600_000) : `Field ${index + 1}`, input: true }))
    render(<FormSchemaEditor value={{ components }} onChange={vi.fn()} />)
    expect(screen.getByLabelText('Loại component mới')).toBeDisabled()
    expect(screen.getByRole('alert')).toHaveTextContent('giới hạn 200 components')
    expect(screen.getByRole('alert')).toHaveTextContent('giới hạn UTF-8')
    expect(document.querySelector('script')).not.toBeInTheDocument()
  })

  it('creates Draft v1 using the exact structured schema payload', async () => {
    const user = userEvent.setup()
    vi.mocked(createFormTemplate).mockResolvedValue({ template: { id: 't1', code: 'SAFE', name: 'Safety', status: 'Active', nextVersionNumber: 2, rowVersion: 'trv' }, version: { id: 'v1', template: { id: 't1', code: 'SAFE', name: 'Safety', status: 'Active', nextVersionNumber: 2, rowVersion: 'trv' }, versionNumber: 1, status: 'Draft', schemaJson: '{"components":[]}', schemaUtf8ByteCount: 17, createdAt: '2026-01-01T00:00:00Z', createdByUserId: 'u1', rowVersion: 'vrv' } })
    renderWithProviders(<Routes><Route path="/form-templates/new" element={<NewFormTemplatePage />} /><Route path="/form-templates/:id" element={<h1>Created</h1>} /></Routes>, ['/form-templates/new'])
    await user.type(screen.getByLabelText('Mã template'), 'SAFE'); await user.type(screen.getByLabelText('Tên template'), 'Safety')
    await user.selectOptions(screen.getByLabelText('Loại component mới'), 'checkbox')
    await user.click(screen.getByRole('button', { name: 'Tạo Draft v1' }))
    await waitFor(() => expect(createFormTemplate).toHaveBeenCalledOnce())
    expect(vi.mocked(createFormTemplate).mock.calls[0][0]).toMatchObject({ code: 'SAFE', name: 'Safety', schemaJson: expect.stringContaining('"type":"checkbox"') })
    expect(await screen.findByRole('heading', { name: 'Created' })).toBeInTheDocument()
  })

  it('blocks invalid create and disables double submit while pending', async () => {
    const user = userEvent.setup(); let resolveCreate!: (value: Awaited<ReturnType<typeof createFormTemplate>>) => void
    vi.mocked(createFormTemplate).mockImplementation(() => new Promise(resolve => { resolveCreate = resolve }))
    renderWithProviders(<Routes><Route path="/form-templates/new" element={<NewFormTemplatePage />} /></Routes>, ['/form-templates/new'])
    await user.click(screen.getByRole('button', { name: 'Tạo Draft v1' })); expect(screen.getByRole('alert')).toHaveTextContent('nhập mã, tên'); expect(createFormTemplate).not.toHaveBeenCalled()
    await user.type(screen.getByLabelText(/^Mã template/), 'SAFE'); await user.type(screen.getByLabelText(/^Tên template/), 'Safety'); await user.click(screen.getByRole('button', { name: 'Tạo Draft v1' }))
    expect(createFormTemplate).toHaveBeenCalledOnce(); expect(screen.getByRole('button', { name: 'Đang tạo…' })).toBeDisabled()
    resolveCreate({ template: { id: 't1', code: 'SAFE', name: 'Safety', status: 'Active', nextVersionNumber: 2, rowVersion: 'trv' }, version: { id: 'v1', template: { id: 't1', code: 'SAFE', name: 'Safety', status: 'Active', nextVersionNumber: 2, rowVersion: 'trv' }, versionNumber: 1, status: 'Draft', schemaJson: '{"components":[]}', schemaUtf8ByteCount: 17, createdAt: '2026-01-01T00:00:00Z', createdByUserId: 'u1', rowVersion: 'vrv' } })
  })
})

function setSession(roles: string[]) {
  sessionStorage.setItem('construction-inspection.auth', JSON.stringify({ token: 'opaque-test-token', user: { id: 'u1', email: 'user@local.test', fullName: 'User', roles } }))
}
function renderWithProviders(node: React.ReactNode, initialEntries = ['/']) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  return render(<MemoryRouter initialEntries={initialEntries}><QueryClientProvider client={client}><AuthProvider>{node}</AuthProvider></QueryClientProvider></MemoryRouter>)
}
