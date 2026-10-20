import { useEffect, useMemo, useRef, useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { Link, useParams } from 'react-router-dom'
import { ApiError, approveInspection, getInspection, getInspectionHistory, getInspectionReviewerOptions, getInspectionRevision, getInspectionRevisions, reassignInspectionReviewer, rejectInspection, requestInspectionRecheck, saveInspectionDraft, startInspectionReview, submitInspection } from '../api/client'
import type { InspectionDetail, InspectionStatus, InspectionWorkflowResult, PagedResult, ProcessHistory, InspectionRevisionListItem, InspectionRevisionDetail, ReviewerOption } from '../api/types'
import { useAuth } from '../auth/AuthContext'
import { DynamicInspectionForm } from '../components/DynamicInspectionForm'
import { parseSchema, type FormSchema } from '../formTemplates/schema'
import { canEditInspection, canReassignInspection, isAssignedReviewer } from '../inspections/access'
import { parseDraftData, serializeDraft, validateDraftTypes, validateRequired, type DraftValues } from '../inspections/draft'
import { inspectionErrorMessage } from '../inspections/errors'
import { useUnsavedChanges } from '../inspections/useUnsavedChanges'

type DialogAction = 'submit' | 'approve' | 'reject' | 'recheck' | 'reassign'
type Versions = { inspection: string; submission: string; process: string }

export function InspectionDetailPage() {
  const { id = '' } = useParams()
  const detail = useQuery({ queryKey: ['inspection', id], queryFn: ({ signal }) => getInspection(id, signal), enabled: Boolean(id), retry: false })
  if (detail.isLoading) return <p className="state" role="status">Đang tải Inspection…</p>
  if (detail.isError || !detail.data) return <div><Link className="back-link" to="/inspections">← Danh sách Inspection</Link><p className="state error" role="alert">{inspectionErrorMessage(detail.error, 'Không thể tải Inspection.')}</p></div>
  return <InspectionContent key={`${detail.data.id}:${detail.data.rowVersion}:${detail.data.processRowVersion}`} item={detail.data} reload={() => detail.refetch().then(() => undefined)} />
}

function InspectionContent({ item, reload }: { item: InspectionDetail; reload: () => Promise<void> }) {
  const { user } = useAuth()
  const schemaResult = useMemo(() => parseSchema(item.template.schemaJson), [item.template.schemaJson])
  const dataResult = useMemo(() => schemaResult.schema ? parseDraftData(item.currentRevision.dataJson, schemaResult.schema) : {}, [item.currentRevision.dataJson, schemaResult.schema])
  if (!schemaResult.schema || !dataResult.values) return <><Link className="back-link" to="/inspections">← Danh sách Inspection</Link><p className="notice error" role="alert">Pinned schema không thể hiển thị an toàn: {schemaResult.errors.join(' ') || dataResult.error}</p></>
  return <WorkflowForm item={item} schema={schemaResult.schema} initialValues={dataResult.values} reloadServer={reload} user={user} />
}

function WorkflowForm({ item, schema, initialValues, reloadServer, user }: { item: InspectionDetail; schema: FormSchema; initialValues: DraftValues; reloadServer: () => Promise<void>; user: ReturnType<typeof useAuth>['user'] }) {
  const errorRef = useRef<HTMLDivElement>(null)
  const [values, setValues] = useState(initialValues)
  const [baseline, setBaseline] = useState(() => serializeDraft(schema, initialValues))
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({})
  const [versions, setVersions] = useState<Versions>({ inspection: item.rowVersion, submission: item.currentRevision.rowVersion, process: item.processRowVersion })
  const [status, setStatus] = useState<InspectionStatus>(item.status)
  const [reviewer, setReviewer] = useState(item.reviewerUserId ?? null)
  const [revision, setRevision] = useState(item.currentRevision.revisionNumber)
  const [updatedAt, setUpdatedAt] = useState(item.updatedAt)
  const [notice, setNotice] = useState('')
  const [dialog, setDialog] = useState<DialogAction | null>(null)
  const [reason, setReason] = useState('')
  const [selectedReviewer, setSelectedReviewer] = useState('')
  const [reviewerSearch, setReviewerSearch] = useState('')
  const [historyPage, setHistoryPage] = useState(1)
  const [revisionPage, setRevisionPage] = useState(1)
  const [selectedRevision, setSelectedRevision] = useState<number | null>(null)
  const dirty = serializeDraft(schema, values) !== baseline
  const editable = canEditInspection(user, item.createdByUserId, status)
  useUnsavedChanges(dirty)

  const history = useQuery({ queryKey: ['inspection-history', item.id, historyPage], queryFn: ({ signal }) => getInspectionHistory(item.id, historyPage, 10, signal), retry: false })
  const revisions = useQuery({ queryKey: ['inspection-revisions', item.id, revisionPage], queryFn: ({ signal }) => getInspectionRevisions(item.id, revisionPage, 10, signal), retry: false })
  const revisionDetail = useQuery({ queryKey: ['inspection-revision', item.id, selectedRevision], queryFn: ({ signal }) => getInspectionRevision(item.id, selectedRevision!, signal), enabled: selectedRevision !== null, retry: false })
  const reviewers = useQuery({ queryKey: ['inspection-reviewers', reviewerSearch], queryFn: ({ signal }) => getInspectionReviewerOptions(1, 50, reviewerSearch.trim(), signal), enabled: dialog === 'submit' || dialog === 'reassign', retry: false })
  const refresh = () => { void history.refetch(); void revisions.refetch() }
  const applyResult = (result: InspectionWorkflowResult, message: string) => {
    setStatus(result.status); setReviewer(result.reviewerUserId ?? null); setRevision(result.currentRevisionNumber)
    setVersions({ inspection: result.inspectionRowVersion, submission: result.submissionRowVersion, process: result.processRowVersion })
    setDialog(null); setReason(''); setSelectedReviewer(''); setNotice(message); refresh()
  }
  const workflow = useMutation({
    mutationFn: (action: DialogAction | 'start') => {
      if (action === 'submit') return submitInspection(item.id, reviewer ?? selectedReviewer, versions.inspection, versions.submission, versions.process)
      if (action === 'start') return startInspectionReview(item.id, versions.process)
      if (action === 'approve') return approveInspection(item.id, reason.trim() || null, versions.process)
      if (action === 'reject') return rejectInspection(item.id, reason.trim(), versions.process)
      if (action === 'recheck') return requestInspectionRecheck(item.id, reason.trim(), versions.process)
      return reassignInspectionReviewer(item.id, selectedReviewer, reason.trim(), versions.inspection, versions.process)
    },
    onSuccess: (result, action) => applyResult(result, successMessage(action)),
    onError: () => window.setTimeout(() => errorRef.current?.focus(), 0),
  })
  const save = useMutation({
    mutationFn: () => saveInspectionDraft(item.id, { data: values, inspectionRowVersion: versions.inspection, submissionRowVersion: versions.submission, processRowVersion: versions.process }),
    onSuccess: result => {
      const saved = parseDraftData(result.dataJson, schema).values ?? values
      const createdRevision = result.revisionNumber !== revision
      setValues(saved); setBaseline(serializeDraft(schema, saved)); setRevision(result.revisionNumber); setUpdatedAt(result.updatedAt)
      setVersions({ inspection: result.inspectionRowVersion, submission: result.submissionRowVersion, process: result.processRowVersion })
      setFieldErrors({}); setNotice(createdRevision ? `Đã tạo và lưu revision ${result.revisionNumber}. Ba rowVersion đã được cập nhật từ máy chủ.` : `Đã lưu revision ${result.revisionNumber}. Ba rowVersion đã được cập nhật từ máy chủ.`); refresh()
    },
    onError: () => window.setTimeout(() => errorRef.current?.focus(), 0),
  })
  const saveDraft = () => {
    const errors = validateDraftTypes(schema, values); setFieldErrors(errors); setNotice('')
    if (Object.keys(errors).length) return focusFirst(errors)
    save.mutate()
  }
  const showDialog = (action: DialogAction) => { workflow.reset(); setReason(''); setSelectedReviewer(''); setDialog(action) }
  const openSubmit = () => {
    if (dirty) { setNotice('Hãy lưu bản nháp trước khi gửi kiểm tra.'); return }
    const errors = validateRequired(schema, values); setFieldErrors(errors)
    if (Object.keys(errors).length) { setNotice('Hãy hoàn thành các trường bắt buộc trước khi Submit.'); return focusFirst(errors) }
    showDialog('submit')
  }
  const reload = async () => {
    if (dirty && !window.confirm('Tải lại sẽ bỏ toàn bộ thay đổi cục bộ chưa lưu. Bạn có chắc chắn?')) return
    save.reset(); workflow.reset(); setNotice(''); await reloadServer()
  }
  const error = workflow.error || save.error
  const conflict = error instanceof ApiError && error.status === 409
  const busy = save.isPending || workflow.isPending
  const assignedReviewer = isAssignedReviewer(user, reviewer)

  return <><Link className="back-link" to="/inspections">← Danh sách Inspection</Link>
    <header className="page-head"><div><p className="kicker">{item.number}</p><h1>{item.title}</h1><p>{item.description || 'Không có mô tả'}</p></div><span className={`status ${status.toLowerCase()}`}>{status}</span></header>
    <dl className="inspection-summary"><div><dt>Construction Object</dt><dd><strong>{item.constructionObjectCode}</strong> — {item.constructionObjectName}</dd></div><div><dt>Pinned template</dt><dd><strong>{item.template.code}</strong> — {item.template.name} · v{item.template.versionNumber}</dd></div><div><dt>Revision hiện tại</dt><dd>{revision}</dd></div><div><dt>Reviewer</dt><dd>{reviewer ? 'Đã phân công' : 'Chưa phân công'}</dd></div><div><dt>Created</dt><dd>{formatDate(item.createdAt)}</dd></div><div><dt>Updated</dt><dd>{updatedAt ? formatDate(updatedAt) : 'Chưa cập nhật'}</dd></div></dl>
    {notice && <p className="notice success" role="status">{notice}</p>}
    {error && (!dialog || Boolean(save.error)) && <div className="notice error" role="alert" tabIndex={-1} ref={errorRef}><p>{inspectionErrorMessage(error, 'Không thể hoàn tất thao tác.')}</p>{conflict && <button className="button" onClick={() => void reload()}>Tải lại bản máy chủ</button>}</div>}
    <section className="inspection-draft"><div className="section-title"><div><h2>Revision {revision}</h2><p>{editable ? status === 'Recheck' ? 'Lần lưu đầu tiên sẽ tạo một revision mới.' : 'Draft có thể lưu dù thiếu trường bắt buộc.' : 'Hồ sơ đang ở chế độ chỉ đọc.'}</p></div>{dirty && <span className="status draft">Chưa lưu</span>}</div>
      <DynamicInspectionForm schema={schema} values={values} onChange={setValues} readOnly={!editable} errors={fieldErrors} />
      <div className="workflow-actions">{editable && <><button className="button" disabled={busy} onClick={() => void reload()}>Tải lại</button><button className="button" disabled={busy || !dirty} onClick={saveDraft}>{save.isPending ? 'Đang lưu…' : status === 'Recheck' ? 'Lưu' : 'Lưu Draft'}</button><button className="button primary" disabled={busy || dirty} title={dirty ? 'Hãy lưu bản nháp trước khi gửi kiểm tra.' : undefined} onClick={openSubmit}>{status === 'Recheck' ? 'Gửi lại' : 'Submit'}</button></>}
        {assignedReviewer && status === 'Submitted' && <button className="button primary" disabled={busy} onClick={() => { if (window.confirm('Bắt đầu xử lý Inspection này?')) workflow.mutate('start') }}>Start Review</button>}
        {assignedReviewer && status === 'UnderReview' && <><button className="button primary" disabled={busy} onClick={() => showDialog('approve')}>Approve</button><button className="button danger" disabled={busy} onClick={() => showDialog('reject')}>Reject</button><button className="button" disabled={busy} onClick={() => showDialog('recheck')}>Request Recheck</button></>}
        {canReassignInspection(user, status) && <button className="button" disabled={busy} onClick={() => showDialog('reassign')}>Reassign Reviewer</button>}</div>
    </section>
    <HistoryPanel result={history.data} loading={history.isLoading} error={history.error} page={historyPage} setPage={setHistoryPage} fetching={history.isFetching} />
    <RevisionPanel result={revisions.data} loading={revisions.isLoading} error={revisions.error} page={revisionPage} setPage={setRevisionPage} fetching={revisions.isFetching} selected={selectedRevision} select={setSelectedRevision} detail={revisionDetail.data} detailLoading={revisionDetail.isLoading} detailError={revisionDetail.error} />
    {dialog && <WorkflowDialog action={dialog} pending={workflow.isPending} error={workflow.error} reviewerSearch={reviewerSearch} setReviewerSearch={setReviewerSearch} selectedReviewer={selectedReviewer} setSelectedReviewer={setSelectedReviewer} options={reviewers.data?.items ?? []} currentReviewer={reviewer} reason={reason} setReason={setReason} close={() => { if (!workflow.isPending) { setDialog(null); setReason(''); setSelectedReviewer(''); workflow.reset() } }} submit={() => workflow.mutate(dialog)} reload={() => void reload()} />}
  </>
}

function WorkflowDialog({ action, pending, error, reviewerSearch, setReviewerSearch, selectedReviewer, setSelectedReviewer, options, currentReviewer, reason, setReason, close, submit, reload }: { action: DialogAction; pending: boolean; error: Error | null; reviewerSearch: string; setReviewerSearch: (v: string) => void; selectedReviewer: string; setSelectedReviewer: (v: string) => void; options: ReviewerOption[]; currentReviewer: string | null; reason: string; setReason: (v: string) => void; close: () => void; submit: () => void; reload: () => void }) {
  const heading = useRef<HTMLHeadingElement>(null); const modal = useRef<HTMLElement>(null)
  const pendingRef = useRef(pending); const closeRef = useRef(close)
  useEffect(() => { pendingRef.current = pending; closeRef.current = close }, [pending, close])
  useEffect(() => { const opener = document.activeElement as HTMLElement | null; heading.current?.focus(); const listener = (event: KeyboardEvent) => { if (event.key === 'Escape' && !pendingRef.current) closeRef.current(); if (event.key === 'Tab') { const focusable = [...(modal.current?.querySelectorAll<HTMLElement>('button:not(:disabled), input:not(:disabled), select:not(:disabled), textarea:not(:disabled), [tabindex="0"]') ?? [])]; if (!focusable.length) return; const first = focusable[0]; const last = focusable.at(-1)!; if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus() } else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus() } } }; window.addEventListener('keydown', listener); return () => { window.removeEventListener('keydown', listener); opener?.focus() } }, [])
  const needsReviewer = action === 'submit' || action === 'reassign'; const needsReason = action === 'reject' || action === 'recheck' || action === 'reassign'
  const reviewerAlreadyAssigned = action === 'submit' && Boolean(currentReviewer)
  const valid = (reviewerAlreadyAssigned || !needsReviewer || Boolean(selectedReviewer)) && (!needsReason || Boolean(reason.trim()))
  const conflict = error instanceof ApiError && error.status === 409
  return <div className="modal-backdrop"><section className="workflow-modal" ref={modal} role="dialog" aria-modal="true" aria-labelledby="workflow-heading"><h2 id="workflow-heading" ref={heading} tabIndex={-1}>{dialogTitle(action)}</h2>
    {action === 'approve' && <p>Approve là trạng thái kết thúc. Hồ sơ sẽ chuyển sang chỉ đọc.</p>}{action === 'reject' && <p>Rejected là trạng thái kết thúc và không thể mở lại.</p>}{action === 'recheck' && <p>Inspector có thể tạo revision mới, chỉnh sửa và gửi lại.</p>}{reviewerAlreadyAssigned && <p>Hồ sơ sẽ được gửi lại cho <strong>{options.find(option => option.userId === currentReviewer)?.displayName ?? 'Reviewer hiện tại'}</strong>.</p>}
    {needsReviewer && !reviewerAlreadyAssigned && <><label htmlFor="reviewer-search">Tìm Reviewer</label><input id="reviewer-search" value={reviewerSearch} maxLength={250} onChange={event => setReviewerSearch(event.target.value)} /><label htmlFor="reviewer-select">{action === 'reassign' ? 'Reviewer mới *' : 'Reviewer *'}</label><select id="reviewer-select" value={selectedReviewer} onChange={event => setSelectedReviewer(event.target.value)}><option value="">Chọn Reviewer</option>{options.filter(option => option.userId !== currentReviewer).map(option => <option key={option.userId} value={option.userId}>{option.displayName}</option>)}</select></>}
    {(needsReason || action === 'approve') && <><label htmlFor="workflow-reason">{action === 'approve' ? 'Comment (không bắt buộc)' : 'Lý do *'}</label><textarea id="workflow-reason" maxLength={2000} value={reason} aria-invalid={needsReason && !reason.trim()} onChange={event => setReason(event.target.value)} /></>}
    {error && <div className="notice error" role="alert"><p>{inspectionErrorMessage(error, 'Không thể hoàn tất thao tác.')}</p>{conflict && <button className="button" onClick={reload}>Tải lại dữ liệu mới nhất</button>}</div>}
    <div className="form-actions"><button className="button" disabled={pending} onClick={close}>Hủy</button><button className="button primary" disabled={pending || !valid} onClick={submit}>{pending ? 'Đang xử lý…' : 'Xác nhận'}</button></div></section></div>
}

