import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import '@testing-library/jest-dom/vitest'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { getDrawing } from '../api/client'
import { AuthProvider } from '../auth/AuthContext'
import { getDocument } from '../pdf/pdfjs'
import { DrawingViewerPage } from './DrawingViewerPage'

vi.mock('../api/client', async importOriginal => {
  const actual = await importOriginal<typeof import('../api/client')>()
  return { ...actual, getDrawing: vi.fn() }
})
vi.mock('../pdf/pdfjs', () => ({ getDocument: vi.fn() }))

const drawing = {
  id: '11111111-1111-1111-1111-111111111111', originalFileName: 'plan.pdf', contentType: 'application/pdf',
  fileSize: 16, sha256: 'a'.repeat(64), pageCount: 2, status: 'Ready' as const,
  createdAt: '2026-10-08T00:00:00Z', createdByUserId: '22222222-2222-2222-2222-222222222222', rowVersion: 'AQ==',
}

describe('DrawingViewerPage', () => {
  beforeEach(() => {
    sessionStorage.setItem('construction-inspection.auth', JSON.stringify({
      token: 'test-session-value',
      user: { id: '1', email: 'administrator@local.test', fullName: 'Administrator', roles: ['ADMINISTRATOR'] },
    }))
    vi.mocked(getDrawing).mockResolvedValue(drawing)
    Object.defineProperty(HTMLCanvasElement.prototype, 'getContext', { configurable: true, value: vi.fn(() => ({})) })
    vi.stubGlobal('ResizeObserver', class { observe() {} disconnect() {} })
  })

  afterEach(() => {
    cleanup()
    sessionStorage.clear()
    vi.unstubAllGlobals()
    vi.resetAllMocks()
  })

  it('uses a direct authenticated URL, renders controls, and destroys PDF.js resources', async () => {
    const cancel = vi.fn()
    const destroy = vi.fn().mockResolvedValue(undefined)
    const render = vi.fn(() => ({ promise: Promise.resolve(), cancel }))
    const getPage = vi.fn().mockResolvedValue({
      getViewport: ({ scale, rotation }: { scale: number; rotation: number }) => ({ width: 600 * scale, height: 800 * scale, rotation }),
      render,
    })
    vi.mocked(getDocument).mockReturnValue({
      promise: Promise.resolve({ numPages: 2, getPage }),
      destroy,
      onProgress: undefined,
    } as never)
    const createObjectUrl = vi.spyOn(URL, 'createObjectURL')

    const view = renderViewer()
    expect(await screen.findByText('Trang 1 / 2')).toBeInTheDocument()
    const options = vi.mocked(getDocument).mock.calls[0][0] as { url: string; httpHeaders: Record<string, string> }
    expect(options.url).toContain(`/api/drawings/${drawing.id}/content`)
    expect(options.url).not.toContain('test-session-value')
    expect(options.httpHeaders.Authorization).toBe('Bearer test-session-value')
    expect(createObjectUrl).not.toHaveBeenCalled()

    await userEvent.click(screen.getByRole('button', { name: 'Trang sau' }))
    expect(await screen.findByText('Trang 2 / 2')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Phóng to' }))
    await userEvent.click(screen.getByRole('button', { name: 'Vừa trang' }))
    await userEvent.click(screen.getByRole('button', { name: 'Xoay 90 độ' }))
    await waitFor(() => expect(getPage).toHaveBeenCalledWith(2))
    fireEvent.keyDown(screen.getByRole('region', { name: `Trình xem ${drawing.originalFileName}` }), { key: 'PageUp' })
    expect(await screen.findByText('Trang 1 / 2')).toBeInTheDocument()
    fireEvent.keyDown(screen.getByRole('region', { name: `Trình xem ${drawing.originalFileName}` }), { key: '+' })
    await waitFor(() => expect(render).toHaveBeenCalled())

    view.unmount()
    await waitFor(() => expect(destroy).toHaveBeenCalled())
  })

  it('shows a safe forbidden state from PDF.js network loading', async () => {
    vi.mocked(getDocument).mockImplementation(() => ({
      promise: Promise.reject({ status: 403 }), destroy: vi.fn().mockResolvedValue(undefined), onProgress: undefined,
    } as never))
    renderViewer()
    expect(await screen.findByRole('alert')).toHaveTextContent('không có quyền')
  })

  it.each([
    [404, 'Không tìm thấy Drawing'],
    [409, 'chưa sẵn sàng'],
    [416, 'phạm vi dữ liệu'],
    [500, 'Không thể tải nội dung PDF'],
    [502, 'kho lưu trữ'],
  ])('shows the safe viewer error state for HTTP %s', async (status, expected) => {
    vi.mocked(getDocument).mockImplementation(() => ({
      promise: Promise.reject({ status }), destroy: vi.fn().mockResolvedValue(undefined), onProgress: undefined,
    } as never))
    renderViewer()
    expect(await screen.findByRole('alert')).toHaveTextContent(expected)
  })
})

function renderViewer() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter initialEntries={[`/drawings/${drawing.id}/viewer`]}>
      <QueryClientProvider client={queryClient}>
        <AuthProvider><Routes><Route path="/drawings/:id/viewer" element={<DrawingViewerPage />} /></Routes></AuthProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}
