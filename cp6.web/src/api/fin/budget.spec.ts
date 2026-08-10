import { beforeEach, describe, expect, it, vi } from 'vitest'
import http from '@/api/http'
import { budgetLineApi } from './budget'

vi.mock('@/api/http', () => ({
  default: {
    post: vi.fn(),
    delete: vi.fn(),
  },
}))

describe('budgetLineApi concurrency tokens', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(http.post).mockResolvedValue({})
    vi.mocked(http.delete).mockResolvedValue({})
  })

  it('passes the version token in an upsert body', async () => {
    await budgetLineApi.upsert({
      versionId: 'version-1',
      accountId: 'account-1',
      annualAmount: 1200,
      spreadMode: 'even',
      versionRowVersion: 'version-token',
    })

    expect(http.post).toHaveBeenCalledWith('/fin/budget/lines', expect.objectContaining({
      versionRowVersion: 'version-token',
    }))
  })

  it('passes line and version tokens when deleting', async () => {
    await budgetLineApi.remove('line-1', 'line-token', 'version-token')

    expect(http.delete).toHaveBeenCalledWith('/fin/budget/lines/line-1', {
      params: { lineRowVersion: 'line-token', versionRowVersion: 'version-token' },
    })
  })

  it('passes the version token when confirming an import', async () => {
    const file = new File(['budget'], 'budget.xlsx')
    await budgetLineApi.importConfirm('version-1', 'version-token', file)

    expect(http.post).toHaveBeenCalledWith(
      '/fin/budget/lines/import/confirm',
      expect.any(FormData),
      { params: { versionId: 'version-1', versionRowVersion: 'version-token' } },
    )
  })
})
