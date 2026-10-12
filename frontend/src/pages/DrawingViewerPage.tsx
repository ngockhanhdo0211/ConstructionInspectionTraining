import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useRef, useState, type KeyboardEvent, type PointerEvent as ReactPointerEvent } from 'react'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import {
  ApiError, createDrawingMapping, createObjectWithDrawingMapping, deactivateDrawingMapping,
  getConstructionObjects, getDrawing, getDrawingContentUrl, getDrawingMappings, updateDrawingMapping,
} from '../api/client'
import type { ConstructionObjectType, DrawingObjectMapping, DrawingRectangle } from '../api/types'
import { useAuth } from '../auth/AuthContext'
import { getDocument, type PDFDocumentLoadingTask, type PDFDocumentProxy, type PDFPageProxy, type RenderTask } from '../pdf/pdfjs'
import { canonicalToViewport, clampGeometry, isUsableRectangle, viewportToCanonical, type PdfViewportTransform } from '../pdf/mappingGeometry'
import { drawingErrorMessage } from './DrawingsPage'

type FitMode = 'numeric' | 'width' | 'page'
type MappingMode = 'view' | 'draw' | 'edit'
type ViewportState = { width: number; height: number; transform: PdfViewportTransform; pageBox: readonly [number, number, number, number] }
type DragState = { kind: 'draw'; start: [number, number] } | { kind: 'move'; start: [number, number]; original: DrawingRectangle }
  | { kind: 'resize'; corner: 'nw' | 'ne' | 'sw' | 'se'; original: DrawingRectangle }
const noMappings: DrawingObjectMapping[] = []

export function DrawingViewerPage() {
  const { id = '' } = useParams()
  const query = useQuery({ queryKey: ['drawing', id], queryFn: () => getDrawing(id) })
  if (query.isLoading) return <p className="state" role="status">Đang tải thông tin Drawing…</p>
  if (query.isError) return <p className="state error" role="alert">{drawingErrorMessage(query.error, 'Không thể tải Drawing.')}</p>
  if (!query.data) return <p className="state error" role="alert">Không tìm thấy Drawing.</p>
  if (query.data.status !== 'Ready') return <p className="state error" role="alert">PDF chưa sẵn sàng để xem.</p>
  return <PdfViewer drawingId={query.data.id} fileName={query.data.originalFileName} expectedPages={query.data.pageCount} />
}

