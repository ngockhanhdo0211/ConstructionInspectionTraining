import { useMemo, useRef, useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { Link, useParams } from 'react-router-dom'
import { ApiError, getInspection, saveInspectionDraft } from '../api/client'
import type { InspectionDetail } from '../api/types'
import { useAuth } from '../auth/AuthContext'
import { DynamicInspectionForm } from '../components/DynamicInspectionForm'
import { parseSchema, type FormSchema } from '../formTemplates/schema'
import { canEditInspection } from '../inspections/access'
import { parseDraftData, serializeDraft, validateDraftTypes, type DraftValues } from '../inspections/draft'
import { inspectionErrorMessage } from '../inspections/errors'
import { useUnsavedChanges } from '../inspections/useUnsavedChanges'

export function InspectionDetailPage() {
  const { id = '' } = useParams()
  const detail = useQuery({ queryKey: ['inspection', id], queryFn: ({ signal }) => getInspection(id, signal), enabled: Boolean(id), retry: false })
  if (detail.isLoading) return <p className="state" role="status">Đang tải Inspection…</p>
  if (detail.isError || !detail.data) return <div><Link className="back-link" to="/inspections">← Danh sách Inspection</Link><p className="state error" role="alert">{inspectionErrorMessage(detail.error, 'Không thể tải Inspection.')}</p></div>
  return <InspectionDraftContent key={`${detail.data.id}:${detail.data.currentRevision.rowVersion}`} item={detail.data} reloadServer={() => detail.refetch().then(() => undefined)} />
}

function InspectionDraftContent({ item, reloadServer }: { item: InspectionDetail; reloadServer: () => Promise<void> }) {
  const { user } = useAuth(); const summaryRef = useRef<HTMLDivElement>(null)
  const schemaResult = useMemo(() => parseSchema(item.template.schemaJson), [item.template.schemaJson])
  const dataResult = useMemo(() => schemaResult.schema ? parseDraftData(item.currentRevision.dataJson, schemaResult.schema) : {}, [item.currentRevision.dataJson, schemaResult.schema])
  if (!schemaResult.schema || !dataResult.values) return <><Link className="back-link" to="/inspections">← Danh sách Inspection</Link><header className="page-head"><div><p className="kicker">{item.number}</p><h1>{item.title}</h1></div></header><p className="notice error" role="alert">{schemaResult.errors.length ? `Pinned schema không thể hiển thị an toàn: ${schemaResult.errors.join(' ')}` : dataResult.error}</p></>
  return <InspectionDraftForm item={item} schema={schemaResult.schema} initialValues={dataResult.values} reloadServer={reloadServer} summaryRef={summaryRef} canEdit={canEditInspection(user, item.createdByUserId, item.status)} />
}

function InspectionDraftForm({ item, schema, initialValues, reloadServer, summaryRef, canEdit }: { item: InspectionDetail; schema: FormSchema; initialValues: DraftValues; reloadServer: () => Promise<void>; summaryRef: React.RefObject<HTMLDivElement | null>; canEdit: boolean }) {
  const [values, setValues] = useState<DraftValues>(initialValues)
  const [baseline, setBaseline] = useState(() => serializeDraft(schema, initialValues))
  const [notice, setNotice] = useState(''); const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({})
  const [versions, setVersions] = useState({ inspection: item.rowVersion, submission: item.currentRevision.rowVersion, process: item.processRowVersion })
  const [updatedAt, setUpdatedAt] = useState(item.updatedAt)
  const dirty = serializeDraft(schema, values) !== baseline
  useUnsavedChanges(dirty)
  const save = useMutation({
    mutationFn: () => saveInspectionDraft(item.id, { data: values, inspectionRowVersion: versions.inspection, submissionRowVersion: versions.submission, processRowVersion: versions.process }),
    onSuccess: result => {
      const parsed = parseDraftData(result.dataJson, schema); const savedValues = parsed.values ?? values
      setValues(savedValues); setBaseline(serializeDraft(schema, savedValues)); setVersions({ inspection: result.inspectionRowVersion, submission: result.submissionRowVersion, process: result.processRowVersion }); setUpdatedAt(result.updatedAt); setFieldErrors({}); setNotice('Đã lưu Draft. Ba rowVersion đã được cập nhật từ máy chủ.')
    },
    onError: () => { window.setTimeout(() => summaryRef.current?.focus(), 0) },
  })
  const submitSave = () => {
    if (!canEdit || save.isPending) return
    const errors = validateDraftTypes(schema, values); setFieldErrors(errors); setNotice('')
    if (Object.keys(errors).length) { window.setTimeout(() => document.getElementById(`inspection-${Object.keys(errors)[0]}`)?.focus(), 0); return }
    save.mutate()
  }
  const reload = async () => {
    if (dirty && !window.confirm('Tải lại sẽ bỏ toàn bộ thay đổi cục bộ chưa lưu. Bạn có chắc chắn?')) return
    save.reset(); setNotice(''); await reloadServer()
  }
  const conflict = save.error instanceof ApiError && save.error.status === 409
  return <><Link className="back-link" to="/inspections">← Danh sách Inspection</Link><header className="page-head"><div><p className="kicker">{item.number}</p><h1>{item.title}</h1><p>{item.description || 'Không có mô tả'}</p></div><span className={`status ${item.status.toLowerCase()}`}>{item.status}</span></header>
    <dl className="inspection-summary"><div><dt>Construction Object</dt><dd><strong>{item.constructionObjectCode}</strong> — {item.constructionObjectName}</dd></div><div><dt>Pinned template</dt><dd><strong>{item.template.code}</strong> — {item.template.name} · v{item.template.versionNumber}</dd></div><div><dt>Revision</dt><dd>{item.currentRevision.revisionNumber}</dd></div><div><dt>Created</dt><dd>{formatDate(item.createdAt)}</dd></div><div><dt>Updated</dt><dd>{updatedAt ? formatDate(updatedAt) : 'Chưa cập nhật'}</dd></div></dl>
    {notice && <p className="notice success" role="status">{notice}</p>}
    {save.error && <div className="notice error" role="alert" tabIndex={-1} ref={summaryRef}><p>{inspectionErrorMessage(save.error, 'Không thể lưu Draft.')}</p>{conflict && <button className="button" onClick={() => void reload()}>Tải lại bản máy chủ</button>}</div>}
    <section className="inspection-draft"><div className="section-title"><div><h2>Draft revision {item.currentRevision.revisionNumber}</h2><p>{canEdit ? 'Có thể lưu khi chưa điền đủ field bắt buộc. Không có thao tác Submit trong checkpoint này.' : 'Hồ sơ đang ở chế độ chỉ đọc.'}</p></div>{dirty && <span className="status draft">Chưa lưu</span>}</div><DynamicInspectionForm schema={schema} values={values} onChange={setValues} readOnly={!canEdit} errors={fieldErrors} />
      {canEdit && <div className="form-actions"><button className="button" type="button" disabled={save.isPending} onClick={() => void reload()}>Tải lại</button><button className="button primary" type="button" disabled={save.isPending || !dirty} onClick={submitSave}>{save.isPending ? 'Đang lưu…' : 'Lưu Draft'}</button></div>}
    </section>
  </>
}

function formatDate(value: string) { return new Intl.DateTimeFormat('vi-VN', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value)) }
