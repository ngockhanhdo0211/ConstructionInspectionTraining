import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import '@testing-library/jest-dom/vitest'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { getConstructionObjects, getDrawing, getDrawingMappings } from '../api/client'
import { AuthProvider } from '../auth/AuthContext'
import { getDocument } from '../pdf/pdfjs'
import { DrawingViewerPage } from './DrawingViewerPage'

vi.mock('../api/client', async importOriginal => {
  const actual = await importOriginal<typeof import('../api/client')>()
  return { ...actual, getDrawing: vi.fn(), getDrawingMappings: vi.fn(), getConstructionObjects: vi.fn() }
})
vi.mock('../pdf/pdfjs', () => ({ getDocument: vi.fn() }))

const drawingId = '11111111-1111-1111-1111-111111111111'
const mappingId = '33333333-3333-3333-3333-333333333333'
const mapping = {
  id: mappingId, drawingDocumentId: drawingId, pageNumber: 1,
  constructionObject: { id: '44444444-4444-4444-4444-444444444444', code: 'ROOM-101', name: 'Phòng 101', objectType: 'Room' as const, isActive: true },
  shapeType: 'Rectangle' as const, geometry: { left: .1, bottom: .2, right: .4, top: .5 }, status: 'Active' as const,
  createdAt: '2026-10-12T00:00:00Z', createdByUserId: '1', rowVersion: 'AQ==',
}

