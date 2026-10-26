import { describe, expect, it } from 'vitest'
import { RFI_CONTENT_BYTES, utf8Bytes, validateRfiText } from './validation'

describe('RFI UTF-8 validation', () => {
  it('counts UTF-8 bytes rather than JavaScript characters', () => {
    expect(utf8Bytes('é')).toBe(2)
    expect(utf8Bytes('🔥')).toBe(4)
  })

  it('accepts the exact 32 KiB boundary and rejects one extra multibyte character', () => {
    const exact = 'é'.repeat(RFI_CONTENT_BYTES / 2)
    expect(validateRfiText('Subject', exact)).toBe('')
    expect(validateRfiText('Subject', `${exact}é`)).toMatch(/32\.768 byte/)
  })
})
