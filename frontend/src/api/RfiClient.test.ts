import { afterEach, describe, expect, it, vi } from 'vitest'
import { cancelRfi, closeRfi, configureApi, createRfi, getRfi, getRfiHistory, getRfiResponderOptions, getRfis, reassignRfi, requestRfiClarification, respondRfi, saveRfiDraft, submitRfi } from './client'

afterEach(() => { vi.unstubAllGlobals(); configureApi(() => null, () => undefined) })

describe('RFI API client', () => {
  it('uses all 12 contracts, sends versions in bodies and AbortSignal only in request options', async () => {
    const calls: Array<[string, RequestInit | undefined]> = []
    vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
      calls.push([url, init]); return new Response(JSON.stringify({ items: [], pageNumber: 1, pageSize: 20, totalCount: 0, totalPages: 0 }), { status: init?.method === 'POST' && url.endsWith('/responses') ? 201 : 200, headers: { 'Content-Type': 'application/json' } })
    }))
    configureApi(() => 'opaque-test-token', () => undefined); const signal = new AbortController().signal
    await getRfis({ pageNumber: 1, pageSize: 20, assignedToMe: true }, signal); await getRfi('id', signal); await getRfiResponderOptions(1, 20, 'reviewer', signal)
    await createRfi({ constructionObjectId: 'object', subject: 'Subject', question: 'Question' }); await saveRfiDraft('id', 'Subject', 'Question', 'r1', 'p1'); await submitRfi('id', 'reviewer', 'r2', 'p2'); await reassignRfi('id', 'reviewer-2', 'reason', 'r3', 'p3'); await respondRfi('id', 'response', 'r4', 'p4'); await requestRfiClarification('id', 'clarify', 'r5', 'p5'); await closeRfi('id', null, 'r6', 'p6'); await cancelRfi('id', 'cancel', 'r7', 'p7'); await getRfiHistory('id', 1, 20, signal)
    expect(calls).toHaveLength(12)
    expect(calls.map(([url]) => new URL(url).pathname)).toEqual(['/api/rfis', '/api/rfis/id', '/api/rfis/responder-options', '/api/rfis', '/api/rfis/id/draft', '/api/rfis/id/submit', '/api/rfis/id/assign', '/api/rfis/id/responses', '/api/rfis/id/request-clarification', '/api/rfis/id/close', '/api/rfis/id/cancel', '/api/rfis/id/history'])
    expect(calls[0][0]).toContain('assignedToMe=true'); expect(calls[0][0]).not.toContain('opaque-test-token'); expect(calls[0][1]?.signal).toBe(signal)
    expect(calls.slice(4, 11).map(([, init]) => JSON.parse(String(init?.body)))).toEqual(expect.arrayContaining([expect.objectContaining({ rfiRowVersion: 'r1', processRowVersion: 'p1' }), expect.objectContaining({ rfiRowVersion: 'r7', processRowVersion: 'p7' })]))
    expect(new Headers(calls[0][1]?.headers).get('Authorization')).toBe('Bearer opaque-test-token')
  })
})
