import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { configureApi, createFormTemplate, createFormTemplateVersion, deactivateFormTemplate, getFormTemplates, getFormTemplateVersion, getFormTemplateVersions, publishFormTemplateDraft, updateFormTemplateDraft } from './client'

describe('Form Template API client', () => {
  beforeEach(() => {
    configureApi(() => 'opaque-test-token', vi.fn())
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: true, json: () => Promise.resolve({}) }))
  })
  afterEach(() => vi.unstubAllGlobals())

  it('uses exactly the list, history and detail GET contracts and forwards cancellation', async () => {
    const controller = new AbortController()
    await getFormTemplates(controller.signal); await getFormTemplateVersions('a/b', controller.signal); await getFormTemplateVersion('a/b', 3, controller.signal)
    const calls = vi.mocked(fetch).mock.calls
    expect(calls.map(call => [String(call[0]).replace(/^.*\/api/, '/api'), call[1]?.method ?? 'GET'])).toEqual([
      ['/api/form-templates?pageNumber=1&pageSize=100', 'GET'], ['/api/form-templates/a%2Fb/versions?pageNumber=1&pageSize=100', 'GET'], ['/api/form-templates/a%2Fb/versions/3', 'GET'],
    ])
    expect(calls.every(call => call[1]?.signal === controller.signal)).toBe(true)
    expect(calls.every(call => (call[1]?.headers as Record<string, string> | undefined)?.Authorization === 'Bearer opaque-test-token')).toBe(true)
    expect(calls.every(call => !String(call[0]).includes('opaque-test-token'))).toBe(true)
  })

  it('uses the five mutation contracts with rowVersion payloads', async () => {
    await createFormTemplate({ code: 'SAF', name: 'Safety', schemaJson: '{"components":[]}' })
    await updateFormTemplateDraft('id', 1, '{}', 'version-rv')
    await publishFormTemplateDraft('id', 1, 'template-rv', 'version-rv')
    await createFormTemplateVersion('id', '{}', 'template-rv')
    await deactivateFormTemplate('id', 'template-rv')
    const calls = vi.mocked(fetch).mock.calls.map(call => ({ path: String(call[0]).replace(/^.*\/api/, '/api'), method: call[1]?.method, body: JSON.parse(String(call[1]?.body)) }))
    expect(calls).toEqual([
      { path: '/api/form-templates', method: 'POST', body: { code: 'SAF', name: 'Safety', schemaJson: '{"components":[]}' } },
      { path: '/api/form-templates/id/versions/1', method: 'PUT', body: { schemaJson: '{}', rowVersion: 'version-rv' } },
      { path: '/api/form-templates/id/versions/1/publish', method: 'POST', body: { templateRowVersion: 'template-rv', versionRowVersion: 'version-rv' } },
      { path: '/api/form-templates/id/versions', method: 'POST', body: { schemaJson: '{}', templateRowVersion: 'template-rv' } },
      { path: '/api/form-templates/id/deactivate', method: 'POST', body: { rowVersion: 'template-rv' } },
    ])
  })
})
