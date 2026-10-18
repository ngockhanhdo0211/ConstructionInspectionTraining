import { useState } from 'react'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { getInspections } from '../api/client'
import type { InspectionStatus } from '../api/types'
import { useAuth } from '../auth/AuthContext'
import { canCreateInspection } from '../inspections/access'
import { inspectionErrorMessage } from '../inspections/errors'

const statuses: Array<InspectionStatus | ''> = ['', 'Draft', 'Submitted', 'UnderReview', 'Approved', 'Recheck', 'Rejected']

export function InspectionsPage() {
  const { user } = useAuth()
  const [searchInput, setSearchInput] = useState('')
  const [search, setSearch] = useState('')
  const [status, setStatus] = useState<InspectionStatus | ''>('')
  const [page, setPage] = useState(1)
  const pageSize = 20
  const query = useQuery({
    queryKey: ['inspections', page, search, status],
    queryFn: ({ signal }) => getInspections({ pageNumber: page, pageSize, search, status, sortBy: 'createdAt', sortDirection: 'desc' }, signal),
    placeholderData: keepPreviousData,
  })
  const applySearch = (event: React.FormEvent) => { event.preventDefault(); setPage(1); setSearch(searchInput.trim()) }
  return <><header className="page-head"><div><p className="kicker">Quality records</p><h1>Inspections</h1><p>Danh sách hồ sơ trong phạm vi tài khoản hiện tại. Dữ liệu biểu mẫu không xuất hiện ở danh sách.</p></div>{canCreateInspection(user) && <Link className="button primary action-link" to="/inspections/new">Tạo Inspection</Link>}</header>
    <section className="inspection-filters" aria-label="Bộ lọc Inspection"><form onSubmit={applySearch}><label htmlFor="inspection-search">Tìm theo số hoặc tiêu đề</label><div className="filter-line"><input id="inspection-search" value={searchInput} maxLength={250} onChange={event => setSearchInput(event.target.value)} /><button className="button" type="submit">Tìm</button></div></form><label htmlFor="inspection-status">Trạng thái<select id="inspection-status" value={status} onChange={event => { setStatus(event.target.value as InspectionStatus | ''); setPage(1) }}>{statuses.map(value => <option value={value} key={value || 'all'}>{value || 'Tất cả'}</option>)}</select></label></section>
    <section className="inventory"><div className="section-title"><h2>Hồ sơ Inspection</h2><span className="count">{query.data?.totalCount ?? '—'} hồ sơ</span></div>
      {query.isLoading && <p className="state" role="status">Đang tải danh sách Inspection…</p>}
      {query.isError && <p className="state error" role="alert">{inspectionErrorMessage(query.error, 'Không thể tải danh sách Inspection.')}</p>}
      {query.data?.items.length === 0 && <div className="empty"><strong>Không có Inspection phù hợp</strong><span>Thay đổi bộ lọc hoặc tạo hồ sơ mới nếu tài khoản có quyền.</span></div>}
      {query.data && query.data.items.length > 0 && <div className="inspection-list" role="list">{query.data.items.map(item => <Link className="inspection-row" role="listitem" to={`/inspections/${item.id}`} key={item.id}>
        <div><span className="code-label">{item.number}</span><strong>{item.title}</strong><span className="identifier">Object: {item.constructionObjectId}</span></div>
        <span>Template v{item.formTemplateVersionNumber}</span><span className={`status ${item.status.toLowerCase()}`}>{item.status}</span>
        <div><span>{formatDate(item.createdAt)}</span><span>{item.updatedAt ? `Cập nhật ${formatDate(item.updatedAt)}` : 'Chưa cập nhật'}</span></div>
      </Link>)}</div>}
      {query.data && query.data.totalPages > 1 && <nav className="pagination" aria-label="Phân trang Inspection"><button className="button" disabled={page <= 1 || query.isFetching} onClick={() => setPage(value => value - 1)}>Trang trước</button><span>Trang {query.data.pageNumber} / {query.data.totalPages}</span><button className="button" disabled={page >= query.data.totalPages || query.isFetching} onClick={() => setPage(value => value + 1)}>Trang sau</button></nav>}
    </section></>
}

function formatDate(value: string) { return new Intl.DateTimeFormat('vi-VN', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value)) }
