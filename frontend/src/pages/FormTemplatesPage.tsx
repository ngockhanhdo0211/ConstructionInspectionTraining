import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { getFormTemplates } from '../api/client'
import { templateErrorMessage } from '../formTemplates/errors'

export function FormTemplatesPage() {
  const query = useQuery({ queryKey: ['form-templates'], queryFn: ({ signal }) => getFormTemplates(signal) })
  return <><header className="page-head"><div><p className="kicker">Quality forms</p><h1>Form Templates</h1><p>Quản lý schema biểu mẫu theo version, từ Draft đến Published.</p></div><Link className="button primary action-link" to="/form-templates/new">Tạo template</Link></header>
    <section className="inventory"><div className="section-title"><h2>Danh sách template</h2><button className="button ghost" onClick={() => query.refetch()} disabled={query.isFetching}>Làm mới</button></div>
      {query.isLoading && <p className="state" role="status">Đang tải danh sách…</p>}
      {query.isError && <p className="state error" role="alert">{templateErrorMessage(query.error, 'Không thể tải danh sách template.')}</p>}
      {query.data?.items.length === 0 && <div className="empty"><strong>Chưa có Form Template</strong><span>Tạo template đầu tiên để bắt đầu thiết kế biểu mẫu.</span></div>}
      {query.data && query.data.items.length > 0 && <div className="template-list" role="list">{query.data.items.map(template => <Link className="template-row" role="listitem" to={`/form-templates/${template.id}`} key={template.id}>
        <div><span className="code-label">{template.code}</span><strong>{template.name}</strong><span>{template.description || 'Không có mô tả'}</span></div>
        <span>Published: {template.latestPublishedVersionNumber ? `v${template.latestPublishedVersionNumber}` : '—'}</span><span>Draft: {template.draftVersionNumber ? `v${template.draftVersionNumber}` : '—'}</span><span className={`status ${template.status.toLowerCase()}`}>{template.status}</span>
      </Link>)}</div>}
    </section></>
}
