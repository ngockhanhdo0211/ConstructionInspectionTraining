import { keepPreviousData, useMutation, useQuery } from '@tanstack/react-query'
import { useRef, useState, type FormEvent } from 'react'
import { Link, Navigate, useNavigate } from 'react-router-dom'
import { createChecklist, getInspectionConstructionObjects } from '../api/client'
import { useAuth } from '../auth/AuthContext'
import { canCreateChecklist } from '../checklists/access'
import { checklistErrorMessage } from '../checklists/errors'
import { useUnsavedChanges } from '../inspections/useUnsavedChanges'

type NewItem = { key: number; criterion: string; isRequired: boolean }
let nextKey = 2

export function NewChecklistPage() {
  const { user } = useAuth(); const navigate = useNavigate(); const errorRef = useRef<HTMLDivElement>(null)
  const allowed = canCreateChecklist(user)
  const [searchInput, setSearchInput] = useState(''); const [search, setSearch] = useState(''); const [page, setPage] = useState(1)
  const [constructionObjectId, setConstructionObjectId] = useState(''); const [title, setTitle] = useState(''); const [description, setDescription] = useState('')
  const [items, setItems] = useState<NewItem[]>([{ key: 1, criterion: '', isRequired: true }]); const [localError, setLocalError] = useState('')
  useUnsavedChanges(Boolean(constructionObjectId || title || description || items.some(item => item.criterion)))
  const objects = useQuery({ queryKey: ['checklist-object-options', page, search], queryFn: ({ signal }) => getInspectionConstructionObjects(page, 20, search, signal), enabled: allowed, placeholderData: keepPreviousData })
  const create = useMutation({ mutationFn: () => createChecklist({ constructionObjectId, title: title.trim(), description: description.trim() || null, items: items.map(item => ({ criterion: item.criterion.trim(), isRequired: item.isRequired })) }), onSuccess: result => navigate(`/checklists/${result.checklistId}`, { replace: true }), onError: () => focusError(errorRef) })
  if (!allowed) return <Navigate to="/checklists" replace />
  const submit = (event: FormEvent) => {
    event.preventDefault(); setLocalError('')
    const normalized = items.map(item => item.criterion.trim())
    let error = ''
    if (!constructionObjectId || !title.trim()) error = 'Hãy chọn Construction Object và nhập tiêu đề.'
    else if (title.trim().length > 250 || description.trim().length > 2000) error = 'Tiêu đề tối đa 250 ký tự và mô tả tối đa 2.000 ký tự.'
    else if (items.length < 1 || items.length > 200 || normalized.some(value => !value || value.length > 1000)) error = 'Checklist cần từ 1 đến 200 tiêu chí; mỗi tiêu chí tối đa 1.000 ký tự.'
    else if (new Set(normalized.map(value => value.toLocaleLowerCase())).size !== normalized.length) error = 'Tiêu chí không được trùng nhau (không phân biệt chữ hoa/thường).'
    if (error) { setLocalError(error); focusError(errorRef); return }
    if (!create.isPending) create.mutate()
  }
  const move = (index: number, direction: -1 | 1) => setItems(current => { const target = index + direction; if (target < 0 || target >= current.length) return current; const copy = [...current]; [copy[index], copy[target]] = [copy[target], copy[index]]; return copy })
  return <><Link className="back-link" to="/checklists">← Danh sách Checklist</Link><header className="page-head"><div><p className="kicker">New quality record</p><h1>Tạo Checklist</h1><p>Server cấp Checklist Number; thứ tự tiêu chí được xác định theo danh sách bên dưới.</p></div></header>
    <form className="checklist-create" onSubmit={submit} noValidate>
      {(localError || create.error) && <div className="notice error" role="alert" tabIndex={-1} ref={errorRef}>{localError || checklistErrorMessage(create.error, 'Không thể tạo Checklist.')}</div>}
      <section className="lookup-section"><h2>Construction Object Active</h2><label htmlFor="checklist-object-search">Tìm object theo code hoặc tên<div className="filter-line"><input id="checklist-object-search" maxLength={200} value={searchInput} onChange={event => setSearchInput(event.target.value)} /><button className="button" type="button" onClick={() => { setPage(1); setSearch(searchInput.trim()) }}>Tìm</button></div></label>{objects.isLoading && <p role="status">Đang tải lựa chọn…</p>}{objects.isError && <p className="notice error" role="alert">{checklistErrorMessage(objects.error, 'Không thể tải Construction Object.')}</p>}<label htmlFor="checklist-object">Construction Object *<select id="checklist-object" value={constructionObjectId} onChange={event => setConstructionObjectId(event.target.value)}><option value="">Chọn object Active</option>{objects.data?.items.map(item => <option key={item.id} value={item.id}>{item.code} — {item.name} ({item.objectType})</option>)}</select></label>{(objects.data?.totalPages ?? 0) > 1 && <div className="pagination compact"><button className="button" type="button" disabled={page <= 1 || objects.isFetching} onClick={() => setPage(value => value - 1)}>Trước</button><span>{page} / {objects.data?.totalPages}</span><button className="button" type="button" disabled={page >= (objects.data?.totalPages ?? 0) || objects.isFetching} onClick={() => setPage(value => value + 1)}>Sau</button></div>}</section>
      <section className="inspection-metadata"><h2>Thông tin Checklist</h2><label htmlFor="checklist-title">Tiêu đề *<input id="checklist-title" required maxLength={250} value={title} onChange={event => setTitle(event.target.value)} /></label><label htmlFor="checklist-description">Mô tả<textarea id="checklist-description" maxLength={2000} rows={4} value={description} onChange={event => setDescription(event.target.value)} /></label></section>
      <section className="checklist-editor"><div className="section-title"><div><h2>Tiêu chí kiểm tra</h2><p>{items.length} / 200 mục</p></div><button className="button" type="button" disabled={items.length >= 200} onClick={() => setItems(current => [...current, { key: nextKey++, criterion: '', isRequired: true }])}>Thêm tiêu chí</button></div>{items.map((item, index) => <div className="checklist-item author" key={item.key}><span className="sequence">{index + 1}</span><label>Tiêu chí *<textarea maxLength={1000} rows={2} value={item.criterion} onChange={event => setItems(current => current.map(entry => entry.key === item.key ? { ...entry, criterion: event.target.value } : entry))} /></label><label className="inline-check"><input type="checkbox" checked={item.isRequired} onChange={event => setItems(current => current.map(entry => entry.key === item.key ? { ...entry, isRequired: event.target.checked } : entry))} /> Bắt buộc</label><div className="item-buttons"><button className="button" type="button" aria-label={`Đưa tiêu chí ${index + 1} lên`} disabled={index === 0} onClick={() => move(index, -1)}>↑</button><button className="button" type="button" aria-label={`Đưa tiêu chí ${index + 1} xuống`} disabled={index === items.length - 1} onClick={() => move(index, 1)}>↓</button><button className="button danger" type="button" disabled={items.length === 1} onClick={() => setItems(current => current.filter(entry => entry.key !== item.key))}>Xóa</button></div></div>)}</section>
      <div className="form-actions"><Link className="button action-link" to="/checklists">Hủy</Link><button className="button primary" type="submit" disabled={create.isPending}>{create.isPending ? 'Đang tạo…' : 'Tạo Draft Checklist'}</button></div>
    </form></>
}

function focusError(ref: React.RefObject<HTMLDivElement | null>) { window.setTimeout(() => ref.current?.focus(), 0) }
