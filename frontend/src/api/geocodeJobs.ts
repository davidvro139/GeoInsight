import { apiFetch } from './client'

export interface GeocodeJob {
  id: number
  status: 'Queued' | 'Running' | 'Completed' | 'Failed' | 'Cancelled'
  totalItems: number
  completedItems: number
  successfulItems: number
  failedItems: number
  startedAt: string | null
  completedAt: string | null
  errorMessage: string | null
  createdAt: string
}

export function createGeocodeJob(organizationId: number, datasetId: number): Promise<GeocodeJob> {
  return apiFetch<GeocodeJob>(`/api/organizations/${organizationId}/datasets/${datasetId}/geocode-jobs`, {
    method: 'POST',
  })
}

export function getGeocodeJobs(organizationId: number, datasetId: number): Promise<GeocodeJob[]> {
  return apiFetch<GeocodeJob[]>(`/api/organizations/${organizationId}/datasets/${datasetId}/geocode-jobs`)
}

export function getGeocodeJob(organizationId: number, datasetId: number, jobId: number): Promise<GeocodeJob> {
  return apiFetch<GeocodeJob>(`/api/organizations/${organizationId}/datasets/${datasetId}/geocode-jobs/${jobId}`)
}

export function cancelGeocodeJob(organizationId: number, datasetId: number, jobId: number): Promise<boolean> {
  return apiFetch<boolean>(
    `/api/organizations/${organizationId}/datasets/${datasetId}/geocode-jobs/${jobId}/cancel`,
    { method: 'POST' },
  )
}