function PdfViewer({ drawingId, fileName, expectedPages }: { drawingId: string; fileName: string; expectedPages: number }) {
  const { token, logout, user } = useAuth()
  const queryClient = useQueryClient()
  const [searchParams, setSearchParams] = useSearchParams()
  const [document, setDocument] = useState<PDFDocumentProxy | null>(null)
  const [pageNumber, setPageNumber] = useState(() => parsePage(searchParams.get('page'), expectedPages))
  const [scale, setScale] = useState(1)
  const [fitMode, setFitMode] = useState<FitMode>('width')
  const [rotation, setRotation] = useState(0)
  const [progress, setProgress] = useState<number | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [mappingError, setMappingError] = useState<string | null>(null)
  const [resizeVersion, setResizeVersion] = useState(0)
  const [viewportState, setViewportState] = useState<ViewportState | null>(null)
  const [mode, setMode] = useState<MappingMode>('view')
  const [selectedId, setSelectedId] = useState<string | null>(searchParams.get('mapping'))
  const [draft, setDraft] = useState<DrawingRectangle | null>(null)
  const [editDraft, setEditDraft] = useState<DrawingRectangle | null>(null)
  const [drag, setDrag] = useState<DragState | null>(null)
  const [selectedObjectId, setSelectedObjectId] = useState('')
  const [objectSearch, setObjectSearch] = useState('')
  const [createNewObject, setCreateNewObject] = useState(false)
  const [objectForm, setObjectForm] = useState({ parentId: '', code: '', name: '', objectType: 'Project' as ConstructionObjectType, location: '', description: '' })
  const [saving, setSaving] = useState(false)
  const viewportRef = useRef<HTMLDivElement>(null)
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const renderTaskRef = useRef<RenderTask | null>(null)
  const administrator = user?.roles.some(role => role.toLowerCase() === 'administrator') ?? false
  const canView = user?.roles.some(role => ['administrator', 'manager'].includes(role.toLowerCase())) ?? false

  const mappingsQuery = useQuery({
    queryKey: ['drawing-mappings', drawingId, pageNumber],
    queryFn: ({ signal }) => getDrawingMappings(drawingId, pageNumber, signal),
    enabled: Boolean(document && canView), retry: false,
  })
  const objectsQuery = useQuery({
    queryKey: ['construction-objects', 'active', objectSearch], queryFn: ({ signal }) => getConstructionObjects(objectSearch.trim(), signal),
    enabled: administrator && Boolean(draft || mode === 'edit'), retry: false,
  })
  const mappings = mappingsQuery.data?.items ?? noMappings
  const selected = mappings.find(item => item.id === selectedId) ?? null

  useEffect(() => {
    if (!token) return
    let active = true
    const loadingTask: PDFDocumentLoadingTask = getDocument({
      url: getDrawingContentUrl(drawingId), httpHeaders: { Authorization: `Bearer ${token}` }, withCredentials: false,
      disableRange: false, disableStream: false, rangeChunkSize: 256 * 1024,
    })
    loadingTask.onProgress = ({ loaded, total }: { loaded: number; total: number }) => active && total > 0 && setProgress(Math.round(loaded / total * 100))
    loadingTask.promise.then(pdf => { if (active) { setDocument(pdf); setProgress(100); setPageNumber(value => Math.min(pdf.numPages, Math.max(1, value))) } })
      .catch(reason => { if (!active) return; const status = statusOf(reason); if (status === 401) logout(); else setError(pdfError(status)) })
    return () => { active = false; void loadingTask.destroy() }
  }, [drawingId, logout, token])

  useEffect(() => {
    const element = viewportRef.current
    if (!element || typeof ResizeObserver === 'undefined') return
    const observer = new ResizeObserver(() => setResizeVersion(value => value + 1))
    observer.observe(element)
    return () => observer.disconnect()
  }, [document])

  useEffect(() => {
    if (!document || !canvasRef.current || !viewportRef.current) return
    let active = true
    void document.getPage(pageNumber).then(page => renderPage(page)).catch(() => active && setError('Không thể đọc trang PDF.'))
    function renderPage(page: PDFPageProxy) {
      if (!active || !canvasRef.current || !viewportRef.current) return
      const combinedRotation = (page.rotate + rotation) % 360
      const base = page.getViewport({ scale: 1, rotation: combinedRotation })
      const availableWidth = Math.max(1, viewportRef.current.clientWidth - 32)
      const availableHeight = Math.max(1, viewportRef.current.clientHeight - 32)
      const effectiveScale = fitMode === 'width' ? availableWidth / base.width : fitMode === 'page'
        ? Math.min(availableWidth / base.width, availableHeight / base.height) : scale
      const viewport = page.getViewport({ scale: effectiveScale, rotation: combinedRotation })
      const ratio = window.devicePixelRatio || 1; const canvas = canvasRef.current
      canvas.width = Math.floor(viewport.width * ratio); canvas.height = Math.floor(viewport.height * ratio)
      canvas.style.width = `${viewport.width}px`; canvas.style.height = `${viewport.height}px`
      setViewportState({ width: viewport.width, height: viewport.height, transform: viewport, pageBox: page.view as [number, number, number, number] })
      const context = canvas.getContext('2d')
      if (!context) { setError('Trình duyệt không hỗ trợ canvas PDF.'); return }
      renderTaskRef.current?.cancel()
      const task = page.render({ canvas, canvasContext: context, viewport, transform: ratio === 1 ? undefined : [ratio, 0, 0, ratio, 0, 0] })
      renderTaskRef.current = task
      task.promise.catch(reason => { if (active && reason?.name !== 'RenderingCancelledException') setError('Không thể hiển thị trang PDF.') })
    }
    return () => { active = false; renderTaskRef.current?.cancel(); renderTaskRef.current = null }
  }, [document, fitMode, pageNumber, resizeVersion, rotation, scale])

  useEffect(() => { setDraft(null); setEditDraft(null); setDrag(null); setMode('view'); setSelectedId(null) }, [pageNumber])
  useEffect(() => { if (mode !== 'draw') { setDraft(null); setDrag(null) }; if (mode !== 'edit') setEditDraft(null) }, [mode])
  useEffect(() => {
    const requestedPage = parsePage(searchParams.get('page'), document?.numPages ?? expectedPages)
    setPageNumber(current => current === requestedPage ? current : requestedPage)
  }, [document?.numPages, expectedPages, searchParams])
  useEffect(() => {
    const requested = searchParams.get('mapping')
    if (!mappingsQuery.isSuccess || !requested) return
    if (mappings.some(item => item.id === requested)) setSelectedId(requested)
    else { setMappingError('Mapping trong liên kết không thuộc Drawing hoặc trang này.'); const next = new URLSearchParams(searchParams); next.delete('mapping'); setSearchParams(next, { replace: true }) }
  }, [mappings, mappingsQuery.isSuccess, searchParams, setSearchParams])

  const updateUrl = (page: number, mapping: string | null) => { const next = new URLSearchParams(); next.set('page', String(page)); if (mapping) next.set('mapping', mapping); setSearchParams(next, { replace: true }) }
  const changePage = (next: number) => { if (draft || editDraft) setMappingError('Bản nháp chưa lưu đã được hủy khi đổi trang.'); setPageNumber(next); updateUrl(next, null) }
  const selectMapping = (mapping: DrawingObjectMapping) => { setSelectedId(mapping.id); setSelectedObjectId(mapping.constructionObject.id); setEditDraft(null); updateUrl(pageNumber, mapping.id) }
  const zoom = (delta: number) => { setFitMode('numeric'); setScale(value => Math.min(4, Math.max(.25, value + delta))) }
  const handleKeyboard = (event: KeyboardEvent<HTMLElement>) => {
    const target = event.target as HTMLElement
    if (event.key === 'Escape' && (draft || editDraft)) { event.preventDefault(); setDraft(null); setEditDraft(null); setDrag(null); setMappingError(null); return }
    if (event.ctrlKey || event.metaKey || event.altKey || target.closest('input,textarea,select,button,a,[role="dialog"],[contenteditable="true"]')) return
    if ((event.key === 'ArrowRight' || event.key === 'PageDown') && document && pageNumber < document.numPages) { event.preventDefault(); changePage(pageNumber + 1) }
    else if ((event.key === 'ArrowLeft' || event.key === 'PageUp') && pageNumber > 1) { event.preventDefault(); changePage(pageNumber - 1) }
    else if (event.key === '+' || event.key === '=') { event.preventDefault(); zoom(.25) }
    else if (event.key === '-') { event.preventDefault(); zoom(-.25) }
    else if (event.key.toLowerCase() === 'r') { event.preventDefault(); setRotation(value => (value + 90) % 360) }
    else if (event.key === '0') { event.preventDefault(); setFitMode(event.shiftKey ? 'width' : 'page') }
  }

  const localPoint = (event: ReactPointerEvent<SVGSVGElement>): [number, number] => { const bounds = event.currentTarget.getBoundingClientRect(); return [Math.min(bounds.width, Math.max(0, event.clientX - bounds.left)), Math.min(bounds.height, Math.max(0, event.clientY - bounds.top))] }
  const beginOverlay = (event: ReactPointerEvent<SVGSVGElement>) => {
    if (!administrator || mode !== 'draw' || !viewportState || event.target !== event.currentTarget) return
    event.currentTarget.setPointerCapture(event.pointerId); setDrag({ kind: 'draw', start: localPoint(event) }); setDraft(null); setMappingError(null)
  }
  const moveOverlay = (event: ReactPointerEvent<SVGSVGElement>) => {
    if (!drag || !viewportState) return
    const current = localPoint(event)
    if (drag.kind === 'draw') setDraft(viewportToCanonical([drag.start, current], viewportState.pageBox, viewportState.transform))
    else if (drag.kind === 'move') {
      const startPdf = viewportToCanonical([drag.start, drag.start], viewportState.pageBox, viewportState.transform)
      const currentPdf = viewportToCanonical([current, current], viewportState.pageBox, viewportState.transform)
      const dx = currentPdf.left - startPdf.left; const dy = currentPdf.bottom - startPdf.bottom
      setEditDraft(clampGeometry({ left: drag.original.left + dx, right: drag.original.right + dx, bottom: drag.original.bottom + dy, top: drag.original.top + dy }))
    } else {
      const cursor = viewportToCanonical([current, current], viewportState.pageBox, viewportState.transform)
      const x = cursor.left; const y = cursor.bottom; const original = drag.original
      const next = {
        left: drag.corner.includes('w') ? Math.min(x, original.right - .000001) : original.left,
        right: drag.corner.includes('e') ? Math.max(x, original.left + .000001) : original.right,
        bottom: drag.corner.includes('s') ? Math.min(y, original.top - .000001) : original.bottom,
        top: drag.corner.includes('n') ? Math.max(y, original.bottom + .000001) : original.top,
      }
      setEditDraft({ left: Math.max(0, next.left), bottom: Math.max(0, next.bottom), right: Math.min(1, next.right), top: Math.min(1, next.top) })
    }
  }
  const endOverlay = (event: ReactPointerEvent<SVGSVGElement>) => {
    if (!drag) return
    event.currentTarget.releasePointerCapture(event.pointerId); setDrag(null)
    if (drag.kind === 'draw' && viewportState) {
      const completed = viewportToCanonical([drag.start, localPoint(event)], viewportState.pageBox, viewportState.transform)
      if (isUsableRectangle(completed)) setDraft(completed)
      else { setDraft(null); setMappingError('Rectangle quá nhỏ; hãy khoanh vùng lớn hơn.') }
    }
  }

  const saveNew = async () => {
    if (!draft) return
    setSaving(true); setMappingError(null)
    try {
      const result = createNewObject ? (await createObjectWithDrawingMapping(drawingId, pageNumber, draft, { ...objectForm, parentId: objectForm.parentId || null })).mapping : await createDrawingMapping(drawingId, pageNumber, selectedObjectId, draft)
      await queryClient.invalidateQueries({ queryKey: ['drawing-mappings', drawingId, pageNumber] })
      setDraft(null); setMode('view'); setSelectedId(result.id); updateUrl(pageNumber, result.id)
    } catch (reason) { setMappingError(mappingErrorMessage(reason)) } finally { setSaving(false) }
  }
  const saveEdit = async () => {
    if (!selected || !editDraft) return
    setSaving(true); setMappingError(null)
    try {
      const result = await updateDrawingMapping(selected.id, selectedObjectId || selected.constructionObject.id, editDraft, selected.rowVersion)
      await queryClient.invalidateQueries({ queryKey: ['drawing-mappings', drawingId, pageNumber] }); setEditDraft(null); setMode('view'); setSelectedId(result.id)
    } catch (reason) { setMappingError(mappingErrorMessage(reason)); if (reason instanceof ApiError && reason.status === 409) { setEditDraft(null); await queryClient.invalidateQueries({ queryKey: ['drawing-mappings', drawingId, pageNumber] }) } } finally { setSaving(false) }
  }
  const deactivate = async () => {
    if (!selected || !window.confirm(`Deactivate mapping tới ${selected.constructionObject.code}?`)) return
    setSaving(true); setMappingError(null)
    try { await deactivateDrawingMapping(selected.id, selected.rowVersion); setSelectedId(null); updateUrl(pageNumber, null); await queryClient.invalidateQueries({ queryKey: ['drawing-mappings', drawingId, pageNumber] }) }
    catch (reason) { setMappingError(mappingErrorMessage(reason)); if (reason instanceof ApiError && reason.status === 409) await queryClient.invalidateQueries({ queryKey: ['drawing-mappings', drawingId, pageNumber] }) } finally { setSaving(false) }
  }

  return <section className="pdf-viewer mapping-viewer" aria-label={`Trình xem ${fileName}`} tabIndex={0} onKeyDown={handleKeyboard}>
    <header className="viewer-header"><div><Link className="back-link" to={`/drawings/${drawingId}`}>← Chi tiết Drawing</Link><h1>{fileName}</h1></div><div className="pdf-toolbar" role="toolbar" aria-label="Điều khiển PDF">
      <button className="button" aria-label="Trang trước" disabled={!document || pageNumber <= 1} onClick={() => changePage(pageNumber - 1)}>←</button><span aria-live="polite">Trang {pageNumber} / {document?.numPages ?? '–'}</span><button className="button" aria-label="Trang sau" disabled={!document || pageNumber >= document.numPages} onClick={() => changePage(pageNumber + 1)}>→</button>
      <button className="button" aria-label="Thu nhỏ" onClick={() => zoom(-.25)}>−</button><button className="button" aria-label="Phóng to" onClick={() => zoom(.25)}>+</button><button className="button" aria-pressed={fitMode === 'width'} onClick={() => setFitMode('width')}>Vừa rộng</button><button className="button" aria-pressed={fitMode === 'page'} onClick={() => setFitMode('page')}>Vừa trang</button><button className="button" aria-label="Xoay 90 độ" onClick={() => setRotation(value => (value + 90) % 360)}>Xoay</button>
    </div></header>
    <div className="mapping-workspace"><div className="mapping-main">
      {administrator && <div className="mapping-modes" role="toolbar" aria-label="Chế độ Mapping">{(['view', 'draw', 'edit'] as const).map(value => <button key={value} className="button" aria-pressed={mode === value} disabled={value === 'edit' && !selected} onClick={() => { setMode(value); if (value === 'edit' && selected) { setEditDraft(selected.geometry); setSelectedObjectId(selected.constructionObject.id) } }}>{value === 'view' ? 'Xem' : value === 'draw' ? 'Khoanh vùng' : 'Chỉnh sửa'}</button>)}</div>}
      {error ? <div className="viewer-status error" role="alert"><p>{error}</p><button className="button" onClick={() => window.location.reload()}>Thử lại</button></div> : <div className="pdf-viewport" ref={viewportRef}>{!document ? <p className="viewer-status" role="status">Đang tải PDF{progress === null ? '…' : `… ${progress}%`}</p> : <div className="page-surface" style={viewportState ? { width: viewportState.width, height: viewportState.height } : undefined}><canvas ref={canvasRef} aria-label={`Trang ${pageNumber} của ${document.numPages}`} />{viewportState && <svg className={`mapping-overlay mode-${mode}`} width={viewportState.width} height={viewportState.height} viewBox={`0 0 ${viewportState.width} ${viewportState.height}`} aria-label={`Mapping trang ${pageNumber}`} onPointerDown={beginOverlay} onPointerMove={moveOverlay} onPointerUp={endOverlay} onPointerCancel={() => { setDrag(null); setDraft(null) }}>
        {mappings.map(mapping => <MappingRect key={mapping.id} mapping={mapping} geometry={mapping.id === selectedId && editDraft ? editDraft : mapping.geometry} viewport={viewportState} selected={mapping.id === selectedId} editable={administrator && mode === 'edit' && mapping.id === selectedId} onSelect={() => selectMapping(mapping)} onMoveStart={(event) => { if (mode !== 'edit') return; event.stopPropagation(); const owner = event.currentTarget.ownerSVGElement; if (!owner) return; owner.setPointerCapture(event.pointerId); const bounds = owner.getBoundingClientRect(); setDrag({ kind: 'move', start: [event.clientX - bounds.left, event.clientY - bounds.top], original: editDraft ?? mapping.geometry }) }} onResizeStart={(corner, event) => { event.stopPropagation(); const owner = event.currentTarget.ownerSVGElement; if (!owner) return; owner.setPointerCapture(event.pointerId); setDrag({ kind: 'resize', corner, original: editDraft ?? mapping.geometry }) }} />)}
        {draft && <PreviewRect geometry={draft} viewport={viewportState} />}
      </svg>}</div>}</div>}
    </div><aside className="mapping-panel" aria-label="Thông tin Mapping"><h2>Mapping trang {pageNumber}</h2>
      {mappingsQuery.isLoading && <p role="status">Đang tải Mapping…</p>}{mappingsQuery.isError && <p className="notice error" role="alert">{mappingErrorMessage(mappingsQuery.error)}</p>}{mappingError && <p className="notice error" role="alert">{mappingError}</p>}{!mappingsQuery.isLoading && !mappingsQuery.isError && mappings.length === 0 && <p>Trang này chưa có Mapping.</p>}
      <ul className="mapping-list">{mappings.map(mapping => <li key={mapping.id}><button className={mapping.id === selectedId ? 'selected' : ''} onClick={() => selectMapping(mapping)}><strong>{mapping.constructionObject.code}</strong><span>{mapping.constructionObject.name}</span>{!mapping.constructionObject.isActive && <em>Object inactive</em>}</button></li>)}</ul>
      {selected && <section className="mapping-selection"><h3>{selected.constructionObject.code}</h3><p>{selected.constructionObject.name} · {selected.constructionObject.objectType}</p>{administrator && <div className="panel-actions"><button className="button" onClick={() => { setMode('edit'); setEditDraft(selected.geometry); setSelectedObjectId(selected.constructionObject.id) }}>Chỉnh sửa</button><button className="button danger" disabled={saving} onClick={deactivate}>Deactivate</button></div>}</section>}
      {administrator && ((draft && !createNewObject) || editDraft) && <label>Tìm Construction Object<input value={objectSearch} onChange={event => setObjectSearch(event.target.value)} /></label>}
      {administrator && draft && <MappingForm title="Xác nhận Rectangle" geometry={draft} objects={objectsQuery.data?.items ?? []} objectId={selectedObjectId} setObjectId={setSelectedObjectId} createNew={createNewObject} setCreateNew={setCreateNewObject} objectForm={objectForm} setObjectForm={setObjectForm} saving={saving} onSave={saveNew} onCancel={() => setDraft(null)} />}
      {administrator && selected && editDraft && mode === 'edit' && <MappingForm title="Chỉnh sửa Mapping" geometry={editDraft} setGeometry={value => setEditDraft(clampGeometry(value))} objects={objectsQuery.data?.items ?? []} objectId={selectedObjectId} setObjectId={setSelectedObjectId} createNew={false} setCreateNew={() => undefined} objectForm={objectForm} setObjectForm={setObjectForm} saving={saving} onSave={saveEdit} onCancel={() => { setEditDraft(null); setMode('view') }} />}
    </aside></div>
  </section>
}

