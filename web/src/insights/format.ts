import type { components } from '../api/generated/openapi'
import { en, locale } from '../i18n'

type Numeric = number | string | null | undefined
export function finite(value: Numeric) {
  if (value === null || value === undefined || value === '') return null
  const parsed = Number(value)
  return Number.isFinite(parsed) ? parsed : null
}

export function number(value: Numeric, digits = 2) {
  const parsed = finite(value)
  return parsed === null ? en.insights.unavailable
    : new Intl.NumberFormat(locale, { maximumFractionDigits: digits }).format(parsed)
}

export type Unit = 'km' | 'minutes' | 'meters' | 'load' | 'ratio' | 'percent'
export function scaled(value: Numeric, unit: Unit) {
  const parsed = finite(value)
  if (parsed === null) return null
  return parsed / (unit === 'km' ? 1000 : unit === 'minutes' ? 60 : 1)
}
export function unitLabel(unit: Unit) {
  const t = en.insights
  return { km: t.km, minutes: t.minutes, meters: t.meters, load: t.loadUnit, ratio: t.ratioUnit, percent: '%' }[unit]
}
export function metric(value: Numeric, unit: Unit) {
  const parsed = scaled(value, unit)
  return parsed === null ? en.insights.unavailable : `${number(parsed)} ${unitLabel(unit)}`
}
export function reason(value: components['schemas']['UnavailableReason'] | null) {
  return value ? en.insights.reasons[value] : en.insights.unavailable
}
export function derived(value: components['schemas']['DerivedMetric'], unit: Unit = 'ratio') {
  return finite(value.value) === null ? `${en.insights.unavailable} — ${reason(value.reason)}`
    : metric(value.value, unit)
}
export function coverage(value: Pick<components['schemas']['MetricCoverage'], 'contributingCount' | 'eligibleCount' | 'missingCount' | 'isComplete'>) {
  return `${number(value.contributingCount, 0)} / ${number(value.eligibleCount, 0)} · ${
    value.isComplete === true ? en.insights.complete : value.isComplete === false ? en.insights.incomplete : en.insights.unavailable
  }${value.missingCount === undefined ? '' : ` · ${en.insights.missing(number(value.missingCount, 0))}`}`
}
export function pace(value: components['schemas']['DerivedMetric']) {
  const seconds = finite(value.value)
  if (seconds === null) return `${en.insights.unavailable} — ${reason(value.reason)}`
  // Decimal min/km preserves the exact server-weighted value rather than averaging rounded paces.
  return `${number(seconds / 60, 3)} ${en.insights.minPerKm}`
}
export function raceTime(value: Numeric) {
  const seconds = finite(value)
  if (seconds === null) return en.insights.unavailable
  return `${Math.floor(seconds / 3600)}:${String(Math.floor(seconds % 3600 / 60)).padStart(2, '0')}:${String(Math.round(seconds % 60)).padStart(2, '0')}`
}
