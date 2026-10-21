import { describe, expect, it } from 'vitest'
import { ApiError } from '../api/client'
import { checklistErrorMessage } from './errors'

describe('Checklist error messages', () => {
  it.each([
    [400, 'không hợp lệ'],
    [401, 'hết hạn'],
    [403, 'không có quyền'],
    [404, 'Không tìm thấy'],
    [409, 'thay đổi'],
    [413, '1 MiB'],
    [500, 'Máy chủ'],
  ])('maps HTTP %s to safe actionable copy', (status, expected) => {
    expect(checklistErrorMessage(new ApiError(status, { status }), 'fallback')).toContain(expected)
  })

  it('uses server validation details without exposing a response payload dump', () => {
    const message = checklistErrorMessage(new ApiError(400, { status: 400, errors: { Items: ['Cần ít nhất một tiêu chí.'] } }), 'fallback')
    expect(message).toBe('Cần ít nhất một tiêu chí.')
    expect(message).not.toContain('Items')
  })
})
