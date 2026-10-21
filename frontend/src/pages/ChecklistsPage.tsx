import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useEffect, useState, type FormEvent } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { getChecklists } from '../api/client'
import type { ChecklistStatus } from '../api/types'
import { useAuth } from '../auth/AuthContext'
import { canCreateChecklist } from '../checklists/access'
import { checklistErrorMessage } from '../checklists/errors'

const statuses: Array<ChecklistStatus | ''> = ['', 'Draft', 'InProgress', 'Submitted', 'UnderReview', 'Approved', 'Recheck', 'Rejected']

export function ChecklistsPage() {
  const { user } = useAuth()
  const [parameters, setParameters] = useSearchParams()
  const page = positiveInt(parameters.get('page'))
  const search = parameters.get('search') ?? ''
  const status = statuses.includes((parameters.get('status') ?? '') as ChecklistStatus) ? (parameters.get('status') ?? '') as ChecklistStatus | '' : ''
  const [searchInput, setSearchInput] = useState(search)
  // Keep the editable field aligned with browser Back/Forward URL state.
  // oxlint-disable-next-line react/set-state-in-effect
  useEffect(() => setSearchInput(search), [search])
  const query = useQuery({
    queryKey: ['checklists', page, search, status],
    queryFn: ({ signal }) => getChecklists({ pageNumber: page, pageSize: 20, search, status, sortBy: 'createdAt', sortDirection: 'desc' }, signal),
    placeholderData: keepPreviousData,
  })
  const update = (next: { page?: number; search?: string; status?: string }) => {
    const values = new URLSearchParams(parameters)
    const merged = { page, search, status, ...next }
    values.set('page', String(merged.page))
    if (merged.search) values.set('search', merged.search); else values.delete('search')
    if (merged.status) values.set('status', merged.status); else values.delete('status')
    setParameters(values)
  }
  const submitSearch = (event: FormEvent) => { event.preventDefault(); update({ page: 1, search: searchInput.trim() }) }
  return <>
    <header className="page-head"><div><p className="kicker">Site quality</p><h1>Checklists</h1><p>Theo dõi danh sách kiểm tra, kết quả từng tiêu chí và quy trình review trong phạm vi tài khoản.</p></div>{canCreateChecklist(user) && <Link className="button primary action-link" to="/checklists/new">Tạo Checklist</Link>}</header>
    <section className="inspection-filters" aria-label="Bộ lọc Checklist"><form onSubmit={submitSearch}><label htmlFor="checklist-search">Tìm theo số hoặc tiêu đề</label><div className="filter-line"><input id="checklist-search" value={searchInput} maxLength={200} onChange={event => setSearchInput(event.target.value)} /><button className="button" type="submit">Tìm</button></div></form><label htmlFor="checklist-status">Trạng thái<select id="checklist-status" value={status} onChange={event => update({ page: 1, status: event.target.value })}>{statuses.map(value => <option value={value} key={value || 'all'}>{value || 'Tất cả'}</option>)}</select></label></section>
    <section className="inventory"><div className="section-title"><h2>Hồ sơ Checklist</h2><span className="count">{query.data?.totalCount ?? '—'} hồ sơ</span></div>
      {query.isLoading && <p className="state" role="status">Đang tải danh sách Checklist…</p>}
      {query.isError && <p className="state error" role="alert">{checklistErrorMessage(query.error, 'Không thể tải danh sách Checklist.')}</p>}
      {query.data?.items.length === 0 && <div className="empty"><strong>Không có Checklist phù hợp</strong><span>Thay đổi bộ lọc hoặc tạo hồ sơ mới nếu tài khoản có quyền.</span></div>}
      {query.data && query.data.items.length > 0 && <div className="checklist-list" role="list">{query.data.items.map(item => <Link className="checklist-row" role="listitem" to={`/checklists/${item.id}`} key={item.id}>
        <div><span className="code-label">{item.number}</span><strong>{item.title}</strong><span>{item.constructionObjectCode} — {item.constructionObjectName}</span></div>
        <span className={`status ${item.status.toLowerCase()}`}>{item.status}</span>
        <div className="checklist-counts"><span>{item.itemCount} mục</span><span>{item.passCount} Pass · {item.failCount} Fail · {item.notApplicableCount} N/A · {item.notCheckedCount} chưa kiểm tra</span></div>
        <div><span>{formatDate(item.createdAt)}</span><span>{item.updatedAt ? `Cập nhật ${formatDate(item.updatedAt)}` : 'Chưa cập nhật'}</span></div>
      </Link>)}</div>}
      {query.data && query.data.totalPages > 1 && <nav className="pagination" aria-label="Phân trang Checklist"><button className="button" disabled={page <= 1 || query.isFetching} onClick={() => update({ page: page - 1 })}>Trang trước</button><span>Trang {query.data.pageNumber} / {query.data.totalPages}</span><button className="button" disabled={page >= query.data.totalPages || query.isFetching} onClick={() => update({ page: page + 1 })}>Trang sau</button></nav>}
    </section>
  </>
}

function positiveInt(value: string | null) { const parsed = Number(value); return Number.isInteger(parsed) && parsed > 0 ? parsed : 1 }
function formatDate(value: string) { return new Intl.DateTimeFormat('vi-VN', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value)) }
