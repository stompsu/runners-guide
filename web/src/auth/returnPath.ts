import { readPeriod } from '../insights/period'

export function safeReturnPath(value: unknown, fallback: string) {
  if (typeof value !== 'string') return fallback
  if (/^\/diary\/\d{4}\/\d{2}$/.test(value)) return value
  if (!value.startsWith('/insights') || value.includes('#')) return fallback
  const url = new URL(value, 'https://diary.invalid')
  if (url.origin !== 'https://diary.invalid' || url.pathname !== '/insights'
    || [...url.searchParams.keys()].some(key => !['from', 'to', 'bucket'].includes(key))
    || ['from', 'to', 'bucket'].some(key => url.searchParams.getAll(key).length > 1)
    || !readPeriod(url.searchParams)) return fallback
  return url.pathname + url.search
}