function MappingRect({ mapping, geometry, viewport, selected, editable, onSelect, onMoveStart, onResizeStart }: { mapping: DrawingObjectMapping; geometry: DrawingRectangle; viewport: ViewportState; selected: boolean; editable: boolean; onSelect(): void; onMoveStart(event: ReactPointerEvent<SVGRectElement>): void; onResizeStart(corner: 'nw' | 'ne' | 'sw' | 'se', event: ReactPointerEvent<SVGRectElement>): void }) {
  const rect = canonicalToViewport(geometry, viewport.pageBox, viewport.transform)
  const handles: Array<{ x: number; y: number; corner: 'nw' | 'ne' | 'sw' | 'se' }> = [{ x: rect.x, y: rect.y, corner: 'nw' }, { x: rect.x + rect.width, y: rect.y, corner: 'ne' }, { x: rect.x, y: rect.y + rect.height, corner: 'sw' }, { x: rect.x + rect.width, y: rect.y + rect.height, corner: 'se' }]
  return <g className={`mapping-shape${selected ? ' selected' : ''}`} role="button" tabIndex={0} aria-label={`${mapping.constructionObject.code}, ${mapping.constructionObject.name}, trang ${mapping.pageNumber}`} onClick={event => { event.stopPropagation(); onSelect() }} onKeyDown={event => { if (event.key === 'Enter') onSelect() }}><rect x={rect.x} y={rect.y} width={rect.width} height={rect.height} onPointerDown={onMoveStart} />{editable && handles.map(handle => <rect key={handle.corner} className="resize-handle" x={handle.x - 5} y={handle.y - 5} width="10" height="10" aria-label={`Resize ${handle.corner}`} onPointerDown={event => onResizeStart(handle.corner, event)} />)}</g>
}
function PreviewRect({ geometry, viewport }: { geometry: DrawingRectangle; viewport: ViewportState }) { const rect = canonicalToViewport(geometry, viewport.pageBox, viewport.transform); return <rect className="mapping-draft" x={rect.x} y={rect.y} width={rect.width} height={rect.height} /> }

