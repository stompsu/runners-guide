import { useMutation, useQueryClient } from '@tanstack/react-query'
import { apiClient } from '../api/client'
import { en } from '../i18n'
import type { RunInput } from './calendar'
import type { components } from '../api/generated/openapi'

export async function listRuns(from: string, to: string, signal?: AbortSignal) {
  const { data, response } = await apiClient.GET('/api/runs', {
    params: { query: { from, to } }, signal,
  })
  if (!data) throw new Error(response.status === 401 ? en.expired : en.requestFailed)
  return data
}

function failure(response: Response, error: unknown) {
  if (response.status === 401) return new Error(en.expired)
  if (response.status === 404) return new Error(en.panel.stale)
  const problem = error as components['schemas']['HttpValidationProblemDetails'] | undefined
  return new Error(problem?.errors
    ? Object.values(problem.errors).flat().join(' ') : en.requestFailed)
}

export function useRunMutation() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: async ({ id, body }: { id?: string; body?: RunInput }) => {
      const result = body
        ? id
          ? await apiClient.PUT('/api/runs/{id}', { params: { path: { id } }, body })
          : await apiClient.POST('/api/runs', { body })
        : await apiClient.DELETE('/api/runs/{id}', { params: { path: { id: id! } } })
      if (!result.response.ok) throw failure(result.response, result.error)
    },
    onSuccess: () => client.invalidateQueries({ queryKey: ['runs'] }),
  })
}
