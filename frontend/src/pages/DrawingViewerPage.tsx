import { useQuery } from '@tanstack/react-query'
import { useEffect, useRef, useState, type KeyboardEvent } from 'react'
import { Link, useParams } from 'react-router-dom'
import { getDrawing, getDrawingContentUrl } from '../api/client'
import { useAuth } from '../auth/AuthContext'
import { getDocument, type PDFDocumentLoadingTask, type PDFDocumentProxy, type RenderTask } from '../pdf/pdfjs'
import { drawingErrorMessage } from './DrawingsPage'

type FitMode = 'numeric' | 'width' | 'page'

export function DrawingViewerPage() {
  const { id = '' } = useParams()
  const query = useQuery({ queryKey: ['drawing', id], queryFn: () => getDrawing(id) })
  if (query.isLoading) return <p className="state" role="status">Đang tải thông tin Drawing…</p>
  if (query.isError) return <p className="state error" role="alert">{drawingErrorMessage(query.error, 'Không thể tải Drawing.')}</p>
  if (!query.data) return <p className="state error" role="alert">Không tìm thấy Drawing.</p>
  if (query.data.status !== 'Ready') return <p className="state error" role="alert">PDF chưa sẵn sàng để xem.</p>
  return <PdfViewer drawingId={query.data.id} fileName={query.data.originalFileName} />
}