type ObjectFormState = { parentId: string; code: string; name: string; objectType: ConstructionObjectType; location: string; description: string }
function MappingForm({ title, geometry, setGeometry, objects, objectId, setObjectId, createNew, setCreateNew, objectForm, setObjectForm, saving, onSave, onCancel }: { title: string; geometry: DrawingRectangle; setGeometry?: (value: DrawingRectangle) => void; objects: { id: string; code: string; name: string; objectType: ConstructionObjectType }[]; objectId: string; setObjectId(value: string): void; createNew: boolean; setCreateNew(value: boolean): void; objectForm: ObjectFormState; setObjectForm(value: ObjectFormState): void; saving: boolean; onSave(): void; onCancel(): void }) {
  const parentType = ({ Building: 'Project', Floor: 'Building', Room: 'Floor', WorkItem: 'Room' } as Partial<Record<ConstructionObjectType, ConstructionObjectType>>)[objectForm.objectType]
  return <section className="mapping-form" role="dialog" aria-label={title}><h3>{title}</h3>{setGeometry && <fieldset><legend>Tọa độ chuẩn hóa</legend>{(['left', 'bottom', 'right', 'top'] as const).map(key => <label key={key}>{key}<input type="number" min="0" max="1" step="0.000001" value={geometry[key]} onChange={event => setGeometry({ ...geometry, [key]: Number(event.target.value) })} /></label>)}</fieldset>}{!setGeometry && <label className="check"><input type="checkbox" checked={createNew} onChange={event => setCreateNew(event.target.checked)} /> Tạo Construction Object mới atomically</label>}{!createNew ? <label>Construction Object<select value={objectId} onChange={event => setObjectId(event.target.value)}><option value="">Chọn Object active</option>{objects.map(object => <option key={object.id} value={object.id}>{object.code} — {object.name}</option>)}</select></label> : <div className="object-fields"><label>Object type<select value={objectForm.objectType} onChange={event => setObjectForm({ ...objectForm, objectType: event.target.value as ConstructionObjectType, parentId: '' })}>{['Project', 'Building', 'Floor', 'Room', 'WorkItem'].map(type => <option key={type}>{type}</option>)}</select></label>{parentType && <label>Parent {parentType}<select value={objectForm.parentId} onChange={event => setObjectForm({ ...objectForm, parentId: event.target.value })}><option value="">Chọn parent active</option>{objects.filter(object => object.objectType === parentType).map(object => <option key={object.id} value={object.id}>{object.code} — {object.name}</option>)}</select></label>}<label>Code<input value={objectForm.code} onChange={event => setObjectForm({ ...objectForm, code: event.target.value })} /></label><label>Name<input value={objectForm.name} onChange={event => setObjectForm({ ...objectForm, name: event.target.value })} /></label><label>Location<input value={objectForm.location} onChange={event => setObjectForm({ ...objectForm, location: event.target.value })} /></label><label>Description<textarea value={objectForm.description} onChange={event => setObjectForm({ ...objectForm, description: event.target.value })} /></label></div>}<div className="panel-actions"><button className="button primary" disabled={saving || (!createNew && !objectId) || (createNew && (!objectForm.code || !objectForm.name || (parentType && !objectForm.parentId))) || !isUsableRectangle(geometry)} onClick={onSave}>Lưu</button><button className="button" disabled={saving} onClick={onCancel}>Hủy</button></div></section>
}

