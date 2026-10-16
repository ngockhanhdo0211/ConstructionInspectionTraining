export const componentTypes = ['textfield', 'textarea', 'number', 'select', 'radio', 'checkbox', 'datetime'] as const
export type ComponentType = typeof componentTypes[number]
export type FormComponent = {
  type: ComponentType; key: string; label: string; description?: string; placeholder?: string; tooltip?: string
  input?: boolean; tableView?: boolean; hidden?: boolean; disabled?: boolean; defaultValue?: string | number | boolean
  validate?: Record<string, string | number | boolean>; prefix?: string; suffix?: string; spellcheck?: boolean
  rows?: number; autoExpand?: boolean; decimalLimit?: number; requireDecimal?: boolean
  dataSrc?: 'values'; data?: { values: { label: string; value: string }[] }; multiple?: boolean
  values?: { label: string; value: string }[]; inline?: boolean; format?: string; enableDate?: boolean; enableTime?: boolean
}
export type FormSchema = { components: FormComponent[] }

export const schemaLimits = { bytes: 1_048_576, components: 200, depth: 16 }
const common = new Set(['type', 'key', 'label', 'description', 'placeholder', 'tooltip', 'input', 'tableView', 'hidden', 'disabled', 'defaultValue', 'validate'])
const specific: Record<ComponentType, Set<string>> = {
  textfield: new Set(['prefix', 'suffix', 'spellcheck']), textarea: new Set(['rows', 'autoExpand', 'spellcheck']),
  number: new Set(['decimalLimit', 'requireDecimal', 'prefix', 'suffix']), select: new Set(['dataSrc', 'data', 'multiple']),
  radio: new Set(['values', 'inline']), checkbox: new Set(), datetime: new Set(['format', 'enableDate', 'enableTime']),
}
const validationProperties: Record<ComponentType, Set<string>> = {
  textfield: new Set(['minLength', 'maxLength', 'required']), textarea: new Set(['minLength', 'maxLength', 'required']),
  number: new Set(['min', 'max', 'integer', 'required']), select: new Set(['required']), radio: new Set(['required']),
  checkbox: new Set(['required']), datetime: new Set(['minDate', 'maxDate', 'required']),
}
const dangerous = new Set(['custom', 'customconditional', 'calculatevalue', 'calculateserver', 'logic', 'actions', 'event', 'events', 'evalcontext', 'template', 'content', 'html', 'url', 'resource', 'refreshon', 'redrawon', 'components'])
const keyPattern = /^[A-Za-z][A-Za-z0-9_]{0,99}$/

export function newComponent(type: ComponentType, ordinal: number): FormComponent {
  const base: FormComponent = { type, key: `field${ordinal}`, label: `Field ${ordinal}`, input: true, tableView: true }
  if (type === 'select') return { ...base, dataSrc: 'values', data: { values: [{ label: 'Option 1', value: 'option1' }] } }
  if (type === 'radio') return { ...base, values: [{ label: 'Option 1', value: 'option1' }] }
  if (type === 'datetime') return { ...base, format: 'yyyy-MM-dd', enableDate: true, enableTime: false }
  return base
}

function jsonDepth(value: unknown, depth = 1): number {
  if (value === null || typeof value !== 'object') return depth
  const children = Array.isArray(value) ? value : Object.values(value as Record<string, unknown>)
  return children.reduce((max, child) => Math.max(max, jsonDepth(child, depth + 1)), depth)
}

