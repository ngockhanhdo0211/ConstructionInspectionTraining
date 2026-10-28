import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { getDashboard } from '../api/client'
import type { DashboardActionItem, DashboardModuleSummary, DashboardResponse } from '../api/types'
import { useAuth } from '../auth/AuthContext'

const statusLabels: Record<string, string> = {
  draft: 'Draft', submitted: 'Đã gửi', underReview: 'Đang review', approved: 'Đã duyệt',
  recheck: 'Cần kiểm tra lại', rejected: 'Bị từ chối', inProgress: 'Đang thực hiện',
  open: 'Đang mở', responded: 'Đã phản hồi', closed: 'Đã đóng', cancelled: 'Đã hủy',
}
const scopeLabels: Record<DashboardResponse['scopeMode'], string> = {
  Full: 'Toàn bộ dữ liệu vận hành', OwnAndAssigned: 'Hồ sơ của tôi và được giao',
  Own: 'Hồ sơ do tôi tạo', Assigned: 'Hồ sơ được giao cho tôi',
}
const roleLabels: Record<string, string> = {
  ADMINISTRATOR: 'Administrator', MANAGER: 'Manager', INSPECTOR: 'Inspector', REVIEWER: 'Reviewer',
}
const actionLabels: Record<string, string> = {
  Edit: 'Tiếp tục', Start: 'Bắt đầu', Resume: 'Tiếp tục', StartReview: 'Bắt đầu review',
  Review: 'Review', Respond: 'Phản hồi', ReviewResponse: 'Xem phản hồi', Reassign: 'Phân công lại',
}

type ActiveDashboardRequest = { controller: AbortController; consumers: number; promise: Promise<DashboardResponse> }
const activeDashboardRequests = new Map<string, ActiveDashboardRequest>()

function loadDashboard(identityKey: string, signal: AbortSignal) {
  let request = activeDashboardRequests.get(identityKey)
  if (!request) {
    const controller = new AbortController()
    request = { controller, consumers: 0, promise: getDashboard(controller.signal) }
    activeDashboardRequests.set(identityKey, request)
    const clear = () => { if (activeDashboardRequests.get(identityKey) === request) activeDashboardRequests.delete(identityKey) }
    void request.promise.then(clear, clear)
  }
  request.consumers++
  let released = false
  const release = () => {
    if (released) return
    released = true; request.consumers--
    // React StrictMode immediately remounts in Development. Give that replacement
    // observer one event-loop turn to reuse the in-flight request before aborting.
    window.setTimeout(() => {
      if (activeDashboardRequests.get(identityKey) !== request || request.consumers !== 0) return
      activeDashboardRequests.delete(identityKey)
      request.controller.abort()
    }, 0)
  }
  signal.addEventListener('abort', release, { once: true })
  if (signal.aborted) release()
  return request.promise.finally(() => { signal.removeEventListener('abort', release); release() })
}

export function DashboardPage() {
  const { user } = useAuth()
  const identityKey = `${user?.id ?? 'anonymous'}:${[...(user?.roles ?? [])].sort().join('|')}`
  const query = useQuery({
    queryKey: ['dashboard', identityKey],
    queryFn: ({ signal }) => loadDashboard(identityKey, signal),
    retry: false,
    staleTime: 0,
    gcTime: 0,
    refetchOnWindowFocus: false,
  })

  if (query.isLoading) return <DashboardLoading />
  if (query.isError || !query.data) return <DashboardError retry={() => void query.refetch()} pending={query.isFetching} />

  const dashboard = query.data
  const total = dashboard.inspectionSummary.total + dashboard.checklistSummary.total + dashboard.rfiSummary.total
  return <div className="dashboard-page">
    <header className="page-head dashboard-head">
      <div><p className="kicker">Operational overview</p><h1>Dashboard</h1><p>{scopeLabels[dashboard.scopeMode]}</p></div>
      <div className="dashboard-head-meta">
        <span><strong>Vai trò:</strong> {dashboard.roles.map(role => roleLabels[role] ?? role).join(', ')}</span>
        <time dateTime={dashboard.generatedAtUtc}><strong>Dữ liệu lúc:</strong> {formatDate(dashboard.generatedAtUtc)}</time>
        <button className="button" type="button" disabled={query.isFetching} onClick={() => void query.refetch()}>{query.isFetching ? 'Đang làm mới…' : 'Làm mới'}</button>
      </div>
    </header>

    {total === 0 && <div className="dashboard-empty" role="status"><strong>Chưa có dữ liệu trong phạm vi này</strong><span>Các chỉ số sẽ xuất hiện khi hồ sơ Inspection, Checklist hoặc RFI được tạo.</span></div>}

    <section aria-labelledby="dashboard-summary-title">
      <div className="dashboard-section-title"><div><p className="kicker">Snapshot</p><h2 id="dashboard-summary-title">Tổng quan ba module</h2></div>{query.isFetching && <span role="status">Đang cập nhật dữ liệu…</span>}</div>
      <div className="dashboard-summary-grid">
        <SummaryCard title="Inspection" href="/inspections" summary={dashboard.inspectionSummary} />
        <SummaryCard title="Checklist" href="/checklists" summary={dashboard.checklistSummary} />
        <SummaryCard title="RFI" href="/rfis" summary={dashboard.rfiSummary} extras={[
          ['Chờ phản hồi', dashboard.rfiSummary.awaitingResponse],
          ['Chờ Inspector xử lý', dashboard.rfiSummary.awaitingInspectorAction],
          ['Đang mở quá hạn', dashboard.rfiSummary.overdueOpen],
        ]} />
      </div>
    </section>

    <section className="dashboard-actions" aria-labelledby="dashboard-actions-title">
      <div className="dashboard-section-title"><div><p className="kicker">Action queue</p><h2 id="dashboard-actions-title">Công việc cần xử lý</h2></div><span className="count">{dashboard.actionQueue.length} việc</span></div>
      <div className="dashboard-queue-grid">
        <ActionQueue title="Inspection" module="Inspection" items={dashboard.actionQueue} />
        <ActionQueue title="Checklist" module="Checklist" items={dashboard.actionQueue} />
        <ActionQueue title="RFI" module="Rfi" items={dashboard.actionQueue} />
      </div>
    </section>
  </div>
}