describe('Drawing mapping overlay', () => {
  beforeEach(() => {
    vi.mocked(getDrawing).mockResolvedValue({ id: drawingId, originalFileName: 'plan.pdf', fileSize: 10, pageCount: 2, status: 'Ready', createdAt: '2026-10-12T00:00:00Z', createdByUserId: '1' })
    vi.mocked(getDrawingMappings).mockResolvedValue({ drawingId, pageNumber: 1, items: [mapping] })
    vi.mocked(getConstructionObjects).mockResolvedValue({ items: [{ ...mapping.constructionObject, parentId: null, location: null, rowVersion: 'AQ==' }], pageNumber: 1, pageSize: 100, totalCount: 1, totalPages: 1 })
    const renderTask = { promise: Promise.resolve(), cancel: vi.fn() }
    vi.mocked(getDocument).mockReturnValue({ promise: Promise.resolve({ numPages: 2, getPage: vi.fn().mockResolvedValue({
      rotate: 0, view: [0, 0, 600, 800], render: vi.fn(() => renderTask),
      getViewport: ({ scale }: { scale: number }) => ({ width: 600 * scale, height: 800 * scale, convertToViewportPoint: (x: number, y: number) => [x * scale, (800 - y) * scale], convertToPdfPoint: (x: number, y: number) => [x / scale, 800 - y / scale] }),
    }) }), destroy: vi.fn().mockResolvedValue(undefined), onProgress: undefined } as never)
    Object.defineProperty(HTMLCanvasElement.prototype, 'getContext', { configurable: true, value: vi.fn(() => ({})) })
    Object.defineProperty(SVGSVGElement.prototype, 'setPointerCapture', { configurable: true, value: vi.fn() })
    Object.defineProperty(SVGSVGElement.prototype, 'releasePointerCapture', { configurable: true, value: vi.fn() })
    vi.stubGlobal('ResizeObserver', class { observe() {} disconnect() {} })
  })

  afterEach(() => { cleanup(); sessionStorage.clear(); vi.unstubAllGlobals(); vi.resetAllMocks() })

  it('loads only the current page and highlights a valid deep link', async () => {
    renderViewer('ADMINISTRATOR', `?page=1&mapping=${mappingId}`)
    const shape = await screen.findByRole('button', { name: /ROOM-101.*trang 1/ })
    expect(shape).toHaveClass('selected')
    expect(getDrawingMappings).toHaveBeenCalledWith(drawingId, 1, expect.any(AbortSignal))
    expect(window.location.href).not.toContain('test-session-value')
  })

  it('does not highlight an invalid mapping id', async () => {
    renderViewer('ADMINISTRATOR', '?page=1&mapping=aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa')
    expect(await screen.findByRole('alert')).toHaveTextContent('không thuộc Drawing')
    expect(screen.getByRole('button', { name: /ROOM-101.*trang 1/ })).not.toHaveClass('selected')
  })

  it('gives Manager view-only UI', async () => {
    renderViewer('MANAGER', '?page=1')
    expect(await screen.findByRole('button', { name: /ROOM-101.*trang 1/ })).toBeInTheDocument()
    expect(screen.queryByRole('toolbar', { name: 'Chế độ Mapping' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Deactivate' })).not.toBeInTheDocument()
  })

  it('creates a local draft and Escape cancels it without sending an API request', async () => {
    renderViewer('ADMINISTRATOR', '?page=1')
    await screen.findByRole('button', { name: /ROOM-101.*trang 1/ })
    await userEvent.click(screen.getByRole('button', { name: 'Khoanh vùng' }))
    const overlay = screen.getByLabelText('Mapping trang 1')
    vi.spyOn(overlay, 'getBoundingClientRect').mockReturnValue({ x: 0, y: 0, left: 0, top: 0, right: 600, bottom: 800, width: 600, height: 800, toJSON: () => ({}) })
    fireEvent.pointerDown(overlay, { pointerId: 1, clientX: 60, clientY: 80 })
    fireEvent.pointerMove(overlay, { pointerId: 1, clientX: 300, clientY: 400 })
    expect(await screen.findByRole('dialog', { name: 'Xác nhận Rectangle' })).toBeInTheDocument()
    fireEvent.change(screen.getByLabelText('Tìm Construction Object'), { target: { value: 'ROOM' } })
    await waitFor(() => expect(getConstructionObjects).toHaveBeenCalledWith('ROOM', expect.any(AbortSignal)))
    fireEvent.keyDown(screen.getByRole('region', { name: 'Trình xem plan.pdf' }), { key: 'Escape' })
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Xác nhận Rectangle' })).not.toBeInTheDocument())
  })

  it('allows Escape to cancel a draft while a mode button has focus', async () => {
    renderViewer('ADMINISTRATOR', '?page=1')
    const drawButton = await screen.findByRole('button', { name: 'Khoanh vùng' })
    await userEvent.click(drawButton)
    const overlay = screen.getByLabelText('Mapping trang 1')
    vi.spyOn(overlay, 'getBoundingClientRect').mockReturnValue({ x: 0, y: 0, left: 0, top: 0, right: 600, bottom: 800, width: 600, height: 800, toJSON: () => ({}) })
    fireEvent.pointerDown(overlay, { pointerId: 1, clientX: 80, clientY: 80 })
    fireEvent.pointerMove(overlay, { pointerId: 1, clientX: 280, clientY: 320 })
    expect(await screen.findByRole('dialog', { name: 'Xác nhận Rectangle' })).toBeInTheDocument()
    drawButton.focus()
    fireEvent.keyDown(drawButton, { key: 'Escape' })
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Xác nhận Rectangle' })).not.toBeInTheDocument())
  })

  it('rejects a zero-area rectangle at pointer up with a clear message', async () => {
    renderViewer('ADMINISTRATOR', '?page=1')
    await screen.findByRole('button', { name: /ROOM-101.*trang 1/ })
    await userEvent.click(screen.getByRole('button', { name: 'Khoanh vùng' }))
    const overlay = screen.getByLabelText('Mapping trang 1')
    vi.spyOn(overlay, 'getBoundingClientRect').mockReturnValue({ x: 0, y: 0, left: 0, top: 0, right: 600, bottom: 800, width: 600, height: 800, toJSON: () => ({}) })
    fireEvent.pointerDown(overlay, { pointerId: 7, clientX: 120, clientY: 160 })
    fireEvent.pointerUp(overlay, { pointerId: 7, clientX: 120, clientY: 160 })
    expect(await screen.findByRole('alert')).toHaveTextContent('Rectangle quá nhỏ')
    expect(screen.queryByRole('dialog', { name: 'Xác nhận Rectangle' })).not.toBeInTheDocument()
  })
})

function renderViewer(role: string, query: string) {
  sessionStorage.setItem('construction-inspection.auth', JSON.stringify({ token: 'test-session-value', user: { id: '1', email: 'user@local.test', fullName: role, roles: [role] } }))
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(<MemoryRouter initialEntries={[`/drawings/${drawingId}/viewer${query}`]}><QueryClientProvider client={client}><AuthProvider><Routes><Route path="/drawings/:id/viewer" element={<DrawingViewerPage />} /></Routes></AuthProvider></QueryClientProvider></MemoryRouter>)
}
