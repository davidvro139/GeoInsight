import { appendFilterParams, type RecordFilters } from './recordFilters'
import { apiFetch } from './client'

export interface DatasetRecordPoint {
  id: number
  latitude: number
  longitude: number
  externalId: string | null
  category: string | null
  censusTract: string | null
  censusGeoid: string | null
}

export interface DatasetRecordListItem {
  id: number
  externalId: string | null
  addressLine1: string
  city: string
  state: string
  postalCode: string | null
  category: string | null
  amount: number | null
  recordDate: string | null
  status: string | null
  geocodeStatus: string
}

export interface PagedResult<T> {
  items: T[]
  totalCount: number
  page: number
  pageSize: number
}

export function getRecordPoints(
  organizationId: number,
  datasetId: number,
  filters: RecordFilters = {},
): Promise<DatasetRecordPoint[]> {
  const query = new URLSearchParams()
  appendFilterParams(query, filters)
  const queryString = query.toString()
  return apiFetch<DatasetRecordPoint[]>(
    `/api/organizations/${organizationId}/datasets/${datasetId}/records/points${queryString ? `?${queryString}` : ''}`,
  )
}

export interface GetRecordsParams {
  page?: number
  pageSize?: number
  sortBy?: string
  sortDir?: 'asc' | 'desc'
  filters?: RecordFilters
}

export function getRecords(
  organizationId: number,
  datasetId: number,
  params: GetRecordsParams = {},
): Promise<PagedResult<DatasetRecordListItem>> {
  const query = new URLSearchParams()
  if (params.page) query.set('page', String(params.page))
  if (params.pageSize) query.set('pageSize', String(params.pageSize))
  if (params.sortBy) query.set('sortBy', params.sortBy)
  if (params.sortDir) query.set('sortDir', params.sortDir)
  appendFilterParams(query, params.filters)

  const queryString = query.toString()
  return apiFetch<PagedResult<DatasetRecordListItem>>(
    `/api/organizations/${organizationId}/datasets/${datasetId}/records${queryString ? `?${queryString}` : ''}`,
  )
}
