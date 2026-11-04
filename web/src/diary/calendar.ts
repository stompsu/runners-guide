import type { components } from '../api/generated/openapi'
import { locale } from '../i18n'

export type Run = components['schemas']['RunResponse']
export type RunInput = components['schemas']['RunInput']
export const dateKey = (date: Date) => date.toISOString().slice(0, 10)
export const utcDate = (key: string) => new Date(`${key}T00:00:00Z`)
export function addDays(date: Date, amount: number) {
  const next = new Date(date)
  next.setUTCDate(next.getUTCDate() + amount)
  return next
}
export const today = () => dateKey(new Date())
export const monthPath = (date: Date) =>
  `/diary/${date.getUTCFullYear()}/${String(date.getUTCMonth() + 1).padStart(2, '0')}`
export const dateLabel = (date: Date) =>
  date.toLocaleDateString(locale, { dateStyle: 'full', timeZone: 'UTC' })
export const monthLabel = (date: Date) =>
  date.toLocaleDateString(locale, { month: 'long', year: 'numeric', timeZone: 'UTC' })
export function monthDays(year: number, month: number) {
  const first = new Date(Date.UTC(year, month - 1, 1))
  const last = new Date(Date.UTC(year, month, 0))
  const start = addDays(first, -((first.getUTCDay() + 6) % 7))
  const end = addDays(last, 6 - ((last.getUTCDay() + 6) % 7))
  return Array.from({ length: Math.round((end.getTime() - start.getTime()) / 86400000) + 1 },
    (_, i) => addDays(start, i))
}
export function isoWeek(date: Date) {
  const thursday = addDays(date, 3 - ((date.getUTCDay() + 6) % 7))
  const year = thursday.getUTCFullYear()
  return { year, week: Math.ceil(((thursday.getTime() - Date.UTC(year, 0, 1)) / 86400000 + 1) / 7) }
}
export function weekTotals(runs: Run[]) {
  return runs.filter(run => run.status === 'Completed').reduce((total, run) => ({
    distance: total.distance + (['cross-training', 'rest-injury'].includes(run.runTypeCode)
      ? 0 : Number(run.distanceMeters ?? 0)),
    seconds: total.seconds + Number(run.durationSeconds ?? 0),
    elevation: total.elevation + Number(run.elevationGainMeters ?? 0),
  }), { distance: 0, seconds: 0, elevation: 0 })
}
export const km = (meters: unknown) =>
  (Number(meters ?? 0) / 1000).toLocaleString(locale, { maximumFractionDigits: 2 })
export function duration(seconds: unknown) {
  const value = Number(seconds ?? 0)
  return `${Math.floor(value / 3600)}h ${Math.floor(value % 3600 / 60)}m`
}
