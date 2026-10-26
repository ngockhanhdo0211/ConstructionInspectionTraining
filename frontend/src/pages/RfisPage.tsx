import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useEffect, useState, type FormEvent } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { getRfis } from '../api/client'
import type { RfiStatus } from '../api/types'
import { useAuth } from '../auth/AuthContext'
import { canCreateRfi } from '../rfis/access'
import { rfiErrorMessage } from '../rfis/errors'

const statuses: Array<RfiStatus | ''> = ['', 'Draft', 'Open', 'Responded', 'Closed', 'Cancelled']

export function RfisPage() {
  const { user } = useAuth(); const [parameters, setParameters] = useSearchParams()
  const page = positiveInt(parameters.get('page')); const search = parameters.get('search') ?? ''
  const status = statuses.includes((parameters.get('status') ?? '') as RfiStatus) ? (parameters.get('status') ?? '') as RfiStatus | '' : ''
  const assignedToMe = parameters.get('assignedToMe') === 'true'; const overdue = parameters.get('overdue') === 'true'
  const [searchInput, setSearchInput] = useState(search)
  // Keep the editable field aligned with browser Back/Forward URL state.
  // oxlint-disable-next-line react/set-state-in-effect
  useEffect(() => setSearchInput(search), [search])
  const query = useQuery({ queryKey: ['rfis', page, search, status, assignedToMe, overdue], queryFn: ({ signal }) => getRfis({ pageNumber: page, pageSize: 20, search, status, assignedToMe: assignedToMe || undefined, overdue: overdue || undefined, sortBy: 'createdAt', sortDirection: 'desc' }, signal), placeholderData: keepPreviousData })
  const update = (next: { page?: number; search?: string; status?: string; assignedToMe?: boolean; overdue?: boolean }) => {
    const merged = { page, search, status, assignedToMe, overdue, ...next }; const values = new URLSearchParams()
    values.set('page', String(merged.page)); if (merged.search) values.set('search', merged.search); if (merged.status) values.set('status', merged.status)
    if (merged.assignedToMe) values.set('assignedToMe', 'true'); if (merged.overdue) values.set('overdue', 'true'); setParameters(values)
  }
  const submit = (event: FormEvent) => { event.preventDefault(); update({ page: 1, search: searchInput.trim() }) }
  return <>
    <header className="page-head"><div><p className="kicker">Project communication</p><h1>RFIs</h1><p>Theo dõi câu hỏi kỹ thuật, người xử lý, hạn phản hồi và lịch sử trao đổi.</p></div>{canCreateRfi(user) && <Link className="button primary action-link" to="/rfis/new">Tạo RFI</Link>}</header>
    <section className="rfi-filters" aria-label="Bộ lọc RFI"><form onSubmit={submit}><label htmlFor="rfi-search">Tìm theo số hoặc tiêu đề</label><div className="filter-line"><input id="rfi-search" value={searchInput} maxLength={200} onChange={event => setSearchInput(event.target.value)} /><button className="button" type="submit">Tìm</button></div></form><label htmlFor="rfi-status">Trạng thái<select id="rfi-status" value={status} onChange={event => update({ page: 1, status: event.target.value })}>{statuses.map(value => <option key={value || 'all'} value={value}>{value || 'Tất cả'}</option>)}</select></label><label className="inline-check"><input type="checkbox" checked={assignedToMe} onChange={event => update({ page: 1, assignedToMe: event.target.checked })} /> Giao cho tôi</label><label className="inline-check"><input type="checkbox" checked={overdue} onChange={event => update({ page: 1, overdue: event.target.checked })} /> Quá hạn</label></section>
    <section className="inventory"><div className="section-title"><h2>Danh sách RFI</h2><span className="count">{query.data?.totalCount ?? '—'} hồ sơ</span></div>
      {query.isLoading && <p className="state" role="status">Đang tải danh sách RFI…</p>}
      {query.isError && <p className="state error" role="alert">{rfiErrorMessage(query.error, 'Không thể tải danh sách RFI.')}</p>}
      {query.data?.items.length === 0 && <div className="empty"><strong>Không có RFI phù hợp</strong><span>Thay đổi bộ lọc hoặc tạo RFI mới nếu tài khoản có quyền.</span></div>}
      {query.data && query.data.items.length > 0 && <div className="rfi-list" role="list">{query.data.items.map(item => <Link className="rfi-row" role="listitem" to={`/rfis/${item.id}`} key={item.id}><div><span className="code-label">{item.number}</span><strong>{item.subject}</strong><span>{item.constructionObjectCode} — {item.constructionObjectName}</span></div><span className={`status ${item.status.toLowerCase()}`}>{item.status}</span><div><span>{item.assignedToDisplayName ? `Phụ trách: ${item.assignedToDisplayName}` : 'Chưa phân công'}</span><span>{item.dueAtUtc ? `Hạn: ${formatDate(item.dueAtUtc)}` : 'Chưa có hạn'}{item.isOverdue ? ' · Quá hạn' : ''}</span></div><div><span>{formatDate(item.createdAt)}</span><span>Người tạo: {shortId(item.createdByUserId)}</span></div></Link>)}</div>}
      {query.data && query.data.totalPages > 1 && <nav className="pagination" aria-label="Phân trang RFI"><button className="button" disabled={page <= 1 || query.isFetching} onClick={() => update({ page: page - 1 })}>Trang trước</button><span>Trang {query.data.pageNumber} / {query.data.totalPages}</span><button className="button" disabled={page >= query.data.totalPages || query.isFetching} onClick={() => update({ page: page + 1 })}>Trang sau</button></nav>}
    </section>
  </>
}
function positiveInt(value: string | null) { const parsed = Number(value); return Number.isInteger(parsed) && parsed > 0 ? parsed : 1 }
function formatDate(value: string) { return new Intl.DateTimeFormat('vi-VN', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value)) }
function shortId(value: string) { return value.length > 12 ? `${value.slice(0, 8)}…` : value }
