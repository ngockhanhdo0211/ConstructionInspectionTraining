const encoder = new TextEncoder()
export const RFI_CONTENT_BYTES = 32768

export function utf8Bytes(value: string) { return encoder.encode(value).byteLength }
export function validateRfiText(subject: string, question: string) {
  if (!subject.trim() || !question.trim()) return 'Tiêu đề và câu hỏi là bắt buộc.'
  if (subject.trim().length > 200) return 'Tiêu đề tối đa 200 ký tự.'
  if (utf8Bytes(question.trim()) > RFI_CONTENT_BYTES) return 'Câu hỏi vượt quá 32.768 byte UTF-8.'
  return ''
}
