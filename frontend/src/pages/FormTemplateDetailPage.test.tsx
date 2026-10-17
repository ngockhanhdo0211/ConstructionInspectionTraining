import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import '@testing-library/jest-dom/vitest'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError, createFormTemplateVersion, deactivateFormTemplate, getFormTemplateVersion, getFormTemplateVersions, publishFormTemplateDraft, updateFormTemplateDraft } from '../api/client'
import type { FormTemplateVersionDetail, FormTemplateVersions } from '../api/types'
import { FormTemplateDetailPage } from './FormTemplateDetailPage'

vi.mock('../api/client', async importOriginal => {
  const actual = await importOriginal<typeof import('../api/client')>()
  return { ...actual, getFormTemplateVersions: vi.fn(), getFormTemplateVersion: vi.fn(), updateFormTemplateDraft: vi.fn(), publishFormTemplateDraft: vi.fn(), createFormTemplateVersion: vi.fn(), deactivateFormTemplate: vi.fn() }
})

const template = { id: '11111111-1111-1111-1111-111111111111', code: 'SAFE', name: 'Safety', description: 'Daily check', status: 'Active' as const, nextVersionNumber: 8, rowVersion: 'template-rv' }
const draft: FormTemplateVersionDetail = { template, id: 'v7', versionNumber: 7, status: 'Draft', schemaJson: '{"components":[{"type":"textfield","key":"note","label":"Note","input":true}]}', schemaUtf8ByteCount: 82, createdByUserId: 'u1', createdAt: '2026-01-01T00:00:00Z', rowVersion: 'version-rv' }
const history: FormTemplateVersions = { template: { ...template }, versions: { items: [{ id: 'v7', versionNumber: 7, status: 'Draft', createdByUserId: 'u1', createdAt: '2026-01-01T00:00:00Z', isReferenced: false, rowVersion: 'version-rv' }], pageNumber: 1, pageSize: 100, totalCount: 1, totalPages: 1 } }

beforeEach(() => { vi.mocked(getFormTemplateVersions).mockResolvedValue(history); vi.mocked(getFormTemplateVersion).mockResolvedValue(draft); vi.stubGlobal('confirm', vi.fn(() => true)) })
afterEach(() => { cleanup(); vi.resetAllMocks(); vi.unstubAllGlobals() })

