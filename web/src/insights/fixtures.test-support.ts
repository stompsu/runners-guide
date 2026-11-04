import type { components } from '../api/generated/openapi'

type Schemas = components['schemas']
export function metricCoverage(value = 0, missing = 0): Schemas['MetricCoverage'] {
  return { knownSubtotal: value, eligibleCount: 3, contributingCount: 3 - missing, missingCount: missing, isComplete: missing === 0 }
}
export function insightFixture(): Schemas['InsightsResponse'] {
  const totals: Schemas['InsightTotals'] = {
    sessionCount: 3, restSessionCount: 2, runningDistanceMeters: metricCoverage(12555, 1),
    durationSeconds: metricCoverage(4567), elevationGainMeters: metricCoverage(321),
    elevationLossMeters: metricCoverage(123), load: metricCoverage(2100),
  }
  const period: Schemas['InsightPeriod'] = {
    from: '2026-10-01', to: '2026-10-14', totals,
    easyPace: {
      secondsPerKilometer: { value: 337.123, reason: null },
      eligibleCount: 2, contributingCount: 1, missingCount: 1, isComplete: false,
    },
  }
  const comparison: Schemas['PeriodComparison'] = {
    currentFrom: '2026-10-12', currentTo: '2026-10-14', previousFrom: '2026-10-05', previousTo: '2026-10-07',
    runningDistanceMeters: { current: metricCoverage(12555), previous: metricCoverage(0), percentageChange: { value: null, reason: 'zeroBaseline' } },
    durationSeconds: { current: metricCoverage(4567), previous: metricCoverage(1000), percentageChange: { value: 356.7, reason: null } },
    elevationGainMeters: { current: metricCoverage(321), previous: metricCoverage(100), percentageChange: { value: 221, reason: null } },
    load: { current: metricCoverage(2100), previous: metricCoverage(1500), percentageChange: { value: 40, reason: null } },
  }
  return {
    from: period.from, to: period.to, bucket: 'week', selected: period,
    daily: [period], buckets: [{ periodStart: '2026-09-28', isoWeekYear: 2026, isoWeekNumber: 40, period }],
    yearToDate: { ...period, from: '2026-01-01' },
    weekComparison: comparison, monthComparison: { ...comparison, currentFrom: '2026-10-01', previousFrom: '2026-09-01', previousTo: '2026-09-14' },
    distribution: [{ typeCode: 'easy-run', sessionCount: 2, share: 2 / 3 }, { typeCode: 'cross-training', sessionCount: 1, share: 1 / 3 }],
    trainingLoad: {
      acute7: metricCoverage(2100), chronic28: metricCoverage(5600), chronicWeeklyKnownSubtotal: 1400,
      acwr: { value: 1.5, reason: null }, band: null, bandReason: 'insufficientHistory',
      monotony: { value: null, reason: 'zeroVariance' }, firstCompletedActivityDate: '2026-10-01',
    },
    loadSeries: [
      {
        date: '2026-10-13', acute7: metricCoverage(1800), chronic28: metricCoverage(4800), chronicWeeklyKnownSubtotal: 1200,
        acwr: { value: 1.25, reason: null }, band: 'optimal', bandReason: null, monotony: { value: 1.7, reason: null },
      },
      {
        date: '2026-10-14', acute7: metricCoverage(2100), chronic28: metricCoverage(5600, 1), chronicWeeklyKnownSubtotal: 1400,
        acwr: { value: null, reason: 'missingData' }, band: null, bandReason: 'missingData', monotony: { value: null, reason: 'zeroVariance' },
      },
    ],
  }
}

export function raceFixture(name: string | null = 'Autumn race'): Schemas['RaceHistoryResponse'] {
  return {
    items: [{
      id: 'race-1', startAtUtc: '2026-10-04T08:30:00Z', runTypeCode: 'road-race',
      distanceMeters: 10000, durationSeconds: 2401, raceName: name,
      officialDistanceCategory: null, resultTimeSeconds: null, overallPlace: null, categoryPlace: null,
    }],
    nextCursor: 'opaque-next',
  }
}
