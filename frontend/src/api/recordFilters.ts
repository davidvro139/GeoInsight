// Shared by records.ts and tracts.ts — one filter shape, one query-string
// builder, so the map, table, and choropleth requests can never drift apart.
export interface RecordFilters {
  dateFrom?: string
  dateTo?: string
  category?: string
  minAmount?: number
  maxAmount?: number
  city?: string
  state?: string
  countyFips?: string
  tract?: string
  geocodeStatus?: string
  minMedianIncome?: number
  maxMedianIncome?: number
}

export function appendFilterParams(query: URLSearchParams, filters: RecordFilters = {}): void {
  if (filters.dateFrom) query.set('dateFrom', filters.dateFrom)
  if (filters.dateTo) query.set('dateTo', filters.dateTo)
  if (filters.category) query.set('category', filters.category)
  if (filters.minAmount != null) query.set('minAmount', String(filters.minAmount))
  if (filters.maxAmount != null) query.set('maxAmount', String(filters.maxAmount))
  if (filters.city) query.set('city', filters.city)
  if (filters.state) query.set('state', filters.state)
  if (filters.countyFips) query.set('countyFips', filters.countyFips)
  if (filters.tract) query.set('tract', filters.tract)
  if (filters.geocodeStatus) query.set('geocodeStatus', filters.geocodeStatus)
  if (filters.minMedianIncome != null) query.set('minMedianIncome', String(filters.minMedianIncome))
  if (filters.maxMedianIncome != null) query.set('maxMedianIncome', String(filters.maxMedianIncome))
}