function PdfViewer({ drawingId, fileName }: { drawingId: string; fileName: string }) {
  const { token, logout } = useAuth()
  const [document, setDocument] = useState<PDFDocumentProxy | null>(null)
  const [pageNumber, setPageNumber] = useState(1)
  const [scale, setScale] = useState(1)
  const [fitMode, setFitMode] = useState<FitMode>('width')
  const [rotation, setRotation] = useState(0)
  const [progress, setProgress] = useState<number | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [resizeVersion, setResizeVersion] = useState(0)
  const viewportRef = useRef<HTMLDivElement>(null)
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const loadingTaskRef = useRef<PDFDocumentLoadingTask | null>(null)
  const renderTaskRef = useRef<RenderTask | null>(null)

  useEffect(() => {
    if (!token) return
    let active = true
    const loadingTask = getDocument({
      url: getDrawingContentUrl(drawingId),
      httpHeaders: { Authorization: `Bearer ${token}` },
      withCredentials: false,
      disableRange: false,
      disableStream: false,
      rangeChunkSize: 256 * 1024,
    })
    loadingTaskRef.current = loadingTask
    loadingTask.onProgress = ({ loaded, total }: { loaded: number; total: number }) => {
      if (active && total > 0) setProgress(Math.round(loaded / total * 100))
    }
    loadingTask.promise.then(pdf => {
      if (!active) return
      setDocument(pdf)
      setProgress(100)
    }).catch(reason => {
      if (!active) return
      const status = typeof reason === 'object' && reason && 'status' in reason ? Number(reason.status) : 0
      if (status === 401) { logout(); return }
      if (status === 403) setError('Bạn không có quyền xem PDF này.')
      else if (status === 404) setError('Không tìm thấy Drawing.')
      else if (status === 409) setError('PDF chưa sẵn sàng để xem.')
      else if (status === 416) setError('Máy chủ từ chối phạm vi dữ liệu PDF.')
      else if (status === 502) setError('Không thể lấy PDF từ kho lưu trữ.')
      else setError('Không thể tải nội dung PDF.')
    })
    return () => {
      active = false
      loadingTaskRef.current = null
      void loadingTask.destroy()
    }
  }, [drawingId, logout, token])

  useEffect(() => {
    const element = viewportRef.current
    if (!element || typeof ResizeObserver === 'undefined') return
    const observer = new ResizeObserver(() => setResizeVersion(value => value + 1))
    observer.observe(element)
    return () => observer.disconnect()
  }, [])

  useEffect(() => {
    if (!document || !canvasRef.current || !viewportRef.current) return
    let active = true
    void document.getPage(pageNumber).then(page => {
      if (!active || !canvasRef.current || !viewportRef.current) return
      const base = page.getViewport({ scale: 1, rotation })
      const availableWidth = Math.max(1, viewportRef.current.clientWidth - 32)
      const availableHeight = Math.max(1, viewportRef.current.clientHeight - 32)
      const effectiveScale = fitMode === 'width' ? availableWidth / base.width
        : fitMode === 'page' ? Math.min(availableWidth / base.width, availableHeight / base.height)
          : scale
      const viewport = page.getViewport({ scale: effectiveScale, rotation })
      const ratio = window.devicePixelRatio || 1
      const canvas = canvasRef.current
      canvas.width = Math.floor(viewport.width * ratio)
      canvas.height = Math.floor(viewport.height * ratio)
      canvas.style.width = `${Math.floor(viewport.width)}px`
      canvas.style.height = `${Math.floor(viewport.height)}px`
      const context = canvas.getContext('2d')
      if (!context) { setError('Trình duyệt không hỗ trợ canvas PDF.'); return }
      renderTaskRef.current?.cancel()
      const task = page.render({ canvas, canvasContext: context, viewport, transform: ratio === 1 ? undefined : [ratio, 0, 0, ratio, 0, 0] })
      renderTaskRef.current = task
      task.promise.catch(reason => {
        if (active && reason?.name !== 'RenderingCancelledException') setError('Không thể hiển thị trang PDF.')
      })
    }).catch(() => active && setError('Không thể đọc trang PDF.'))
    return () => { active = false; renderTaskRef.current?.cancel(); renderTaskRef.current = null }
  }, [document, fitMode, pageNumber, resizeVersion, rotation, scale])

  const zoom = (delta: number) => { setFitMode('numeric'); setScale(value => Math.min(4, Math.max(.25, value + delta))) }
  const handleKeyboard = (event: KeyboardEvent<HTMLElement>) => {
    if (event.target instanceof HTMLInputElement || event.target instanceof HTMLTextAreaElement || event.target instanceof HTMLSelectElement) return
    if ((event.key === 'ArrowRight' || event.key === 'PageDown') && document && pageNumber < document.numPages) {
      event.preventDefault(); setPageNumber(value => value + 1)
    } else if ((event.key === 'ArrowLeft' || event.key === 'PageUp') && pageNumber > 1) {
      event.preventDefault(); setPageNumber(value => value - 1)
    } else if (event.key === '+' || event.key === '=') {
      event.preventDefault(); zoom(.25)
    } else if (event.key === '-') {
      event.preventDefault(); zoom(-.25)
    }
  }
  return <section className="pdf-viewer" aria-label={`Trình xem ${fileName}`} tabIndex={0} onKeyDown={handleKeyboard}>
    <header className="viewer-header">
      <div><Link className="back-link" to={`/drawings/${drawingId}`}>← Chi tiết Drawing</Link><h1>{fileName}</h1></div>
      <div className="pdf-toolbar" role="toolbar" aria-label="Điều khiển PDF">
        <button className="button" aria-label="Trang trước" disabled={!document || pageNumber <= 1} onClick={() => setPageNumber(value => value - 1)}>←</button>
        <span aria-live="polite">Trang {pageNumber} / {document?.numPages ?? '–'}</span>
        <button className="button" aria-label="Trang sau" disabled={!document || pageNumber >= document.numPages} onClick={() => setPageNumber(value => value + 1)}>→</button>
        <button className="button" aria-label="Thu nhỏ" onClick={() => zoom(-.25)}>−</button>
        <button className="button" aria-label="Phóng to" onClick={() => zoom(.25)}>+</button>
        <button className="button" aria-pressed={fitMode === 'width'} onClick={() => setFitMode('width')}>Vừa rộng</button>
        <button className="button" aria-pressed={fitMode === 'page'} onClick={() => setFitMode('page')}>Vừa trang</button>
        <button className="button" aria-label="Xoay 90 độ" onClick={() => setRotation(value => (value + 90) % 360)}>Xoay</button>
      </div>
    </header>
    {error ? <div className="viewer-status error" role="alert"><p>{error}</p><button className="button" onClick={() => window.location.reload()}>Thử lại</button></div>
      : !document ? <p className="viewer-status" role="status">Đang tải PDF{progress === null ? '…' : `… ${progress}%`}</p>
        : <div className="pdf-viewport" ref={viewportRef}><canvas ref={canvasRef} aria-label={`Trang ${pageNumber} của ${document.numPages}`} /></div>}
  </section>
}
