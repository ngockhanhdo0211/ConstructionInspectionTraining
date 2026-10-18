import type { FormComponent, FormSchema } from '../formTemplates/schema'
import { requiredIncomplete, type DraftValue, type DraftValues } from '../inspections/draft'

type Props = {
  schema: FormSchema; values: DraftValues; onChange: (values: DraftValues) => void
  readOnly?: boolean; errors?: Record<string, string>
}

export function DynamicInspectionForm({ schema, values, onChange, readOnly = false, errors = {} }: Props) {
  const setValue = (key: string, value: DraftValue | undefined) => {
    const next = { ...values }
    if (value === undefined) delete next[key]
    else next[key] = value
    onChange(next)
  }

  return <div className="inspection-form" aria-label="Biểu mẫu Inspection">
    {schema.components.filter(component => !component.hidden).map(component => {
      const errorId = errors[component.key] ? `${component.key}-error` : undefined
      const incomplete = requiredIncomplete(component, values)
      return <div className="inspection-field" key={component.key}>
        {renderField(component, values[component.key], value => setValue(component.key, value), readOnly || component.disabled === true, errorId)}
        {component.description && <small>{component.description}</small>}
        {component.tooltip && <small>{component.tooltip}</small>}
        {incomplete && <span className="incomplete">Bắt buộc khi Submit — Draft vẫn có thể lưu.</span>}
        {errors[component.key] && <span id={errorId} className="field-error" role="alert">{errors[component.key]}</span>}
      </div>
    })}
  </div>
}

function renderField(component: FormComponent, value: DraftValue | undefined, change: (value: DraftValue | undefined) => void, disabled: boolean, errorId?: string) {
  const id = `inspection-${component.key}`
  const required = component.validate?.required === true
  const common = { id, disabled, 'aria-describedby': errorId, 'aria-invalid': Boolean(errorId) }
  if (component.type === 'textfield') return <label htmlFor={id}>{label(component, required)}<input {...common} type="text" placeholder={component.placeholder} value={typeof value === 'string' ? value : ''} minLength={numberRule(component, 'minLength')} maxLength={numberRule(component, 'maxLength')} onChange={event => change(event.target.value || undefined)} /></label>
  if (component.type === 'textarea') return <label htmlFor={id}>{label(component, required)}<textarea {...common} placeholder={component.placeholder} rows={component.rows ?? 4} value={typeof value === 'string' ? value : ''} minLength={numberRule(component, 'minLength')} maxLength={numberRule(component, 'maxLength')} onChange={event => change(event.target.value || undefined)} /></label>
  if (component.type === 'number') return <label htmlFor={id}>{label(component, required)}<input {...common} type="number" placeholder={component.placeholder} step={component.validate?.integer === true ? 1 : 'any'} min={numberRule(component, 'min')} max={numberRule(component, 'max')} value={typeof value === 'number' && Number.isFinite(value) ? value : ''} onChange={event => change(event.target.value === '' ? undefined : Number(event.target.value))} /></label>
  if (component.type === 'checkbox') return <label className="inspection-check" htmlFor={id}><input {...common} type="checkbox" checked={value === true} onChange={event => change(event.target.checked)} />{label(component, required)}</label>
  if (component.type === 'datetime') {
    const inputType = component.enableDate === false ? 'time' : component.enableTime ? 'datetime-local' : 'date'
    return <label htmlFor={id}>{label(component, required)}<input {...common} type={inputType} value={dateInputValue(value, inputType)} onChange={event => change(event.target.value ? isoDateValue(event.target.value, inputType) : undefined)} /></label>
  }
  const options = component.type === 'select' ? component.data?.values : component.values
  if (component.type === 'select') {
    const selected = component.multiple && Array.isArray(value) ? value : value === undefined ? [] : [value]
    const selectValue = component.multiple ? selected.map(item => optionIndex(options, item)) : optionIndex(options, selected[0])
    return <label htmlFor={id}>{label(component, required)}<select {...common} multiple={component.multiple} value={selectValue} onChange={event => {
      const indices = Array.from(event.target.selectedOptions).map(option => Number(option.value))
      const selectedValues = indices.map(index => options?.[index]?.value).filter((item): item is string | number | boolean => item !== undefined)
      change(component.multiple ? selectedValues : selectedValues[0])
    }}><option value="" hidden={component.multiple}>Chọn một giá trị</option>{options?.map((option, index) => <option value={index} key={`${index}-${String(option.value)}`}>{option.label}</option>)}</select></label>
  }
  return <fieldset aria-describedby={errorId}><legend>{label(component, required)}</legend>{options?.map((option, index) => <label className="inspection-choice" key={`${index}-${String(option.value)}`}><input disabled={disabled} type="radio" name={component.key} checked={value === option.value} onChange={() => change(option.value)} />{option.label}</label>)}</fieldset>
}

function label(component: FormComponent, required: boolean) {
  return <span>{component.label}{required && <span aria-label="bắt buộc"> *</span>} <code>{component.key}</code></span>
}
function numberRule(component: FormComponent, key: string) {
  const value = component.validate?.[key]
  return typeof value === 'number' ? value : undefined
}
function optionIndex(options: Array<{ value: string | number | boolean }> | undefined, value: DraftValue | undefined) {
  const index = options?.findIndex(option => option.value === value) ?? -1
  return index < 0 ? '' : String(index)
}
function dateInputValue(value: DraftValue | undefined, type: string) {
  if (typeof value !== 'string') return ''
  if (type === 'date') return value.slice(0, 10)
  if (type === 'time') return value.includes('T') ? value.split('T')[1]?.slice(0, 5) ?? '' : value.slice(0, 5)
  return value.replace(/Z$/, '').slice(0, 16)
}
function isoDateValue(value: string, type: string) {
  if (type === 'date') return `${value}T00:00:00Z`
  if (type === 'time') return `1970-01-01T${value.length === 5 ? `${value}:00` : value}Z`
  return `${value.length === 16 ? `${value}:00` : value}Z`
}
