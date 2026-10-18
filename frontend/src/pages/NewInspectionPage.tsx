import { useRef, useState } from 'react'
import { keepPreviousData, useMutation, useQuery } from '@tanstack/react-query'
import { Link, Navigate, useNavigate } from 'react-router-dom'
import { createInspection, getInspectionConstructionObjects, getInspectionFormTemplateOptions } from '../api/client'
import { useAuth } from '../auth/AuthContext'
import { canCreateInspection } from '../inspections/access'
import { inspectionErrorMessage } from '../inspections/errors'

const pageSize = 10

export function NewInspectionPage() {
  const { user } = useAuth(); const navigate = useNavigate(); const summaryRef = useRef<HTMLDivElement>(null)
  const [objectSearchInput, setObjectSearchInput] = useState(''); const [objectSearch, setObjectSearch] = useState(''); const [objectPage, setObjectPage] = useState(1)
  const [templateSearchInput, setTemplateSearchInput] = useState(''); const [templateSearch, setTemplateSearch] = useState(''); const [templatePage, setTemplatePage] = useState(1)
  const [constructionObjectId, setConstructionObjectId] = useState(''); const [formTemplateId, setFormTemplateId] = useState('')
  const [title, setTitle] = useState(''); const [description, setDescription] = useState(''); const [localError, setLocalError] = useState('')
  const allowed = canCreateInspection(user)
  const objects = useQuery({ queryKey: ['inspection-object-options', objectPage, objectSearch], queryFn: ({ signal }) => getInspectionConstructionObjects(objectPage, pageSize, objectSearch, signal), enabled: allowed, placeholderData: keepPreviousData })
  const templates = useQuery({ queryKey: ['inspection-template-options', templatePage, templateSearch], queryFn: ({ signal }) => getInspectionFormTemplateOptions(templatePage, pageSize, templateSearch, signal), enabled: allowed, placeholderData: keepPreviousData })
  const create = useMutation({ mutationFn: () => createInspection({ constructionObjectId, formTemplateId, title: title.trim(), description: description.trim() || null, data: {} }), onSuccess: detail => navigate(`/inspections/${detail.id}`, { replace: true }), onError: () => { window.setTimeout(() => summaryRef.current?.focus(), 0) } })
  if (!allowed) return <Navigate to="/inspections" replace />
  const submit = (event: React.FormEvent) => {
    event.preventDefault(); setLocalError('')
    if (!constructionObjectId || !formTemplateId || !title.trim()) { setLocalError('Hãy chọn Construction Object, Form Template và nhập tiêu đề.'); window.setTimeout(() => summaryRef.current?.focus(), 0); return }
    if (title.trim().length > 250 || description.trim().length > 2000) { setLocalError('Tiêu đề tối đa 250 ký tự và mô tả tối đa 2.000 ký tự.'); window.setTimeout(() => summaryRef.current?.focus(), 0); return }
    if (!create.isPending) create.mutate()
  }
  return <><Link className="back-link" to="/inspections">← Danh sách Inspection</Link><header className="page-head"><div><p className="kicker">New draft</p><h1>Tạo Inspection</h1><p>Server sẽ cấp Inspection Number và pin latest Published version của template đã chọn.</p></div></header>
    <form className="inspection-create" onSubmit={submit} noValidate>
      {(localError || create.error) && <div className="notice error" role="alert" tabIndex={-1} ref={summaryRef}>{localError || inspectionErrorMessage(create.error, 'Không thể tạo Inspection.')}</div>}
      <LookupSection title="Construction Object Active" searchLabel="Tìm object theo code hoặc tên" search={objectSearchInput} setSearch={setObjectSearchInput} apply={() => { setObjectPage(1); setObjectSearch(objectSearchInput.trim()) }} loading={objects.isLoading} error={objects.isError ? inspectionErrorMessage(objects.error, 'Không thể tải Construction Object.') : ''}>
        <label htmlFor="construction-object">Construction Object<select id="construction-object" value={constructionObjectId} onChange={event => setConstructionObjectId(event.target.value)}><option value="">Chọn object Active</option>{objects.data?.items.map(item => <option key={item.id} value={item.id}>{item.code} — {item.name} ({item.objectType})</option>)}</select></label>
        <Pager page={objectPage} totalPages={objects.data?.totalPages ?? 0} busy={objects.isFetching} setPage={setObjectPage} />
      </LookupSection>
      <LookupSection title="Form Template Published" searchLabel="Tìm template theo code hoặc tên" search={templateSearchInput} setSearch={setTemplateSearchInput} apply={() => { setTemplatePage(1); setTemplateSearch(templateSearchInput.trim()) }} loading={templates.isLoading} error={templates.isError ? inspectionErrorMessage(templates.error, 'Không thể tải Form Template.') : ''}>
        <label htmlFor="form-template">Form Template<select id="form-template" value={formTemplateId} onChange={event => setFormTemplateId(event.target.value)}><option value="">Chọn template khả dụng</option>{templates.data?.items.map(item => <option key={item.formTemplateId} value={item.formTemplateId}>{item.code} — {item.name} · Published v{item.latestPublishedVersionNumber}</option>)}</select></label>
        <Pager page={templatePage} totalPages={templates.data?.totalPages ?? 0} busy={templates.isFetching} setPage={setTemplatePage} />
      </LookupSection>
      <section className="inspection-metadata"><h2>Thông tin hồ sơ</h2><label htmlFor="inspection-title">Tiêu đề *<input id="inspection-title" required maxLength={250} value={title} onChange={event => setTitle(event.target.value)} /></label><label htmlFor="inspection-description">Mô tả<textarea id="inspection-description" maxLength={2000} rows={5} value={description} onChange={event => setDescription(event.target.value)} /></label><p className="read-only-note">Draft ban đầu được tạo với DataJson rỗng. Bạn sẽ điền biểu mẫu từ pinned schema ở màn hình chi tiết.</p></section>
      <div className="form-actions"><Link className="button action-link" to="/inspections">Hủy</Link><button className="button primary" type="submit" disabled={create.isPending}>{create.isPending ? 'Đang tạo…' : 'Tạo Draft Inspection'}</button></div>
    </form></>
}

function LookupSection({ title, searchLabel, search, setSearch, apply, loading, error, children }: { title: string; searchLabel: string; search: string; setSearch: (value: string) => void; apply: () => void; loading: boolean; error: string; children: React.ReactNode }) {
  return <section className="lookup-section"><h2>{title}</h2><label>{searchLabel}<div className="filter-line"><input maxLength={250} value={search} onChange={event => setSearch(event.target.value)} /><button className="button" type="button" onClick={apply}>Tìm</button></div></label>{loading && <p role="status">Đang tải lựa chọn…</p>}{error && <p className="notice error" role="alert">{error}</p>}{children}</section>
}
function Pager({ page, totalPages, busy, setPage }: { page: number; totalPages: number; busy: boolean; setPage: React.Dispatch<React.SetStateAction<number>> }) {
  if (totalPages <= 1) return null
  return <div className="pagination"><button type="button" className="button" disabled={page <= 1 || busy} onClick={() => setPage(value => value - 1)}>Trước</button><span>{page} / {totalPages}</span><button type="button" className="button" disabled={page >= totalPages || busy} onClick={() => setPage(value => value + 1)}>Sau</button></div>
}