export function validateSchema(schema: unknown): string[] {
  const errors: string[] = []
  if (!schema || typeof schema !== 'object' || Array.isArray(schema)) return ['Schema phải là một JSON object.']
  const root = schema as Record<string, unknown>
  if (Object.keys(root).some(key => key !== 'components')) errors.push('Root chỉ được chứa thuộc tính components.')
  if (!Array.isArray(root.components)) return [...errors, 'components phải là một mảng.']
  if (root.components.length > schemaLimits.components) errors.push(`Tối đa ${schemaLimits.components} components.`)
  if (jsonDepth(schema) > schemaLimits.depth) errors.push(`JSON depth không được vượt quá ${schemaLimits.depth}.`)
  const seen = new Set<string>()
  root.components.forEach((raw, index) => {
    const label = `Component ${index + 1}`
    if (!raw || typeof raw !== 'object' || Array.isArray(raw)) { errors.push(`${label} không hợp lệ.`); return }
    const component = raw as Record<string, unknown>
    const type = component.type
    if (!componentTypes.includes(type as ComponentType)) { errors.push(`${label}: type không được hỗ trợ.`); return }
    const allowed = new Set([...common, ...specific[type as ComponentType]])
    Object.keys(component).forEach(property => {
      const lower = property.toLowerCase()
      if (dangerous.has(lower) || !allowed.has(property)) errors.push(`${label}: thuộc tính ${property} không được phép.`)
    })
    if (typeof component.key !== 'string' || !keyPattern.test(component.key)) errors.push(`${label}: key phải bắt đầu bằng chữ và chỉ gồm chữ, số hoặc _.`)
    else if (seen.has(component.key.toLowerCase())) errors.push(`${label}: key bị trùng (không phân biệt hoa thường).`)
    else seen.add(component.key.toLowerCase())
    if (typeof component.label !== 'string' || !component.label.trim()) errors.push(`${label}: label là bắt buộc.`)
    if (component.input !== undefined && component.input !== true) errors.push(`${label}: input chỉ được phép là true.`)
    if (component.validate !== undefined) {
      if (!component.validate || typeof component.validate !== 'object' || Array.isArray(component.validate)) errors.push(`${label}: validate không hợp lệ.`)
      else Object.keys(component.validate).forEach(property => { if (!validationProperties[type as ComponentType].has(property)) errors.push(`${label}: validate.${property} không được phép.`) })
    }
    if (type === 'select' && (component.dataSrc !== 'values' || !hasOptions((component.data as { values?: unknown } | undefined)?.values))) errors.push(`${label}: Select cần ít nhất một option local.`)
    if (type === 'radio' && !hasOptions(component.values)) errors.push(`${label}: Radio cần ít nhất một option.`)
    if (type === 'datetime' && component.enableDate === false && component.enableTime === false) errors.push(`${label}: Date/Time phải bật ngày hoặc giờ.`)
    scanStrings(component, label, errors)
  })
  return errors
}

function hasOptions(value: unknown): boolean {
  return Array.isArray(value) && value.length > 0 && value.every(option => {
    if (!option || typeof option !== 'object' || Array.isArray(option)) return false
    const entry = option as Record<string, unknown>
    return Object.keys(entry).every(key => key === 'label' || key === 'value') && typeof entry.label === 'string' && Boolean(entry.label.trim()) && ['string', 'number', 'boolean'].includes(typeof entry.value)
  })
}

function scanStrings(value: unknown, label: string, errors: string[]) {
  if (typeof value === 'string' && /(javascript\s*:|<\s*script|data\s*:[^,]*;base64)/i.test(value)) errors.push(`${label}: nội dung thực thi, script hoặc base64 không được phép.`)
  else if (Array.isArray(value)) value.forEach(item => scanStrings(item, label, errors))
  else if (value && typeof value === 'object') Object.values(value as Record<string, unknown>).forEach(item => scanStrings(item, label, errors))
}

export function parseSchema(json: string): { schema?: FormSchema; errors: string[] } {
  try {
    const parsed = JSON.parse(json) as unknown
    const errors = validateSchema(parsed)
    return errors.length ? { errors } : { schema: parsed as FormSchema, errors: [] }
  } catch { return { errors: ['SchemaJson không phải JSON hợp lệ.'] } }
}

export function serializeSchema(schema: FormSchema) { return JSON.stringify(schema) }
export function schemaByteCount(schema: FormSchema) { return new TextEncoder().encode(serializeSchema(schema)).length }
