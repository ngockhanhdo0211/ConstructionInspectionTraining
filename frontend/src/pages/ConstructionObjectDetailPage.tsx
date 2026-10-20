import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useMemo, useRef, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { deactivateConstructionObject, getConstructionObject, updateConstructionObject } from '../api/client'
import type { ConstructionObjectDetail } from '../api/types'
import { useAuth } from '../auth/AuthContext'
import { ConstructionObjectForm } from '../components/ConstructionObjectForm'
import { canManageConstructionObjects } from '../constructionObjects/access'
import { constructionObjectErrorMessage, isConstructionObjectConcurrencyError } from '../constructionObjects/errors'
import { toConstructionObjectInput, validateConstructionObject, type ConstructionObjectFormValue } from '../constructionObjects/form'
import { useUnsavedConstructionObject } from '../constructionObjects/useUnsavedConstructionObject'

function formFromDetail(detail: ConstructionObjectDetail): ConstructionObjectFormValue {
  return { parentId: detail.parentId ?? '', code: detail.code, name: detail.name, objectType: detail.objectType, location: detail.location ?? '', description: detail.description ?? '' }
}

export function ConstructionObjectDetailPage() {
  const { id = '' } = useParams(); const { user } = useAuth(); const queryClient = useQueryClient(); const summaryRef = useRef<HTMLDivElement>(null)
  const query = useQuery({ queryKey: ['construction-object', id], queryFn: ({ signal }) => getConstructionObject(id, signal), enabled: Boolean(id) })
  const [value, setValue] = useState<ConstructionObjectFormValue | null>(null); const [editing, setEditing] = useState(false)
  const [errors, setErrors] = useState<ReturnType<typeof validateConstructionObject>>({}); const [message, setMessage] = useState('')
  const baseline = useMemo(() => query.data ? formFromDetail(query.data) : null, [query.data])
  const dirty = editing && Boolean(value && baseline && JSON.stringify(value) !== JSON.stringify(baseline))
  useUnsavedConstructionObject(dirty)
  const update = useMutation({
    mutationFn: () => updateConstructionObject(id, { ...toConstructionObjectInput(value!), rowVersion: query.data!.rowVersion }),
    onSuccess: detail => { queryClient.setQueryData(['construction-object', id], detail); setValue(null); setEditing(false); setErrors({}); setMessage('Đã cập nhật Construction Object.') },
    onError: () => window.setTimeout(() => summaryRef.current?.focus(), 0),
  })
  const deactivate = useMutation({
    mutationFn: () => deactivateConstructionObject(id, query.data!.rowVersion),
    onSuccess: result => { queryClient.setQueryData<ConstructionObjectDetail>(['construction-object', id], current => current ? { ...current, isActive: false, updatedAt: result.updatedAt, updatedByUserId: result.updatedByUserId, rowVersion: result.rowVersion } : current); setEditing(false); setMessage('Construction Object đã chuyển sang Inactive.') },
    onError: () => window.setTimeout(() => summaryRef.current?.focus(), 0),
  })
  const reload = async () => {
    if (dirty && !window.confirm('Tải lại sẽ bỏ các thay đổi chưa lưu. Tiếp tục?')) return
    const result = await query.refetch()
    if (result.data) { setValue(null); setEditing(false); setErrors({}); update.reset(); deactivate.reset(); setMessage('Đã tải phiên bản mới nhất.') }
  }
  const submit = (event: React.FormEvent) => {
    event.preventDefault(); if (!value) return
    const nextErrors = validateConstructionObject(value); setErrors(nextErrors); setMessage('')
    if (Object.keys(nextErrors).length) { window.setTimeout(() => summaryRef.current?.focus(), 0); return }
    if (!update.isPending) update.mutate()
  }
  if (query.isLoading) return <p className="state" role="status">Đang tải Construction Object…</p>
  if (query.isError) return <><Link className="back-link" to="/construction-objects">← Danh sách Construction Object</Link><div className="state error" role="alert">{constructionObjectErrorMessage(query.error, 'Không thể tải Construction Object.')} <button className="button" onClick={() => query.refetch()}>Thử lại</button></div></>
  if (!query.data || !baseline) return null
  const detail = query.data; const canWrite = canManageConstructionObjects(user) && detail.isActive
  const bannerError = deactivate.error || (!editing ? update.error : null)
  return <><Link className="back-link" to="/construction-objects">← Danh sách Construction Object</Link>
    <header className="page-head"><div><p className="kicker">{detail.objectType}</p><h1>{detail.code} · {detail.name}</h1><p>{detail.breadcrumb.map(item => item.code).join(' / ')}</p></div><span className={`status ${detail.isActive ? 'active' : 'inactive'}`}>{detail.isActive ? 'Active' : 'Inactive'}</span></header>
    {(message || bannerError) && <div className={`notice ${bannerError ? 'error' : 'success'}`} role={bannerError ? 'alert' : 'status'} tabIndex={bannerError ? -1 : undefined} ref={summaryRef}>{bannerError ? constructionObjectErrorMessage(bannerError, 'Không thể thực hiện thao tác.') : message} {isConstructionObjectConcurrencyError(bannerError) && <button className="button" type="button" onClick={reload}>Tải lại phiên bản mới</button>}</div>}
    {!editing && <><dl className="construction-object-summary"><div><dt>Code</dt><dd>{detail.code}</dd></div><div><dt>Name</dt><dd>{detail.name}</dd></div><div><dt>Object Type</dt><dd>{detail.objectType}</dd></div><div><dt>Status</dt><dd>{detail.isActive ? 'Active' : 'Inactive'}</dd></div><div><dt>Parent</dt><dd>{detail.parent ? <Link to={`/construction-objects/${detail.parent.id}`}>{detail.parent.code} — {detail.parent.name}</Link> : 'Root'}</dd></div><div><dt>Direct children</dt><dd>{detail.directChildrenCount}</dd></div><div><dt>Location</dt><dd>{detail.location || '—'}</dd></div><div className="wide"><dt>Description</dt><dd>{detail.description || '—'}</dd></div><div><dt>Created at</dt><dd>{formatDate(detail.createdAt)}</dd></div><div><dt>Updated at</dt><dd>{detail.updatedAt ? formatDate(detail.updatedAt) : 'Chưa cập nhật'}</dd></div></dl>
      {!detail.isActive && <p className="read-only-note lifecycle-note">Object Inactive vẫn được đọc để bảo toàn lịch sử; không thể edit, reactivate hoặc deactivate lại.</p>}
      {canWrite && <div className="detail-actions construction-object-actions"><button className="button" onClick={() => { setValue(formFromDetail(detail)); setEditing(true); setMessage(''); update.reset(); deactivate.reset() }}>Chỉnh sửa</button><button className="button danger" disabled={deactivate.isPending} onClick={() => { setMessage(''); if (window.confirm(`Deactivate ${detail.code}? Object sẽ không thể được kích hoạt lại trong MVP.`)) deactivate.mutate() }}>{deactivate.isPending ? 'Đang deactivate…' : 'Deactivate'}</button></div>}
    </>}
    {editing && <form className="construction-object-form" onSubmit={submit} noValidate>
      {(Object.keys(errors).length > 0 || update.error) && <div className="notice error validation-summary" role="alert" tabIndex={-1} ref={summaryRef}>{update.error ? constructionObjectErrorMessage(update.error, 'Không thể cập nhật Construction Object.') : 'Hãy kiểm tra các trường được đánh dấu bên dưới.'} {isConstructionObjectConcurrencyError(update.error) && <button className="button" type="button" onClick={reload}>Tải lại phiên bản mới</button>}</div>}
      <ConstructionObjectForm value={value!} onChange={setValue} errors={errors} objectTypeLocked currentId={detail.id} currentParent={detail.parent} disabled={update.isPending} />
      <div className="form-actions"><button className="button" type="button" disabled={update.isPending} onClick={() => { if (!dirty || window.confirm('Bỏ các thay đổi chưa lưu?')) { setValue(null); setEditing(false); setErrors({}); update.reset() } }}>Hủy chỉnh sửa</button><button className="button primary" type="submit" disabled={update.isPending || !dirty}>{update.isPending ? 'Đang lưu…' : 'Lưu thay đổi'}</button></div>
    </form>}
  </>
}

function formatDate(value: string) { return new Intl.DateTimeFormat('vi-VN', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value)) }
