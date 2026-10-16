import { useEffect, useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import { ApiError, createFormTemplateVersion, deactivateFormTemplate, getFormTemplateVersion, getFormTemplateVersions, publishFormTemplateDraft, updateFormTemplateDraft } from '../api/client'
import { FormSchemaEditor } from '../components/FormSchemaEditor'
import { parseSchema, schemaByteCount, schemaLimits, serializeSchema, validateSchema, type FormSchema } from '../formTemplates/schema'
import { templateErrorMessage } from '../formTemplates/errors'

export function FormTemplateDetailPage() {
  const { id = '' } = useParams(); const [params, setParams] = useSearchParams(); const queryClient = useQueryClient()
  const requestedVersion = Number(params.get('version')) || 0
  const versions = useQuery({ queryKey: ['form-template-versions', id], queryFn: ({ signal }) => getFormTemplateVersions(id, signal), enabled: Boolean(id) })
  const selectedVersion = requestedVersion || versions.data?.template.draftVersionNumber || versions.data?.template.latestPublishedVersionNumber || versions.data?.versions.items[0]?.versionNumber || 0
  const detail = useQuery({ queryKey: ['form-template-version', id, selectedVersion], queryFn: ({ signal }) => getFormTemplateVersion(id, selectedVersion, signal), enabled: Boolean(id && selectedVersion) })
  const [editSchema, setEditSchema] = useState<FormSchema | null>(null); const [newDraftSchema, setNewDraftSchema] = useState<FormSchema | null>(null); const [notice, setNotice] = useState('')
  useEffect(() => { if (!requestedVersion && selectedVersion) setParams({ version: String(selectedVersion) }, { replace: true }) }, [requestedVersion, selectedVersion, setParams])
  const refresh = async () => { await queryClient.invalidateQueries({ queryKey: ['form-templates'] }); await queryClient.invalidateQueries({ queryKey: ['form-template-versions', id] }); await queryClient.invalidateQueries({ queryKey: ['form-template-version', id] }) }
  const update = useMutation({ mutationFn: (schema: FormSchema) => updateFormTemplateDraft(id, selectedVersion, serializeSchema(schema), detail.data!.rowVersion), onSuccess: async () => { setNotice('Đã lưu Draft.'); setEditSchema(null); await refresh() } })
  const publish = useMutation({ mutationFn: () => publishFormTemplateDraft(id, selectedVersion, versions.data!.template.rowVersion, detail.data!.rowVersion), onSuccess: async () => { setNotice('Đã publish version. Published version hiện ở chế độ chỉ đọc.'); await refresh() } })
  const createDraft = useMutation({ mutationFn: (schema: FormSchema) => createFormTemplateVersion(id, serializeSchema(schema), versions.data!.template.rowVersion), onSuccess: async result => { await refresh(); setNewDraftSchema(null); setParams({ version: String(result.versionNumber) }); setNotice(`Đã tạo Draft v${result.versionNumber}.`) } })
  const deactivate = useMutation({ mutationFn: () => deactivateFormTemplate(id, versions.data!.template.rowVersion), onSuccess: async () => { setNotice('Template đã được deactivate và không thể reactivate trong MVP.'); setEditSchema(null); setNewDraftSchema(null); await refresh() } })
  const parsed = useMemo(() => detail.data ? parseSchema(detail.data.schemaJson) : { errors: [] }, [detail.data])
  const mutationError = update.error || publish.error || createDraft.error || deactivate.error
  if (versions.isLoading) return <p className="state" role="status">Đang tải template…</p>
  if (versions.isError || !versions.data) return <p className="state error" role="alert">{templateErrorMessage(versions.error, 'Không thể tải template.')}</p>
  const template = versions.data.template; const active = template.status === 'Active'; const isDraft = detail.data?.status === 'Draft'
  const selectedMetadata = versions.data.versions.items.find(version => version.versionNumber === selectedVersion)
  const isValid = (schema: FormSchema | null) => Boolean(schema && !validateSchema(schema).length && schemaByteCount(schema) <= schemaLimits.bytes)
  const chooseVersion = (versionNumber: number) => { setEditSchema(null); setNewDraftSchema(null); setNotice(''); setParams({ version: String(versionNumber) }) }
  return <><Link className="back-link" to="/form-templates">← Danh sách template</Link>
    <header className="page-head"><div><p className="kicker">{template.code}</p><h1>{template.name}</h1><p>{template.description || 'Không có mô tả'}</p></div><span className={`status ${template.status.toLowerCase()}`}>{template.status}</span></header>
    <div className="template-detail-layout"><aside className="version-panel"><h2>Lịch sử version</h2><ul>{versions.data.versions.items.map(version => <li key={version.id}><button className={selectedVersion === version.versionNumber ? 'selected' : ''} onClick={() => chooseVersion(version.versionNumber)}><strong>v{version.versionNumber}</strong><span className={`status ${version.status.toLowerCase()}`}>{version.status}</span><small>{formatDate(version.publishedAt ?? version.updatedAt ?? version.createdAt)}</small></button></li>)}</ul>
      {active && !template.draftVersionNumber && !newDraftSchema && <button className="button" disabled={!detail.data} onClick={() => { setEditSchema(null); setNotice(''); setNewDraftSchema(parsed.schema ?? { components: [] }) }}>Tạo Draft version mới</button>}
      {active && <button className="button danger" disabled={deactivate.isPending} onClick={() => window.confirm('Sau khi deactivate sẽ không thể tạo, sửa hoặc publish version mới. Lịch sử vẫn được giữ và MVP không hỗ trợ reactivate. Bạn chắc chắn muốn tiếp tục?') && deactivate.mutate()}>Deactivate template</button>}
    </aside><main className="version-content">
      {notice && <p className="notice success" role="status">{notice}</p>}{mutationError && <div className="notice error" role="alert"><p>{templateErrorMessage(mutationError, 'Không thể hoàn tất thao tác.')}</p>{mutationError instanceof ApiError && mutationError.status === 409 && <button className="button" onClick={() => { setEditSchema(null); setNewDraftSchema(null); update.reset(); publish.reset(); createDraft.reset(); deactivate.reset(); void refresh() }}>Tải lại và bỏ thay đổi cục bộ</button>}</div>}
      {detail.isLoading && <p className="state" role="status">Đang tải version…</p>}{detail.isError && <p className="state error" role="alert">{templateErrorMessage(detail.error, 'Không thể tải version.')}</p>}
      {newDraftSchema && <section><div className="section-title"><div><p className="kicker">New version</p><h2>Tạo Draft mới</h2></div></div><p>Version number sẽ do backend cấp từ bộ đếm của template.</p><FormSchemaEditor value={newDraftSchema} onChange={setNewDraftSchema} /><div className="form-actions"><button className="button" onClick={() => setNewDraftSchema(null)}>Hủy</button><button className="button primary" disabled={!isValid(newDraftSchema) || createDraft.isPending} onClick={() => createDraft.mutate(newDraftSchema)}>Tạo Draft</button></div></section>}
      {!newDraftSchema && detail.data && <section><div className="version-heading"><div><p className="kicker">Version {detail.data.versionNumber}</p><h2>{detail.data.status}</h2></div><dl><div><dt>Created</dt><dd>{formatDate(detail.data.createdAt)}</dd></div><div><dt>Updated</dt><dd>{formatDate(detail.data.updatedAt)}</dd></div><div><dt>Published</dt><dd>{formatDate(detail.data.publishedAt)}</dd></div><div><dt>Referenced</dt><dd>{selectedMetadata?.isReferenced ? 'Có' : 'Không'}</dd></div></dl></div>
        {parsed.schema ? <FormSchemaEditor value={editSchema ?? parsed.schema} onChange={setEditSchema} disabled={!editSchema} /> : <p className="notice error" role="alert">Schema lưu trên server không thể hiển thị an toàn: {parsed.errors.join(' ')}</p>}
        {active && isDraft && parsed.schema && <div className="form-actions">{editSchema ? <><button className="button" onClick={() => setEditSchema(null)}>Hủy thay đổi</button><button className="button primary" disabled={!isValid(editSchema) || update.isPending} onClick={() => update.mutate(editSchema)}>Lưu Draft</button></> : <button className="button" onClick={() => setEditSchema(parsed.schema!)}>Chỉnh sửa Draft</button>}<button className="button primary" disabled={Boolean(editSchema) || publish.isPending} onClick={() => window.confirm('Published version sẽ bất biến. Bạn chắc chắn muốn publish?') && publish.mutate()}>Publish Draft</button></div>}
        {(!active || detail.data.status === 'Published') && <p className="read-only-note">Version này chỉ đọc.</p>}
      </section>}
    </main></div></>
}

function formatDate(value?: string | null) { return value ? new Intl.DateTimeFormat('vi-VN', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value)) : '—' }
