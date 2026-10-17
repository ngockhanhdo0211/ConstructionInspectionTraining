import { useRef, useState, type FormEvent } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate } from 'react-router-dom'
import { createFormTemplate } from '../api/client'
import { FormSchemaEditor } from '../components/FormSchemaEditor'
import { schemaByteCount, schemaLimits, serializeSchema, validateSchema, type FormSchema } from '../formTemplates/schema'
import { templateErrorMessage } from '../formTemplates/errors'

export function NewFormTemplatePage() {
  const navigate = useNavigate(); const queryClient = useQueryClient()
  const codeInput = useRef<HTMLInputElement>(null); const nameInput = useRef<HTMLInputElement>(null); const validationSummary = useRef<HTMLParagraphElement>(null)
  const [code, setCode] = useState(''); const [name, setName] = useState(''); const [description, setDescription] = useState('')
  const [schema, setSchema] = useState<FormSchema>({ components: [] }); const [submitted, setSubmitted] = useState(false)
  const errors = validateSchema(schema); const valid = code.trim() && name.trim() && !errors.length && schemaByteCount(schema) <= schemaLimits.bytes
  const create = useMutation({ mutationFn: () => createFormTemplate({ code: code.trim(), name: name.trim(), description: description.trim() || null, schemaJson: serializeSchema(schema) }), onSuccess: async result => { await queryClient.invalidateQueries({ queryKey: ['form-templates'] }); navigate(`/form-templates/${result.template.id}?version=${result.version.versionNumber}`) } })
  const submit = (event: FormEvent) => {
    event.preventDefault(); setSubmitted(true)
    if (valid) { create.mutate(); return }
    requestAnimationFrame(() => {
      if (!code.trim()) codeInput.current?.focus()
      else if (!name.trim()) nameInput.current?.focus()
      else validationSummary.current?.focus()
    })
  }
  return <><Link className="back-link" to="/form-templates">← Danh sách template</Link><header className="page-head"><div><p className="kicker">New draft</p><h1>Tạo Form Template</h1><p>Template mới luôn bắt đầu với Draft version 1.</p></div></header>
    <form className="template-form" onSubmit={submit} noValidate>
      <section className="template-metadata"><h2>Thông tin template</h2><label>Mã template<input ref={codeInput} value={code} maxLength={100} required aria-invalid={submitted && !code.trim()} aria-describedby={submitted && !code.trim() ? 'code-error' : undefined} onChange={event => setCode(event.target.value)} />{submitted && !code.trim() && <span id="code-error" className="field-error">Mã template là bắt buộc.</span>}</label><label>Tên template<input ref={nameInput} value={name} maxLength={200} required aria-invalid={submitted && !name.trim()} aria-describedby={submitted && !name.trim() ? 'name-error' : undefined} onChange={event => setName(event.target.value)} />{submitted && !name.trim() && <span id="name-error" className="field-error">Tên template là bắt buộc.</span>}</label><label>Mô tả<textarea value={description} rows={3} onChange={event => setDescription(event.target.value)} /></label></section>
      <FormSchemaEditor value={schema} onChange={setSchema} />
      {submitted && !valid && <p ref={validationSummary} className="notice error" role="alert" tabIndex={-1}>Hãy nhập mã, tên và sửa toàn bộ lỗi schema trước khi tạo.</p>}
      {create.error && <p className="notice error" role="alert">{templateErrorMessage(create.error, 'Không thể tạo template.')}</p>}
      <div className="form-actions"><Link className="button action-link" to="/form-templates">Hủy</Link><button className="button primary" disabled={create.isPending} type="submit">{create.isPending ? 'Đang tạo…' : 'Tạo Draft v1'}</button></div>
    </form></>
}
