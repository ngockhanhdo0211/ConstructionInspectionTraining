import { ApiError } from '../api/client'

const conflictMessages: Record<string, string> = {
  'construction_object.concurrency_conflict': 'Construction Object đã thay đổi trên máy chủ. Nội dung đang nhập vẫn được giữ; hãy tải lại phiên bản mới.',
  'construction_object.code_duplicate': 'Code đã được sử dụng bởi Construction Object khác.',
  'construction_object.parent_inactive': 'Parent đã ngừng hoạt động và không thể nhận thêm object con.',
  'construction_object.invalid_parent_type': 'Loại parent không phù hợp với Object Type đã chọn.',
  'construction_object.self_parent': 'Construction Object không thể là parent của chính nó.',
  'construction_object.cycle_detected': 'Parent đã chọn sẽ tạo chu trình trong hierarchy.',
  'construction_object.maximum_depth_exceeded': 'Hierarchy không được vượt quá năm cấp.',
  'construction_object.active_descendants_exist': 'Hãy deactivate toàn bộ descendant đang Active trước.',
  'construction_object.inactive': 'Construction Object Inactive không thể chỉnh sửa.',
  'construction_object.object_type_immutable': 'Object Type không thể thay đổi sau khi tạo.',
}

export function constructionObjectErrorMessage(error: unknown, fallback: string) {
  if (!(error instanceof ApiError)) return error instanceof Error ? error.message : fallback
  if (error.status === 400) return validationMessage(error) || 'Dữ liệu Construction Object không hợp lệ.'
  if (error.status === 401) return 'Phiên đăng nhập đã hết hạn.'
  if (error.status === 403) return 'Tài khoản không có quyền thực hiện thao tác này.'
  if (error.status === 404) return 'Không tìm thấy Construction Object hoặc object nằm ngoài phạm vi truy cập.'
  if (error.status === 409) return (error.problem?.code && conflictMessages[error.problem.code]) || 'Construction Object đang ở trạng thái xung đột. Hãy tải lại dữ liệu mới nhất.'
  if (error.status >= 500) return 'Máy chủ gặp lỗi. Vui lòng thử lại sau.'
  return error.problem?.detail || fallback
}

export function isConstructionObjectConcurrencyError(error: unknown) {
  return error instanceof ApiError && error.status === 409 && error.problem?.code === 'construction_object.concurrency_conflict'
}

function validationMessage(error: ApiError) {
  return error.problem?.errors ? Object.values(error.problem.errors).flat().join(' ') : error.problem?.detail
}
