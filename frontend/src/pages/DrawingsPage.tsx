import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { ApiError, getDrawings, uploadDrawing } from '../api/client'
import { useAuth } from '../auth/AuthContext'
import type { User } from '../api/types'

export function validatePdf(file?: File) { if (!file) return 'Chọn một file PDF.'; if (file.size > 50 * 1024 * 1024) return 'File không được vượt quá 50 MiB.'; if (!file.name.toLowerCase().endsWith('.pdf')) return 'Chỉ chấp nhận file PDF.'; return '' }
export function canUploadDrawings(user: User | null) { return user?.roles.includes('ADMINISTRATOR') ?? false }
export function drawingErrorMessage(error: unknown, fallback: string) {
  if (error instanceof ApiError && error.status === 403) return 'Tài khoản không có quyền truy cập Drawing.'
  if (error instanceof ApiError && error.status === 401) return 'Phiên đăng nhập đã hết hạn.'
  return error instanceof Error ? error.message : fallback
}
export function DrawingsPage() {
  const { user } = useAuth(); const queryClient = useQueryClient(); const [file, setFile] = useState<File>(); const [progress, setProgress] = useState(0); const [message, setMessage] = useState('')
  const query = useQuery({ queryKey: ['drawings'], queryFn: getDrawings })
  const upload = useMutation({ mutationFn: (selected: File) => uploadDrawing(selected, setProgress), onSuccess: async result => { setFile(undefined); setProgress(0); setMessage(result.duplicateWarning ? 'Đã tải lên. Nội dung trùng với một Drawing hiện có.' : 'Đã tải lên Drawing mới.'); await queryClient.invalidateQueries({ queryKey: ['drawings'] }) } })
  const canUpload = Boolean(file) && !validatePdf(file)
  return <><header className="page-head"><div><p className="kicker">Document control</p><h1>Drawings</h1><p>PDF gốc trong object storage; metadata kiểm soát trong SQL Server.</p></div><span className="count">{query.data?.totalCount ?? '—'} hồ sơ</span></header>
    {canUploadDrawings(user) && <section className="upload-panel"><div><h2>Tải PDF</h2><p>Tối đa 50 MiB · 2.000 trang · không hỗ trợ file có mật khẩu.</p></div><label className="file-field"><span>{file?.name ?? 'Chọn PDF từ máy'}</span><input type="file" accept="application/pdf,.pdf" onChange={e => { setFile(e.target.files?.[0]); setMessage('') }} /></label><button className="button primary" disabled={!canUpload || upload.isPending} onClick={() => file && upload.mutate(file)}>{upload.isPending ? 'Đang tải…' : 'Tải lên'}</button>
      {upload.isPending && <div className="progress" aria-label={`Đã tải ${progress}%`}><span style={{ transform: `scaleX(${progress / 100})` }} /></div>}
      {file && validatePdf(file) && <p className="notice error">{validatePdf(file)}</p>}{upload.error && <p className="notice error" role="alert">{upload.error instanceof ApiError && upload.error.status === 403 ? 'Tài khoản không có quyền tải Drawing.' : upload.error.message}</p>}{message && <p className="notice success" role="status">{message}</p>}
    </section>}
    <section className="inventory"><div className="section-title"><h2>Danh sách Drawing</h2><button className="button ghost" onClick={() => query.refetch()} disabled={query.isFetching}>Làm mới</button></div>
      {query.isLoading && <p className="state">Đang tải danh sách…</p>}{query.isError && <p className="state error" role="alert">{drawingErrorMessage(query.error, 'Không thể tải danh sách.')}</p>}{query.data?.items.length === 0 && <div className="empty"><strong>Chưa có Drawing</strong><span>Tải PDF đầu tiên để bắt đầu danh mục.</span></div>}
      {query.data && query.data.items.length > 0 && <div className="drawing-list" role="list">{query.data.items.map(d => <Link className="drawing-row" role="listitem" to={`/drawings/${d.id}`} key={d.id}><div><strong>{d.originalFileName}</strong><span>{new Intl.DateTimeFormat('vi-VN', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(d.createdAt))}</span></div><span>{d.pageCount} trang</span><span>{formatBytes(d.fileSize)}</span><span className={`status ${d.status.toLowerCase()}`}>{d.status}</span></Link>)}</div>}
    </section></>
}
export function formatBytes(value: number) { return value < 1024 * 1024 ? `${Math.ceil(value / 1024)} KiB` : `${(value / 1024 / 1024).toFixed(1)} MiB` }
