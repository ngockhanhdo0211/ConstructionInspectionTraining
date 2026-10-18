import { ApiError } from '../api/client'

export function inspectionErrorMessage(error: unknown, fallback: string) {
  if (!(error instanceof ApiError)) return error instanceof Error ? error.message : fallback
  if (error.status === 400) return validationMessage(error) || 'Dữ liệu Inspection không hợp lệ. Hãy kiểm tra các trường đã nhập.'
  if (error.status === 401) return 'Phiên đăng nhập đã hết hạn.'
  if (error.status === 403) return 'Tài khoản không có quyền thực hiện thao tác này.'
  if (error.status === 404) return 'Không tìm thấy Inspection hoặc tài nguyên nằm ngoài phạm vi truy cập.'
  if (error.status === 413) return 'Dữ liệu Draft vượt quá kích thước cho phép.'
  if (error.status === 409) {
    if (error.problem?.code === 'inspection.concurrency_conflict') return 'Bản ghi đã thay đổi trên máy chủ. Nội dung bạn đang nhập vẫn được giữ; hãy tải lại bản máy chủ trước khi sửa tiếp.'
    if (error.problem?.code === 'inspection.invalid_state') return 'Inspection không còn ở trạng thái Draft.'
    if (error.problem?.code === 'inspection.form_template_unavailable') return 'Form Template đã chọn không còn khả dụng.'
    if (error.problem?.code === 'inspection.construction_object_inactive') return 'Construction Object đã chọn không còn hoạt động.'
    return 'Trạng thái dữ liệu đã thay đổi. Hãy tải lại trước khi tiếp tục.'
  }
  if (error.status >= 500) return 'Máy chủ gặp lỗi. Vui lòng thử lại sau.'
  return error.message || fallback
}

function validationMessage(error: ApiError) {
  const errors = error.problem?.errors
  return errors ? Object.values(errors).flat().join(' ') : error.problem?.detail
}