function HistoryPanel({ result, loading, error, page, setPage, fetching }: { result?: PagedResult<ProcessHistory>; loading: boolean; error: Error | null; page: number; setPage: React.Dispatch<React.SetStateAction<number>>; fetching: boolean }) {
  return <section className="workflow-panel"><div className="section-title"><h2>Lịch sử xử lý</h2></div>{loading && <p className="state" role="status">Đang tải lịch sử…</p>}{error && <p className="state error" role="alert">{inspectionErrorMessage(error, 'Không thể tải lịch sử.')}</p>}{result?.items.length === 0 && <p className="state">Chưa có lịch sử xử lý.</p>}<ol className="history-list">{result?.items.map(entry => <li key={entry.id}><div><strong>{actionLabel(entry.action)}</strong><span>{entry.fromStatus ? `${entry.fromStatus} → ${entry.toStatus}` : entry.toStatus}</span></div><p>{formatDate(entry.createdAt)} · {entry.actorDisplayName}</p>{entry.reason && <blockquote>{entry.reason}</blockquote>}{(entry.previousReviewerDisplayName || entry.newReviewerDisplayName) && <p>Reviewer: {entry.previousReviewerDisplayName ?? 'Chưa gán'} → {entry.newReviewerDisplayName ?? 'Chưa gán'}</p>}</li>)}</ol>{result && result.totalPages > 1 && <PageNav page={page} total={result.totalPages} disabled={fetching} setPage={setPage} label="lịch sử" />}</section>
}

