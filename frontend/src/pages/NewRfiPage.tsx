import { keepPreviousData, useMutation, useQuery } from '@tanstack/react-query'
import { useRef, useState, type FormEvent } from 'react'
import { Link, Navigate, useNavigate } from 'react-router-dom'
import { createRfi, getInspectionConstructionObjects } from '../api/client'
import { useAuth } from '../auth/AuthContext'
import { useUnsavedChanges } from '../inspections/useUnsavedChanges'
import { canCreateRfi } from '../rfis/access'
import { rfiErrorMessage } from '../rfis/errors'
import { RFI_CONTENT_BYTES, utf8Bytes, validateRfiText } from '../rfis/validation'

export function NewRfiPage() {
  const { user } = useAuth(); const navigate = useNavigate(); const errorRef = useRef<HTMLDivElement>(null)
  const allowed = canCreateRfi(user); const [searchInput, setSearchInput] = useState(''); const [search, setSearch] = useState(''); const [page, setPage] = useState(1)
  const [constructionObjectId, setConstructionObjectId] = useState(''); const [subject, setSubject] = useState(''); const [question, setQuestion] = useState(''); const [localError, setLocalError] = useState('')
  useUnsavedChanges(Boolean(constructionObjectId || subject || question))
  const objects = useQuery({ queryKey: ['rfi-object-options', page, search], queryFn: ({ signal }) => getInspectionConstructionObjects(page, 20, search, signal), enabled: allowed, placeholderData: keepPreviousData })
  const create = useMutation({ mutationFn: () => createRfi({ constructionObjectId, subject: subject.trim(), question: question.trim() }), onSuccess: result => navigate(`/rfis/${result.rfiId}`, { replace: true }), onError: () => window.setTimeout(() => errorRef.current?.focus(), 0) })
  if (!allowed) return <Navigate to="/rfis" replace />
  const submit = (event: FormEvent) => { event.preventDefault(); const error = !constructionObjectId ? 'Hãy chọn Construction Object.' : validateRfiText(subject, question); setLocalError(error); if (error) { window.setTimeout(() => errorRef.current?.focus(), 0); return } if (!create.isPending) create.mutate() }
  return <><Link className="back-link" to="/rfis">← Danh sách RFI</Link><header className="page-head"><div><p className="kicker">New technical query</p><h1>Tạo RFI</h1><p>Server cấp RFI Number sau khi Draft được tạo.</p></div></header><form className="rfi-form" onSubmit={submit} noValidate>
    {(localError || create.error) && <div className="notice error" role="alert" tabIndex={-1} ref={errorRef}>{localError || rfiErrorMessage(create.error, 'Không thể tạo RFI.')}</div>}
    <section className="lookup-section"><h2>Construction Object Active</h2><label htmlFor="rfi-object-search">Tìm object theo code hoặc tên<div className="filter-line"><input id="rfi-object-search" value={searchInput} maxLength={200} onChange={event => setSearchInput(event.target.value)} /><button className="button" type="button" onClick={() => { setPage(1); setSearch(searchInput.trim()) }}>Tìm</button></div></label>{objects.isLoading && <p role="status">Đang tải lựa chọn…</p>}{objects.isError && <p className="notice error" role="alert">{rfiErrorMessage(objects.error, 'Không thể tải Construction Object.')}</p>}<label htmlFor="rfi-object">Construction Object *<select id="rfi-object" value={constructionObjectId} onChange={event => setConstructionObjectId(event.target.value)}><option value="">Chọn object Active</option>{objects.data?.items.map(item => <option key={item.id} value={item.id}>{item.code} — {item.name} ({item.objectType})</option>)}</select></label>{(objects.data?.totalPages ?? 0) > 1 && <nav className="pagination compact" aria-label="Phân trang Construction Object"><button className="button" type="button" disabled={page <= 1 || objects.isFetching} onClick={() => setPage(value => value - 1)}>Trước</button><span>{page} / {objects.data?.totalPages}</span><button className="button" type="button" disabled={page >= (objects.data?.totalPages ?? 0) || objects.isFetching} onClick={() => setPage(value => value + 1)}>Sau</button></nav>}</section>
    <section className="rfi-fields"><h2>Nội dung RFI</h2><label htmlFor="rfi-subject">Tiêu đề *<input id="rfi-subject" required maxLength={200} value={subject} onChange={event => setSubject(event.target.value)} /><span className="field-hint">{subject.length} / 200 ký tự</span></label><label htmlFor="rfi-question">Câu hỏi *<textarea id="rfi-question" required rows={10} value={question} onChange={event => setQuestion(event.target.value)} /><span className="field-hint">{utf8Bytes(question)} / {RFI_CONTENT_BYTES.toLocaleString('vi-VN')} byte UTF-8</span></label></section>
    <div className="form-actions"><Link className="button action-link" to="/rfis">Hủy</Link><button className="button primary" type="submit" disabled={create.isPending}>{create.isPending ? 'Đang tạo…' : 'Tạo Draft RFI'}</button></div>
  </form></>
}
