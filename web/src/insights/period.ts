export type Period = { from: string; to: string; bucket: 'week' | 'month' }

export function utcToday() {
  return new Date().toISOString().slice(0, 10)
}

export function defaultPeriod(today = utcToday()): Period {
  return { from: `${today.slice(0, 7)}-01`, to: today, bucket: 'week' }
}

function validDate(value: string) {
  return /^\d{4}-\d{2}-\d{2}$/.test(value) && value >= '0002-01-01'
    && Number.isFinite(Date.parse(`${value}T00:00:00Z`))
    && new Date(`${value}T00:00:00Z`).toISOString().slice(0, 10) === value
}

export function validPeriod(period: Period, today = utcToday()) {
  return validDate(period.from) && validDate(period.to) && period.from <= period.to
    && period.to <= today
    && (Date.parse(period.to) - Date.parse(period.from)) / 86400000 <= 366
    && (period.bucket === 'week' || period.bucket === 'month')
}

export function readPeriod(params: URLSearchParams, today = utcToday()): Period | null {
  const defaults = defaultPeriod(today)
  const period = {
    from: params.get('from') ?? defaults.from,
    to: params.get('to') ?? defaults.to,
    bucket: params.get('bucket') ?? defaults.bucket,
  }
  if (period.bucket !== 'week' && period.bucket !== 'month') return null
  const result: Period = { ...period, bucket: period.bucket }
  return validPeriod(result, today) ? result : null
}

export function periodSearch(period: Period) {
  return new URLSearchParams(period).toString()
}
