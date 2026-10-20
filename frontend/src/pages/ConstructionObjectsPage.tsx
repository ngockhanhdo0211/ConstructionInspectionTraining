import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { Link } from 'react-router-dom'
import { getConstructionObjectPage } from '../api/client'
import type { ConstructionObjectType } from '../api/types'
import { useAuth } from '../auth/AuthContext'
import { canManageConstructionObjects } from '../constructionObjects/access'
import { constructionObjectErrorMessage } from '../constructionObjects/errors'
import { objectTypes } from '../constructionObjects/form'

const pageSize = 20

export function ConstructionObjectsPage() {
  const { user } = useAuth(); const [page, setPage] = useState(1)
  const [searchInput, setSearchInput] = useState(''); const [search, setSearch] = useState('')
  const [status, setStatus] = useState<'true' | 'false' | ''>(''); const [type, setType] = useState<ConstructionObjectType | ''>('')
  const query = useQuery({
    queryKey: ['construction-objects', page, search, status, type],
    queryFn: ({ signal }) => getConstructionObjectPage({ pageNumber: page, pageSize, search, isActive: status, objectType: type, sortBy: 'name', sortDirection: 'asc' }, signal),
    placeholderData: keepPreviousData,
  })
  const apply = (event: React.FormEvent) => { event.preventDefault(); setPage(1); setSearch(searchInput.trim()) }
  return <><header className="page-head"><div><p className="kicker">Project structure</p><h1>Construction Objects</h1><p>Quản lý hierarchy Project → Building → Floor → Room → WorkItem.</p></div>{canManageConstructionObjects(user) && <Link className="button primary action-link" to="/construction-objects/new">Tạo Construction Object</Link>}</header>
    <section className="construction-object-filters"><form onSubmit={apply}><label htmlFor="object-search">Tìm theo code, name hoặc location<div className="filter-line"><input id="object-search" maxLength={200} value={searchInput} onChange={event => setSearchInput(event.target.value)} /><button className="button" type="submit" disabled={query.isFetching}>Tìm</button></div></label></form>
      <label htmlFor="object-status">Trạng thái<select id="object-status" value={status} onChange={event => { setPage(1); setStatus(event.target.value as typeof status) }}><option value="">Tất cả</option><option value="true">Active</option><option value="false">Inactive</option></select></label>
      <label htmlFor="object-type-filter">Object Type<select id="object-type-filter" value={type} onChange={event => { setPage(1); setType(event.target.value as ConstructionObjectType | '') }}><option value="">Tất cả</option>{objectTypes.map(item => <option key={item} value={item}>{item}</option>)}</select></label>
    </section>
    <section className="inventory"><div className="section-title"><h2>Danh sách object</h2><span className="count">{query.data?.totalCount ?? 0} object</span></div>
      {query.isLoading && <p className="state" role="status">Đang tải Construction Object…</p>}
      {query.isError && <div className="state error" role="alert">{constructionObjectErrorMessage(query.error, 'Không thể tải Construction Object.')} <button className="button" onClick={() => query.refetch()} disabled={query.isFetching}>Thử lại</button></div>}
      {query.data?.items.length === 0 && <div className="empty"><strong>Không có Construction Object phù hợp</strong><span>Thay đổi bộ lọc hoặc tạo object mới nếu bạn có quyền.</span></div>}
      {query.data && query.data.items.length > 0 && <div className="construction-object-list" role="list">{query.data.items.map(item => <Link className="construction-object-row" role="listitem" key={item.id} to={`/construction-objects/${item.id}`}><div><span className="code-label">{item.code}</span><strong>{item.name}</strong><span>{item.location || 'Chưa có location'}</span></div><span>{item.objectType}</span><span>{item.parentId ? 'Có parent' : 'Root'}</span><span className={`status ${item.isActive ? 'active' : 'inactive'}`}>{item.isActive ? 'Active' : 'Inactive'}</span></Link>)}</div>}
      {query.data && query.data.totalPages > 1 && <div className="pagination"><button className="button" disabled={page <= 1 || query.isFetching} onClick={() => setPage(value => value - 1)}>Trang trước</button><span>Trang {page} / {query.data.totalPages}</span><button className="button" disabled={page >= query.data.totalPages || query.isFetching} onClick={() => setPage(value => value + 1)}>Trang sau</button></div>}
    </section></>
}