function parsePage(value: string | null, pageCount: number) { const parsed = Number(value); return Number.isInteger(parsed) && parsed >= 1 && parsed <= pageCount ? parsed : 1 }
function statusOf(reason: unknown) { return typeof reason === 'object' && reason && 'status' in reason ? Number(reason.status) : 0 }
function pdfError(status: number) { if (status === 403) return 'Bạn không có quyền xem PDF này.'; if (status === 404) return 'Không tìm thấy Drawing.'; if (status === 409) return 'PDF chưa sẵn sàng để xem.'; if (status === 416) return 'Máy chủ từ chối phạm vi dữ liệu PDF.'; if (status === 502) return 'Không thể lấy PDF từ kho lưu trữ.'; return 'Không thể tải nội dung PDF.' }
function mappingErrorMessage(reason: unknown) {
  if (!(reason instanceof ApiError)) return 'Không thể hoàn tất thao tác Mapping.'
  const code = reason.problem?.code ?? ''
  if (code === 'drawing_mapping.page_capacity_reached') return 'Trang đã đạt giới hạn 500 Mapping active.'
  if (code === 'drawing_mapping.duplicate') return 'Rectangle này đã được map tới cùng Construction Object.'
  if (code === 'drawing_mapping.concurrency_conflict') return 'Mapping đã được người khác thay đổi. Dữ liệu mới nhất đã được tải lại.'
  if (code === 'construction_object.inactive') return 'Construction Object không còn active.'
  if (reason.status === 400) return 'Rectangle hoặc dữ liệu nhập không hợp lệ.'
  if (reason.status === 401) return 'Phiên đăng nhập đã hết hạn.'
  if (reason.status === 403) return 'Bạn không có quyền thực hiện thao tác này.'
  if (reason.status === 404) return 'Không tìm thấy Drawing, Mapping hoặc Construction Object.'
  if (reason.status === 409) return 'Dữ liệu đang xung đột; hãy tải lại và thử lại.'
  return 'Máy chủ không thể hoàn tất thao tác Mapping.'
}
