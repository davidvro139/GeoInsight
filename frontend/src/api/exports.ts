import { getStoredToken } from './client'
import type { RecordFilters } from './recordFilters'
import { apiFetch, ApiError } from './client'

const API_BASE_URL = import.meta.env.VITE_API_URL ?? 'http://localhost:5035'

export type ExportType = 'Csv' | 'Xlsx' | 'GeoJson' | 'Png' | 'Pdf'

export interface ExportRecord {
  id: number
  exportType: ExportType
  status: string
  createdAt: string
  completedAt: string | null
}

export function createDataExport(
  organizationId: number,
  datasetId: number,
  exportType: 'Csv' | 'Xlsx' | 'GeoJson',
  filters: RecordFilters = {},
): Promise<ExportRecord> {
  return apiFetch<ExportRecord>(`/api/organizations/${organizationId}/datasets/${datasetId}/exports`, {
    method: 'POST',
    body: JSON.stringify({ exportType, filters, fileContentBase64: null }),
  })
}

export function createFileExport(
  organizationId: number,
  datasetId: number,
  exportType: 'Png' | 'Pdf',
  fileContentBase64: string,
): Promise<ExportRecord> {
  return apiFetch<ExportRecord>(`/api/organizations/${organizationId}/datasets/${datasetId}/exports`, {
    method: 'POST',
    body: JSON.stringify({ exportType, filters: null, fileContentBase64 }),
  })
}

export function getExports(organizationId: number, datasetId: number): Promise<ExportRecord[]> {
  return apiFetch<ExportRecord[]>(`/api/organizations/${organizationId}/datasets/${datasetId}/exports`)
}

/**
 * Downloads/re-downloads a stored export. Can't be a plain <a href> link —
 * the API only accepts a JWT bearer header, no cookie/query-token auth — so
 * this fetches with the header manually and saves the response via a
 * synthetic <a download> + object URL.
 */
export async function downloadExport(organizationId: number, datasetId: number, exportId: number, suggestedFileName: string): Promise<void> {
  const token = getStoredToken()
  const headers = new Headers()
  if (token) headers.set('Authorization', `Bearer ${token}`)

  const response = await fetch(
    `${API_BASE_URL}/api/organizations/${organizationId}/datasets/${datasetId}/exports/${exportId}/download`,
    { headers },
  )
  if (!response.ok) {
    throw new ApiError(response.statusText, response.status)
  }

  const disposition = response.headers.get('Content-Disposition')
  const fileName = disposition?.match(/filename="?([^"]+)"?/)?.[1] ?? suggestedFileName
  const blob = await response.blob()
  saveBlob(blob, fileName)
}

export function saveBlob(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob)
  saveDataUrl(url, fileName)
  URL.revokeObjectURL(url)
}

export function saveDataUrl(url: string, fileName: string): void {
  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  document.body.appendChild(link)
  link.click()
  link.remove()
}

export function base64FromDataUrl(dataUrl: string): string {
  return dataUrl.slice(dataUrl.indexOf(',') + 1)
}
