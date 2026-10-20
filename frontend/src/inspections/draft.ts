import type { FormComponent, FormSchema } from '../formTemplates/schema'

export type DraftValue = string | number | boolean | Array<string | number | boolean> | null
export type DraftValues = Record<string, DraftValue>

export function parseDraftData(json: string, schema: FormSchema): { values?: DraftValues; error?: string } {
  try {
    const parsed = JSON.parse(json) as unknown
    if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) return { error: 'DataJson không phải JSON object hợp lệ.' }
    const allowed = new Set(schema.components.map(component => component.key))
    const values: DraftValues = {}
    for (const [key, value] of Object.entries(parsed as Record<string, unknown>)) {
      if (!allowed.has(key)) return { error: `DataJson chứa field không có trong pinned schema: ${key}.` }
      if (!isDraftValue(value)) return { error: `Giá trị của field ${key} không thể hiển thị an toàn.` }
      values[key] = value
    }
    return { values }
  } catch { return { error: 'DataJson không phải JSON hợp lệ.' } }
}

export function serializeDraft(schema: FormSchema, values: DraftValues) {
  const ordered: DraftValues = {}
  schema.components.forEach(component => { if (Object.hasOwn(values, component.key)) ordered[component.key] = values[component.key] })
  return JSON.stringify(ordered)
}

export function validateDraftTypes(schema: FormSchema, values: DraftValues) {
  const errors: Record<string, string> = {}
  schema.components.forEach(component => {
    if (!Object.hasOwn(values, component.key) || values[component.key] === null) return
    const value = values[component.key]
    const message = validateValue(component, value)
    if (message) errors[component.key] = message
  })
  return errors
}

export function validateRequired(schema: FormSchema, values: DraftValues) {
  const errors = validateDraftTypes(schema, values)
  schema.components.forEach(component => {
    if (requiredIncomplete(component, values)) errors[component.key] = 'Trường này là bắt buộc trước khi Submit.'
  })
  return errors
}

export function requiredIncomplete(component: FormComponent, values: DraftValues) {
  if (component.validate?.required !== true) return false
  const value = values[component.key]
  return value === undefined || value === null || value === '' || (Array.isArray(value) && value.length === 0)
}

function validateValue(component: FormComponent, value: DraftValue | undefined) {
  if (component.type === 'textfield' || component.type === 'textarea' || component.type === 'datetime')
    return typeof value === 'string' ? '' : 'Giá trị phải là chuỗi.'
  if (component.type === 'checkbox') return typeof value === 'boolean' ? '' : 'Giá trị phải là boolean.'
  if (component.type === 'number') {
    if (typeof value !== 'number' || !Number.isFinite(value)) return 'Giá trị phải là số hữu hạn.'
    if (component.validate?.integer === true && !Number.isInteger(value)) return 'Giá trị phải là số nguyên.'
    if (typeof component.validate?.min === 'number' && value < component.validate.min) return `Giá trị tối thiểu là ${component.validate.min}.`
    if (typeof component.validate?.max === 'number' && value > component.validate.max) return `Giá trị tối đa là ${component.validate.max}.`
    return ''
  }
  const options = component.type === 'select' ? component.data?.values : component.values
  if (component.type === 'select' && component.multiple) {
    if (!Array.isArray(value)) return 'Giá trị phải là danh sách lựa chọn.'
    return value.every(item => options?.some(option => option.value === item)) ? '' : 'Giá trị không thuộc danh sách lựa chọn.'
  }
  return options?.some(option => option.value === value) ? '' : 'Giá trị không thuộc danh sách lựa chọn.'
}

function isDraftValue(value: unknown): value is DraftValue {
  if (value === null || ['string', 'boolean'].includes(typeof value)) return true
  if (typeof value === 'number') return Number.isFinite(value)
  return Array.isArray(value) && value.every(item => ['string', 'number', 'boolean'].includes(typeof item))
}
