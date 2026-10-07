import { cleanup, render, screen } from '@testing-library/react'
import '@testing-library/jest-dom/vitest'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError, getDrawings } from '../api/client'
import { AuthProvider } from '../auth/AuthContext'
import { DrawingsPage } from './DrawingsPage'

vi.mock('../api/client', async importOriginal => {
  const actual = await importOriginal<typeof import('../api/client')>()
  return { ...actual, getDrawings: vi.fn() }
})

const emptyPage = { items: [], pageNumber: 1, pageSize: 50, totalCount: 0, totalPages: 0 }

describe('DrawingsPage capabilities and states', () => {
  afterEach(() => {
    cleanup()
    sessionStorage.clear()
    vi.resetAllMocks()
  })

  it('shows an empty list but no upload capability to a Manager', async () => {
    vi.mocked(getDrawings).mockResolvedValue(emptyPage)
    renderPage(['MANAGER'])

    expect(await screen.findByText('Chưa có Drawing')).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Tải PDF' })).not.toBeInTheDocument()
  })

  it('shows the upload capability to an Administrator', async () => {
    vi.mocked(getDrawings).mockResolvedValue(emptyPage)
    renderPage(['ADMINISTRATOR'])

    expect(await screen.findByRole('heading', { name: 'Tải PDF' })).toBeInTheDocument()
  })

  it('renders an explicit forbidden state', async () => {
    vi.mocked(getDrawings).mockRejectedValue(new ApiError(403))
    renderPage(['INSPECTOR'])

    expect(await screen.findByRole('alert')).toHaveTextContent('không có quyền')
  })
})

function renderPage(roles: string[]) {
  sessionStorage.setItem('construction-inspection.auth', JSON.stringify({
    token: 'test-session-value',
    user: { id: '1', email: 'user@local.test', fullName: 'Test User', roles },
  }))
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter>
      <QueryClientProvider client={queryClient}>
        <AuthProvider><DrawingsPage /></AuthProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}
