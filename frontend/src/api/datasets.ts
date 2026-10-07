import { apiFetch } from './client'

export interface DatasetSummary {
  id: number
  name: string
  datasetType: string
  isArchived: boolean
  updatedAt: string
  recordCount: number
  geocodedCount: number
}

export interface DatasetDetail {
  id: number
  name: string
  description: string | null
  datasetType: string
  isArchived: boolean
  createdAt: string
  updatedAt: string
  createdByDisplayName: string
}

export interface DatasetInput {
  name: string
  description?: string | null
  datasetType?: string | null
}

export function getDatasets(organizationId: number, includeArchived = false): Promise<DatasetSummary[]> {
  const query = includeArchived ? '?includeArchived=true' : ''
  return apiFetch<DatasetSummary[]>(`/api/organizations/${organizationId}/datasets${query}`)
}

export function createDataset(organizationId: number, input: DatasetInput): Promise<DatasetDetail> {
  return apiFetch<DatasetDetail>(`/api/organizations/${organizationId}/datasets`, {
    method: 'POST',
    body: JSON.stringify(input),
  })
}

export function getDataset(organizationId: number, datasetId: number): Promise<DatasetDetail> {
  return apiFetch<DatasetDetail>(`/api/organizations/${organizationId}/datasets/${datasetId}`)
}

export function updateDataset(
  organizationId: number,
  datasetId: number,
  input: DatasetInput,
): Promise<DatasetDetail> {
  return apiFetch<DatasetDetail>(`/api/organizations/${organizationId}/datasets/${datasetId}`, {
    method: 'PUT',
    body: JSON.stringify(input),
  })
}

export function archiveDataset(organizationId: number, datasetId: number): Promise<boolean> {
  return apiFetch<boolean>(`/api/organizations/${organizationId}/datasets/${datasetId}/archive`, {
    method: 'POST',
  })
}

export function unarchiveDataset(organizationId: number, datasetId: number): Promise<boolean> {
  return apiFetch<boolean>(`/api/organizations/${organizationId}/datasets/${datasetId}/unarchive`, {
    method: 'POST',
  })
}
