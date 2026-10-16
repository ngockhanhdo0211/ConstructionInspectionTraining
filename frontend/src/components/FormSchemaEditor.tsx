import { useMemo, useState } from 'react'
import { componentTypes, newComponent, parseSchema, schemaByteCount, schemaLimits, serializeSchema, validateSchema, type ComponentType, type FormComponent, type FormSchema } from '../formTemplates/schema'

const typeLabels: Record<ComponentType, string> = { textfield: 'Text Field', textarea: 'Text Area', number: 'Number', select: 'Select', radio: 'Radio', checkbox: 'Checkbox', datetime: 'Date/Time' }

export function FormSchemaEditor({ value, onChange, disabled = false }: { value: FormSchema; onChange(value: FormSchema): void; disabled?: boolean }) {
  const [selected, setSelected] = useState(0)
  const [advanced, setAdvanced] = useState(false)
  const [json, setJson] = useState(() => serializeSchema(value))
  const [jsonErrors, setJsonErrors] = useState<string[]>([])
  const errors = useMemo(() => validateSchema(value), [value])
  const bytes = schemaByteCount(value)
  const current = value.components[selected]
  const replace = (component: FormComponent) => onChange({ components: value.components.map((item, index) => index === selected ? component : item) })
  const add = (type: ComponentType) => {
    if (disabled || value.components.length >= schemaLimits.components) return
    const next = { components: [...value.components, newComponent(type, value.components.length + 1)] }
    onChange(next); setSelected(next.components.length - 1)
  }
  const move = (direction: -1 | 1) => {
    const target = selected + direction
    if (target < 0 || target >= value.components.length) return
    const components = [...value.components]; [components[selected], components[target]] = [components[target], components[selected]]
    onChange({ components }); setSelected(target)
  }
  const remove = () => {
    const components = value.components.filter((_, index) => index !== selected)
    onChange({ components }); setSelected(Math.max(0, selected - 1))
  }
  const applyJson = () => {
    const result = parseSchema(json)
    if (!result.schema) { setJsonErrors(result.errors); return }
    if (new TextEncoder().encode(json).length > schemaLimits.bytes) { setJsonErrors([`Schema vượt quá ${schemaLimits.bytes.toLocaleString()} UTF-8 bytes.`]); return }
    onChange(result.schema); setSelected(0); setJsonErrors([]); setJson(serializeSchema(result.schema))
  }

  return <section className="schema-editor" aria-label="Form schema editor">
    <div className="schema-toolbar">
      <strong>{value.components.length}/{schemaLimits.components} components</strong><span>{bytes.toLocaleString()} bytes</span>
      {!disabled && <select aria-label="Loại component mới" defaultValue="" disabled={value.components.length >= schemaLimits.components} onChange={event => { if (event.target.value) add(event.target.value as ComponentType); event.target.value = '' }}>
        <option value="" disabled>+ Thêm component</option>{componentTypes.map(type => <option key={type} value={type}>{typeLabels[type]}</option>)}
      </select>}
      <button type="button" className="button ghost" onClick={() => { setAdvanced(show => !show); setJson(serializeSchema(value)); setJsonErrors([]) }}>{advanced ? 'Editor trực quan' : 'JSON nâng cao'}</button>
    </div>
    {advanced ? <div className="advanced-editor">
      <label>SchemaJson<textarea aria-label="SchemaJson" value={json} disabled={disabled} rows={18} onChange={event => setJson(event.target.value)} /></label>
      {!disabled && <button type="button" className="button" onClick={applyJson}>Áp dụng JSON</button>}
      {jsonErrors.map(error => <p className="notice error" key={error}>{error}</p>)}
    </div> : <div className="schema-layout">
      <ol className="component-list">
        {value.components.map((component, index) => <li key={`${component.key}-${index}`}><button type="button" className={selected === index ? 'selected' : ''} onClick={() => setSelected(index)}><span>{typeLabels[component.type]}</span><strong>{component.label}</strong><code>{component.key}</code></button></li>)}
        {!value.components.length && <li className="empty">Chưa có component. Hãy thêm trường đầu tiên.</li>}
      </ol>
      {current && <fieldset className="component-properties" disabled={disabled}>
        <legend>Thuộc tính component</legend>
        <div className="panel-actions"><button type="button" className="button" onClick={() => move(-1)} disabled={selected === 0}>Lên</button><button type="button" className="button" onClick={() => move(1)} disabled={selected === value.components.length - 1}>Xuống</button><button type="button" className="button danger" onClick={remove}>Xóa</button></div>
        <label>Label<input value={current.label} onChange={event => replace({ ...current, label: event.target.value })} /></label>
        <label>Key<input value={current.key} onChange={event => replace({ ...current, key: event.target.value })} /></label>
        <label>Mô tả<input value={current.description ?? ''} onChange={event => replace(withOptional(current, 'description', event.target.value))} /></label>
        <label>Placeholder<input value={current.placeholder ?? ''} onChange={event => replace(withOptional(current, 'placeholder', event.target.value))} /></label>
        <label className="check"><input type="checkbox" checked={Boolean(current.validate?.required)} onChange={event => replace({ ...current, validate: cleanValidate({ ...current.validate, required: event.target.checked }) })} />Bắt buộc</label>
        <label className="check"><input type="checkbox" checked={Boolean(current.hidden)} onChange={event => replace({ ...current, hidden: event.target.checked })} />Ẩn</label>
        <label className="check"><input type="checkbox" checked={Boolean(current.disabled)} onChange={event => replace({ ...current, disabled: event.target.checked })} />Vô hiệu hóa</label>
        {(current.type === 'select' || current.type === 'radio') && <label>Options (mỗi dòng: Label|value)<textarea rows={6} value={optionsText(current)} onChange={event => replace(withOptions(current, event.target.value))} /></label>}
        {current.type === 'textarea' && <label>Số dòng<input type="number" min="1" value={current.rows ?? 3} onChange={event => replace({ ...current, rows: Number(event.target.value) })} /></label>}
        {current.type === 'number' && <label>Số chữ số thập phân<input type="number" min="0" value={current.decimalLimit ?? 0} onChange={event => replace({ ...current, decimalLimit: Number(event.target.value) })} /></label>}
        {current.type === 'datetime' && <label>Định dạng<select value={current.format ?? 'yyyy-MM-dd'} onChange={event => replace({ ...current, format: event.target.value })}><option>yyyy-MM-dd</option><option>yyyy-MM-dd HH:mm</option><option>yyyy-MM-dd'T'HH:mm:ssXXX</option></select></label>}
      </fieldset>}
    </div>}
    {(errors.length > 0 || bytes > schemaLimits.bytes || value.components.length === schemaLimits.components) && <div role="alert">{value.components.length === schemaLimits.components && <p className="notice">Đã đạt giới hạn 200 components.</p>}{bytes > schemaLimits.bytes && <p className="notice error">Schema vượt quá giới hạn UTF-8.</p>}{errors.map(error => <p className="notice error" key={error}>{error}</p>)}</div>}
    <details className="schema-preview"><summary>JSON preview</summary><pre>{JSON.stringify(value, null, 2)}</pre></details>
  </section>
}

function withOptional(component: FormComponent, property: 'description' | 'placeholder', value: string): FormComponent {
  const next = { ...component, [property]: value || undefined }; if (!value) delete next[property]; return next
}
function cleanValidate(validate: Record<string, string | number | boolean>) {
  return Object.fromEntries(Object.entries(validate).filter(([, value]) => value !== false && value !== ''))
}
function optionsText(component: FormComponent) {
  const options = component.type === 'select' ? component.data?.values : component.values
  return (options ?? []).map(option => `${option.label}|${option.value}`).join('\n')
}
function withOptions(component: FormComponent, text: string): FormComponent {
  const values = text.split('\n').map(line => { const [label, ...rest] = line.split('|'); return { label: label.trim(), value: rest.join('|').trim() } }).filter(option => option.label || option.value)
  return component.type === 'select' ? { ...component, dataSrc: 'values', data: { values } } : { ...component, values }
}
