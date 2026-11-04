import { queryOptions } from '@tanstack/react-query'
import { apiClient } from '../api/client'
import { en } from '../i18n'
import type { Period } from './period'

export function insightsOptions(period: Period) {
  return queryOptions({
    queryKey: ['insights', 'summary', period],
    queryFn: async ({ signal }) => {
      const { data, response } = await apiClient.GET('/api/insights', {
        params: { query: period }, signal,
      })
      if (!data) throw new Error(response.status === 401 ? en.expired : en.insights.failed)
      return data
    },
  })
}

export function racesOptions(to: string, cursor?: string) {
  return queryOptions({
    queryKey: ['insights', 'races', to, cursor ?? null],
    queryFn: async ({ signal }) => {
      const { data, response } = await apiClient.GET('/api/insights/races', {
        params: { query: { to, pageSize: 25, cursor } }, signal,
      })
      if (!data) throw new Error(response.status === 401 ? en.expired : en.insights.racesFailed)
      return data
    },
  })
}