describe('Form Template lifecycle UI', () => {
  it('updates a Draft with the server rowVersion', async () => {
    const user = userEvent.setup(); vi.mocked(updateFormTemplateDraft).mockResolvedValue({ ...draft, rowVersion: 'version-rv-2' })
    renderPage(); await screen.findByRole('heading', { name: 'Draft' }); await user.click(screen.getByRole('button', { name: 'Chỉnh sửa Draft' }))
    await user.clear(screen.getByLabelText('Label')); await user.type(screen.getByLabelText('Label'), 'Updated note'); await user.click(screen.getByRole('button', { name: 'Lưu Draft' }))
    await waitFor(() => expect(updateFormTemplateDraft).toHaveBeenCalledOnce())
    expect(updateFormTemplateDraft).toHaveBeenCalledWith(template.id, 7, expect.stringContaining('Updated note'), 'version-rv')
  })

  it('keeps edited content and offers reload after a stale conflict', async () => {
    const user = userEvent.setup(); vi.mocked(updateFormTemplateDraft).mockRejectedValue(new ApiError(409, { detail: 'stale' }))
    renderPage(); await screen.findByRole('heading', { name: 'Draft' }); await user.click(screen.getByRole('button', { name: 'Chỉnh sửa Draft' }))
    await user.clear(screen.getByLabelText('Label')); await user.type(screen.getByLabelText('Label'), 'Unsaved note'); await user.click(screen.getByRole('button', { name: 'Lưu Draft' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Dữ liệu đã thay đổi')
    expect(screen.getByDisplayValue('Unsaved note')).toBeInTheDocument(); expect(screen.getByRole('button', { name: 'Tải lại và bỏ thay đổi cục bộ' })).toBeInTheDocument()
  })

  it('confirms and publishes with both rowVersions', async () => {
    const user = userEvent.setup(); vi.mocked(publishFormTemplateDraft).mockResolvedValue({ ...draft, status: 'Published', publishedAt: '2026-01-02T00:00:00Z', publishedByUserId: 'u1' })
    renderPage(); await screen.findByRole('heading', { name: 'Draft' }); await user.click(screen.getByRole('button', { name: 'Publish Draft' }))
    expect(confirm).toHaveBeenCalledWith(expect.stringContaining('bất biến'))
    expect(publishFormTemplateDraft).toHaveBeenCalledWith(template.id, 7, 'template-rv', 'version-rv')
  })

  it('renders Published and Inactive versions read-only', async () => {
    vi.mocked(getFormTemplateVersions).mockResolvedValue({ ...history, template: { ...template, status: 'Inactive', draftVersionNumber: undefined }, versions: { ...history.versions, items: [{ ...history.versions.items[0], status: 'Published' }] } })
    vi.mocked(getFormTemplateVersion).mockResolvedValue({ ...draft, template: { ...template, status: 'Inactive' }, status: 'Published' })
    renderPage(); expect(await screen.findByText('Version này chỉ đọc.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Chỉnh sửa Draft' })).not.toBeInTheDocument(); expect(screen.queryByRole('button', { name: 'Deactivate template' })).not.toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Lịch sử version' })).toBeInTheDocument()
  })

  it('creates a new Draft only when none exists and accepts the server version number', async () => {
    const user = userEvent.setup(); vi.mocked(getFormTemplateVersions).mockResolvedValue({ ...history, template: { ...template, draftVersionNumber: undefined }, versions: { ...history.versions, items: [{ ...history.versions.items[0], status: 'Published' }] } })
    vi.mocked(getFormTemplateVersion).mockResolvedValue({ ...draft, status: 'Published' }); vi.mocked(createFormTemplateVersion).mockResolvedValue({ ...draft, versionNumber: 42 })
    renderPage(); await screen.findByRole('heading', { name: 'Published' }); await user.click(screen.getByRole('button', { name: 'Tạo Draft version mới' })); await user.click(screen.getByRole('button', { name: 'Tạo Draft' }))
    await waitFor(() => expect(createFormTemplateVersion).toHaveBeenCalledOnce())
    expect(createFormTemplateVersion).toHaveBeenCalledWith(template.id, draft.schemaJson, 'template-rv')
  })

  it('hides new-version action while a Draft exists and confirms permanent deactivation', async () => {
    const user = userEvent.setup(); vi.mocked(getFormTemplateVersions).mockResolvedValue({ ...history, template: { ...template, draftVersionNumber: undefined } }); vi.mocked(deactivateFormTemplate).mockResolvedValue({ id: template.id, status: 'Inactive', updatedByUserId: 'u1', updatedAt: '2026-01-02T00:00:00Z', deactivatedByUserId: 'u1', deactivatedAt: '2026-01-02T00:00:00Z', rowVersion: 'new-rv' })
    renderPage(); await screen.findByRole('heading', { name: 'Draft' }); expect(screen.queryByRole('button', { name: 'Tạo Draft version mới' })).not.toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Deactivate template' })); expect(confirm).toHaveBeenCalledWith(expect.stringContaining('không thể tạo, sửa hoặc publish'))
    expect(deactivateFormTemplate).toHaveBeenCalledWith(template.id, 'template-rv')
  })
})

function renderPage() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  return render(<MemoryRouter initialEntries={[`/form-templates/${template.id}?version=7`]}><QueryClientProvider client={client}><Routes><Route path="/form-templates/:id" element={<FormTemplateDetailPage />} /></Routes></QueryClientProvider></MemoryRouter>)
}