function RevisionPanel({ result, loading, error, page, setPage, fetching, selected, select, detail, detailLoading, detailError }: { result?: PagedResult<InspectionRevisionListItem>; loading: boolean; error: Error | null; page: number; setPage: React.Dispatch<React.SetStateAction<number>>; fetching: boolean; selected: number | null; select: (v: number | null) => void; detail?: InspectionRevisionDetail; detailLoading: boolean; detailError: Error | null }) {
  const schema = useMemo(() => detail ? parseSchema(detail.template.schemaJson).schema : undefined, [detail]); const data = useMemo(() => detail && schema ? parseDraftData(detail.dataJson, schema).values : undefined, [detail, schema])
  return <section className="workflow-panel"><div className="section-title"><h2>Phiên bản</h2>{selected !== null && <button className="button" onClick={() => select(null)}>Quay lại revision hiện tại</button>}</div>{loading && <p className="state" role="status">Đang tải phiên bản…</p>}{error && <p className="state error" role="alert">{inspectionErrorMessage(error, 'Không thể tải phiên bản.')}</p>}<ul className="revision-list">{result?.items.map(entry => <li key={entry.id}><button className={selected === entry.revisionNumber ? 'selected' : ''} onClick={() => select(entry.revisionNumber)}><strong>Revision {entry.revisionNumber}</strong><span>{entry.isCurrent ? 'Hiện tại' : entry.isImmutable ? 'Đã Submit · chỉ đọc' : 'Draft'}</span><small>{formatDate(entry.updatedAt ?? entry.createdAt)}</small></button></li>)}</ul>{result && result.totalPages > 1 && <PageNav page={page} total={result.totalPages} disabled={fetching} setPage={setPage} label="phiên bản" />}{selected !== null && detailLoading && <p className="state" role="status">Đang tải chi tiết revision…</p>}{detailError && <p className="state error" role="alert">{inspectionErrorMessage(detailError, 'Không tìm thấy revision được yêu cầu.')}</p>}{detail && schema && data && <div className="historical-revision"><h3>Revision {detail.revisionNumber} · chỉ đọc</h3><DynamicInspectionForm schema={schema} values={data} onChange={() => undefined} readOnly /></div>}</section>
}

