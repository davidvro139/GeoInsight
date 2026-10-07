import { appendFilterParams, type RecordFilters } from './recordFilters'
import { apiFetch } from './client'

export interface HmdaCountyBenchmark {
  stateFips: string
  countyFips: string
  datasetRecordCount: number
  datasetTotalAmount: number
  marketOriginatedLoanCount: number | null
  marketTotalAmount: number | null
  datasetSharePercent: number | null
}

export function getHmdaBenchmark(
  organizationId: number,
  datasetId: number,
  year: number,
  filters: RecordFilters = {},
): Promise<HmdaCountyBenchmark[]> {
  const query = new URLSearchParams({ year: String(year) })
  appendFilterParams(query, filters)
  return apiFetch<HmdaCountyBenchmark[]>(
    `/api/organizations/${organizationId}/datasets/${datasetId}/hmda-benchmark?${query.toString()}`,
  )
}
