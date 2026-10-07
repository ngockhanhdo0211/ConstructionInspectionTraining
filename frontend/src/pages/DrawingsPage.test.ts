import { describe, expect, it } from 'vitest'
import { ApiError } from '../api/client'
import { canUploadDrawings, drawingErrorMessage, formatBytes, validatePdf } from './DrawingsPage'

describe('Drawing upload validation', () => {
  it('rejects missing, non-PDF and oversized files', () => {
    expect(validatePdf()).toContain('Chọn')
    expect(validatePdf(new File(['x'], 'plan.txt'))).toContain('PDF')
    const oversized = { name: 'plan.pdf', size: 50 * 1024 * 1024 + 1 } as File
    expect(validatePdf(oversized)).toContain('50 MiB')
  })
  it('accepts a PDF within the limit and formats metadata', () => {
    expect(validatePdf(new File(['%PDF-'], 'plan.PDF'))).toBe('')
    expect(formatBytes(2 * 1024 * 1024)).toBe('2.0 MiB')
  })
  it('only exposes upload capability to administrators', () => {
    const user = { id: '1', email: 'manager@local.test', fullName: 'Manager', roles: ['MANAGER'] }
    expect(canUploadDrawings(user)).toBe(false)
    expect(canUploadDrawings({ ...user, roles: ['ADMINISTRATOR'] })).toBe(true)
  })
  it('reports forbidden Drawing responses explicitly', () => {
    expect(drawingErrorMessage(new ApiError(403), 'fallback')).toContain('quyền')
  })
})
