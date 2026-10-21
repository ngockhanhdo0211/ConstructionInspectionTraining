import { ApiError } from '../api/client'

export function checklistErrorMessage(error: unknown, fallback: string) {
  if (!(error instanceof ApiError)) return error instanceof Error ? error.message : fallback
  if (error.status === 400) return validationMessage(error) || 'Dữ liệu Checklist không hợp lệ. Hãy kiểm tra các trường đã nhập.'
  if (error.status === 401) return 'Phiên đăng nhập đã hết hạn.'
  if (error.status === 403) return 'Tài khoản không có quyền thực hiện thao tác này.'
  if (error.status === 404) return 'Không tìm thấy Checklist hoặc dữ liệu nằm ngoài phạm vi truy cập.'
  if (error.status === 413) return 'Nội dung Checklist vượt quá giới hạn 1 MiB.'
  if (error.status === 409) return error.problem?.detail || 'Dữ liệu hoặc trạng thái Checklist đã thay đổi. Nội dung đang nhập vẫn được giữ; hãy tải lại dữ liệu mới nhất.'
  if (error.status >= 500) return 'Máy chủ gặp lỗi. Vui lòng thử lại sau.'
  return error.message || fallback
}

function validationMessage(error: ApiError) {
  const errors = error.problem?.errors
  return errors ? Object.values(errors).flat().join(' ') : error.problem?.detail
}
