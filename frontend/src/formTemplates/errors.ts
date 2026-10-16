import { ApiError } from '../api/client'

export function templateErrorMessage(error: unknown, fallback: string) {
  if (error instanceof ApiError && error.status === 409) return 'Dữ liệu đã thay đổi. Hãy tải lại trước khi tiếp tục; nội dung đang nhập vẫn được giữ.'
  if (error instanceof ApiError && error.status === 403) return 'Tài khoản không có quyền quản lý Form Template.'
  if (error instanceof ApiError && error.status === 401) return 'Phiên đăng nhập đã hết hạn.'
  if (error instanceof ApiError && error.status === 404) return 'Không tìm thấy template hoặc version được yêu cầu.'
  if (error instanceof ApiError && error.status === 413) return 'Schema vượt quá kích thước payload được phép.'
  if (error instanceof ApiError && error.problem?.errors) return Object.values(error.problem.errors).flat().join(' ')
  if (error instanceof ApiError && error.status >= 500) return 'Máy chủ gặp lỗi. Vui lòng thử lại sau.'
  return error instanceof Error ? error.message : fallback
}
