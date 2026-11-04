import { describe, expect, it } from 'vitest'
import { defaultPeriod, readPeriod, validPeriod } from './period'
import { safeReturnPath } from '../auth/returnPath'

describe('Insights bookmarks', () => {
  it('defaults to the UTC calendar month through today', () => {
    expect(defaultPeriod('2026-11-04')).toEqual({ from: '2026-11-01', to: '2026-11-04', bucket: 'week' })
  })
  it.each([
    'from=2026-02-30&to=2026-03-01',
    'from=2026-11-05&to=2026-11-04',
    'from=2026-11-01&to=2026-11-05',
    'from=2025-01-01&to=2026-11-04',
    'from=0001-01-01&to=0001-01-02',
    'bucket=day',
  ])('rejects invalid API periods: %s', query => {
    expect(readPeriod(new URLSearchParams(query), '2026-11-04')).toBeNull()
  })
  it('accepts leap dates and the API inclusive limit', () => {
    expect(validPeriod({ from: '2024-02-29', to: '2025-03-01', bucket: 'month' }, '2026-11-04')).toBe(true)
  })
  it('preserves valid local Insights return paths and rejects external or malformed paths', () => {
    const bookmark = '/insights?from=2024-02-01&to=2024-02-29&bucket=month'
    expect(safeReturnPath(bookmark, '/diary')).toBe(bookmark)
    for (const unsafe of ['//evil.invalid/insights', '/insights/other', '/insights?to=bad', '/insights?next=https://evil.invalid',
      '/insights?bucket=week&bucket=month', '/insights#x', 'https://evil.invalid/insights']) {
      expect(safeReturnPath(unsafe, '/diary')).toBe('/diary')
    }
  })
})
