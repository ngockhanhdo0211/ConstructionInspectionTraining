import { describe, expect, it } from 'vitest'
import { componentTypes, newComponent, parseSchema, schemaByteCount, serializeSchema, validateSchema, type FormSchema } from './schema'

describe('FormIO schema safety contract', () => {
  it.each(componentTypes)('accepts supported component %s', type => {
    expect(validateSchema({ components: [newComponent(type, 1)] })).toEqual([])
  })

  it('rejects unknown component types and properties', () => {
    expect(validateSchema({ components: [{ type: 'file', key: 'upload', label: 'Upload' }] }).join(' ')).toContain('type không được hỗ trợ')
    expect(validateSchema({ components: [{ ...newComponent('textfield', 1), widget: 'custom' }] }).join(' ')).toContain('widget')
  })

  it.each(['custom', 'calculateValue', 'logic', 'components', 'html', 'url'])('rejects dangerous property %s', property => {
    expect(validateSchema({ components: [{ ...newComponent('textfield', 1), [property]: 'unsafe' }] }).join(' ')).toContain(property)
  })

  it('rejects nested components', () => {
    expect(validateSchema({ components: [{ ...newComponent('textfield', 1), components: [] }] })).not.toEqual([])
  })

  it('enforces case-insensitive unique keys and key syntax', () => {
    expect(validateSchema({ components: [{ ...newComponent('textfield', 1), key: 'Name' }, { ...newComponent('textarea', 2), key: 'name' }] }).join(' ')).toContain('trùng')
    expect(validateSchema({ components: [{ ...newComponent('textfield', 1), key: '1bad-key' }] }).join(' ')).toContain('key')
  })

  it('requires local options for Select and Radio', () => {
    expect(validateSchema({ components: [{ ...newComponent('select', 1), data: { values: [] } }] })).not.toEqual([])
    expect(validateSchema({ components: [{ ...newComponent('radio', 1), values: [] }] })).not.toEqual([])
  })

  it('rejects remote data and executable strings', () => {
    expect(validateSchema({ components: [{ ...newComponent('select', 1), dataSrc: 'url' }] }).join(' ')).toContain('Select')
    expect(validateSchema({ components: [{ ...newComponent('textfield', 1), description: 'javascript:alert(1)' }] }).join(' ')).toContain('script')
  })

  it('enforces component count and JSON depth', () => {
    expect(validateSchema({ components: Array.from({ length: 201 }, (_, index) => newComponent('checkbox', index + 1)) }).join(' ')).toContain('200')
    const deeplyNested = { components: [{ ...newComponent('textfield', 1), validate: { required: true } }] }
    let cursor: Record<string, unknown> = deeplyNested.components[0].validate!
    for (let index = 0; index < 17; index++) { cursor.next = {}; cursor = cursor.next as Record<string, unknown> }
    expect(validateSchema(deeplyNested).join(' ')).toContain('depth')
  })

  it('counts UTF-8 bytes instead of JavaScript characters', () => {
    const schema: FormSchema = { components: [{ ...newComponent('textfield', 1), label: 'Độ ẩm' }] }
    expect(schemaByteCount(schema)).toBe(new TextEncoder().encode(serializeSchema(schema)).length)
    expect(schemaByteCount(schema)).toBeGreaterThan(serializeSchema(schema).length)
  })

  it('parses valid JSON and fails closed for invalid JSON', () => {
    expect(parseSchema('{"components":[]}').schema).toEqual({ components: [] })
    expect(parseSchema('{bad').errors).toEqual(['SchemaJson không phải JSON hợp lệ.'])
  })

  it('only permits validation properties accepted for each type', () => {
    expect(validateSchema({ components: [{ ...newComponent('checkbox', 1), validate: { min: 1 } }] }).join(' ')).toContain('validate.min')
    expect(validateSchema({ components: [{ ...newComponent('number', 1), validate: { min: 0, integer: true } }] })).toEqual([])
  })
})
