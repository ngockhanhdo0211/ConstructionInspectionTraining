import { cleanup, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import '@testing-library/jest-dom/vitest'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { FormSchema } from '../formTemplates/schema'
import { DynamicInspectionForm } from './DynamicInspectionForm'

const schema: FormSchema = { components: [
  { type: 'textfield', key: 'text', label: '<script>alert(1)</script>', description: 'Plain help', validate: { required: true } },
  { type: 'textarea', key: 'notes', label: 'Notes', rows: 3 },
  { type: 'number', key: 'score', label: 'Score', validate: { integer: true, min: 0, max: 10 } },
  { type: 'select', key: 'trade', label: 'Trade', dataSrc: 'values', data: { values: [{ label: 'Civil', value: 'civil' }] } },
  { type: 'radio', key: 'result', label: 'Result', values: [{ label: 'Pass', value: 'pass' }] },
  { type: 'checkbox', key: 'ok', label: 'OK' },
  { type: 'datetime', key: 'at', label: 'At', enableDate: true, enableTime: true },
] }

afterEach(cleanup)

describe('DynamicInspectionForm', () => {
  it('renders all seven supported components, hydrates values and treats labels as text', () => {
    render(<DynamicInspectionForm schema={schema} values={{ text: 'Existing', notes: 'Note', score: 5, trade: 'civil', result: 'pass', ok: true, at: '2026-10-18T09:30:00Z' }} onChange={vi.fn()} />)
    expect(screen.getByDisplayValue('Existing')).toBeInTheDocument()
    expect(screen.getByDisplayValue('Note')).toBeInTheDocument()
    expect(screen.getByDisplayValue('5')).toHaveAttribute('step', '1')
    expect(screen.getByRole('option', { name: 'Civil' })).toBeInTheDocument()
    expect(screen.getByRole('radio', { name: /Pass/ })).toBeChecked()
    expect(screen.getByRole('checkbox', { name: /OK/ })).toBeChecked()
    expect(screen.getByDisplayValue('2026-10-18T09:30')).toBeInTheDocument()
    expect(screen.getByText('<script>alert(1)</script>')).toBeInTheDocument()
    expect(document.querySelector('script')).not.toBeInTheDocument()
  })

  it('emits real number, boolean, static option and ISO date values', async () => {
    const user = userEvent.setup(); const change = vi.fn()
    const { rerender } = render(<DynamicInspectionForm schema={schema} values={{}} onChange={change} />)
    await user.type(screen.getByLabelText(/Score/), '7')
    expect(change).toHaveBeenLastCalledWith({ score: 7 })
    rerender(<DynamicInspectionForm schema={schema} values={{ score: 7 }} onChange={change} />)
    await user.click(screen.getByRole('checkbox', { name: /OK/ }))
    expect(change).toHaveBeenLastCalledWith({ score: 7, ok: true })
    await user.selectOptions(screen.getByLabelText(/Trade/), '0')
    expect(change).toHaveBeenLastCalledWith({ score: 7, trade: 'civil' })
    await user.type(screen.getByLabelText(/At/), '2026-10-18T09:30')
    expect(change.mock.calls.flatMap(call => Object.values(call[0])).some(value => value === '2026-10-18T09:30:00Z')).toBe(true)
  })

  it('marks required fields incomplete without disabling Draft input', () => {
    render(<DynamicInspectionForm schema={schema} values={{}} onChange={vi.fn()} />)
    expect(screen.getByText('Bắt buộc khi Submit — Draft vẫn có thể lưu.')).toBeInTheDocument()
    expect(screen.getByLabelText(/text/)).toBeEnabled()
  })

  it('renders every control disabled in read-only mode', () => {
    render(<DynamicInspectionForm schema={schema} values={{}} onChange={vi.fn()} readOnly />)
    screen.getAllByRole('textbox').forEach(control => expect(control).toBeDisabled())
    expect(screen.getByRole('spinbutton')).toBeDisabled()
    expect(screen.getByRole('combobox')).toBeDisabled()
    expect(screen.getByRole('checkbox')).toBeDisabled()
    expect(screen.getByRole('radio')).toBeDisabled()
  })
})