function PageNav({ page, total, disabled, setPage, label }: { page: number; total: number; disabled: boolean; setPage: React.Dispatch<React.SetStateAction<number>>; label: string }) { return <nav className="pagination" aria-label={`Phân trang ${label}`}><button className="button" disabled={page <= 1 || disabled} onClick={() => setPage(v => v - 1)}>Trang trước</button><span>Trang {page} / {total}</span><button className="button" disabled={page >= total || disabled} onClick={() => setPage(v => v + 1)}>Trang sau</button></nav> }
function focusFirst(errors: Record<string, string>) { window.setTimeout(() => document.getElementById(`inspection-${Object.keys(errors)[0]}`)?.focus(), 0) }
function dialogTitle(action: DialogAction) { return ({ submit: 'Xác nhận Submit', approve: 'Xác nhận Approve', reject: 'Reject Inspection', recheck: 'Request Recheck', reassign: 'Reassign Reviewer' } as const)[action] }
function successMessage(action: DialogAction | 'start') { return ({ submit: 'Đã gửi Inspection để kiểm tra.', start: 'Đã bắt đầu review.', approve: 'Inspection đã được Approve.', reject: 'Inspection đã bị Reject.', recheck: 'Đã yêu cầu Inspector kiểm tra lại.', reassign: 'Đã chuyển Inspection cho Reviewer mới.' } as const)[action] }
function actionLabel(action: string) { return ({ Create: 'Tạo Inspection', Submit: 'Submit', StartReview: 'Start Review', Approve: 'Approve', Reject: 'Reject', RequestRecheck: 'Request Recheck', ReassignReviewer: 'Reassign Reviewer', CreateRevision: 'Tạo revision' } as Record<string, string>)[action] ?? action }
function formatDate(value: string) { return new Intl.DateTimeFormat('vi-VN', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value)) }
