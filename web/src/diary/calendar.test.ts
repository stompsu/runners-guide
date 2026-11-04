import { describe, expect, it } from 'vitest'
import { dateKey, isoWeek, monthDays, weekTotals } from './calendar'
import type { Run } from './calendar'

describe('UTC calendar calculations', () => {
  it('includes complete ISO weeks across years and leap days', () => {
    const days = monthDays(2027, 1)
    expect(dateKey(days[0])).toBe('2026-12-28')
    expect(isoWeek(days[0])).toEqual({ year: 2026, week: 53 })
    expect(dateKey(days.at(-1)!)).toBe('2027-01-31')
    expect(monthDays(2028, 2).some(date => dateKey(date) === '2028-02-29')).toBe(true)
    expect(monthDays(2027, 2)).toHaveLength(28)
  })
  it('counts completed time and elevation but excludes planned sessions and non-running distance', () => {
    const runs = [
      { status: 'Completed', runTypeCode: 'easy-run', distanceMeters: '5000', durationSeconds: '1800', elevationGainMeters: '30' },
      { status: 'Planned', runTypeCode: 'road-race', distanceMeters: 42000, durationSeconds: 12000, elevationGainMeters: 999 },
      { status: 'Completed', runTypeCode: 'cross-training', distanceMeters: 20000, durationSeconds: 3600, elevationGainMeters: null },
      { status: 'Completed', runTypeCode: 'rest-injury', distanceMeters: null, durationSeconds: null, elevationGainMeters: null },
    ] as Run[]
    expect(weekTotals(runs)).toEqual({ distance: 5000, seconds: 5400, elevation: 30 })
  })
})