function SummaryCard<TStatus extends object>({ title, href, summary, extras = [] }: {
  title: string; href: string; summary: DashboardModuleSummary<TStatus>; extras?: Array<[string, number]>
}) {
  return <article className="dashboard-card" aria-labelledby={`summary-${title.toLowerCase()}`}>
    <div className="dashboard-card-head"><h3 id={`summary-${title.toLowerCase()}`}>{title}</h3><Link to={href}>Mở danh sách <span aria-hidden="true">→</span></Link></div>
    <p className="dashboard-total"><strong>{summary.total}</strong><span>Tổng hồ sơ</span></p>
    <dl className="dashboard-key-metrics"><div><dt>Đang mở</dt><dd>{summary.openWork}</dd></div><div><dt>Chờ tôi xử lý</dt><dd>{summary.awaitingMyAction}</dd></div>{extras.map(([label, value]) => <div key={label}><dt>{label}</dt><dd>{value}</dd></div>)}</dl>
    <h4>Phân bố trạng thái</h4>
    <ul className="dashboard-status-list">{Object.entries(summary.statusCounts).map(([status, value]) => <li key={status}><span>{statusLabels[status] ?? status}</span><strong>{value}</strong></li>)}</ul>
  </article>
}

function ActionQueue({ title, module, items }: { title: string; module: DashboardActionItem['module']; items: DashboardActionItem[] }) {
  const queue = items.filter(item => item.module === module)
  return <section className="dashboard-queue" aria-labelledby={`queue-${module.toLowerCase()}`}>
    <div className="dashboard-queue-head"><h3 id={`queue-${module.toLowerCase()}`}>{title}</h3><span>{queue.length}/5</span></div>
    {queue.length === 0 ? <p className="dashboard-queue-empty">Không có công việc cần xử lý.</p> : <ul>{queue.map(item => <li key={`${item.module}-${item.entityId}`}><QueueItem item={item} /></li>)}</ul>}
  </section>
}

function QueueItem({ item }: { item: DashboardActionItem }) {
  const href = item.module === 'Inspection' ? `/inspections/${item.entityId}` : item.module === 'Checklist' ? `/checklists/${item.entityId}` : `/rfis/${item.entityId}`
  return <Link className="dashboard-queue-item" to={href}>
    <span className="dashboard-queue-title"><span className="code-label">{item.number}</span><strong>{item.displayTitle}</strong></span>
    <span>{item.constructionObject.code} — {item.constructionObject.name}</span>
    <span className="dashboard-queue-meta"><span className={`status ${item.status.toLowerCase()}`}>{statusLabels[item.status[0].toLowerCase() + item.status.slice(1)] ?? item.status}</span>{item.isOverdue && <span className="status overdue">Quá hạn</span>}</span>
    <span>{item.dueAtUtc ? `Hạn ${formatDate(item.dueAtUtc)}` : `Cập nhật ${formatDate(item.updatedAtUtc)}`}</span>
    <strong className="dashboard-action-label">{actionLabels[item.actionKind] ?? item.actionKind} <span aria-hidden="true">→</span></strong>
  </Link>
}

function DashboardLoading() {
  return <div className="dashboard-page" aria-busy="true"><header className="page-head dashboard-head"><div><p className="kicker">Operational overview</p><h1>Dashboard</h1><p role="status">Đang tải dữ liệu tổng quan…</p></div></header><div className="dashboard-summary-grid" aria-hidden="true">{[1, 2, 3].map(value => <div className="dashboard-card dashboard-skeleton" key={value} />)}</div></div>
}
function DashboardError({ retry, pending }: { retry(): void; pending: boolean }) {
  return <div className="dashboard-page"><header className="page-head"><div><p className="kicker">Operational overview</p><h1>Dashboard</h1></div></header><div className="dashboard-error" role="alert"><strong>Không thể tải Dashboard</strong><span>Vui lòng kiểm tra kết nối rồi thử lại.</span><button className="button" type="button" disabled={pending} onClick={retry}>{pending ? 'Đang thử lại…' : 'Thử lại'}</button></div></div>
}
function formatDate(value: string) { return new Intl.DateTimeFormat('vi-VN', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value)) }
