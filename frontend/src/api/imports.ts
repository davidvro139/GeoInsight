import { apiFetch } from './client'

export interface ColumnMapping {
  csvColumn: string
  systemField: string | null
}

export interface ImportUploadResult {
  importId: number
  headers: string[]
  sampleRows: string[][]
  detectedDelimiter: string
  totalRows: number
}

export interface ImportRowIssue {
  rowNumber: number
  severity: 'Valid' | 'Warning' | 'Invalid'
  message: string
}

export interface ImportSummary {
  importId: number
  status: string
  totalRows: number
  validRows: number
  warningRows: number
  invalidRows: number
  issues: ImportRowIssue[]
}

export interface ImportListItem {
  id: number
  originalFileName: string
  status: string
  totalRows: number
  validRows: number
  warningRows: number
  invalidRows: number
  createdAt: string
  completedAt: string | null
}

export async function uploadImport(
  organizationId: number,
  datasetId: number,
  file: File,
): Promise<ImportUploadResult> {
  const formData = new FormData()
  formData.append('file', file)
  return apiFetch<ImportUploadResult>(
    `/api/organizations/${organizationId}/datasets/${datasetId}/imports`,
    { method: 'POST', body: formData },
  )
}

export function getImports(organizationId: number, datasetId: number): Promise<ImportListItem[]> {
  return apiFetch<ImportListItem[]>(`/api/organizations/${organizationId}/datasets/${datasetId}/imports`)
}

export function validateImport(
  organizationId: number,
  datasetId: number,
  importId: number,
  delimiter: string,
  mappings: ColumnMapping[],
): Promise<ImportSummary> {
  return apiFetch<ImportSummary>(
    `/api/organizations/${organizationId}/datasets/${datasetId}/imports/${importId}/validate`,
    { method: 'POST', body: JSON.stringify({ delimiter, mappings }) },
  )
}

export function commitImport(
  organizationId: number,
  datasetId: number,
  importId: number,
  delimiter: string,
  mappings: ColumnMapping[],
): Promise<ImportSummary> {
  return apiFetch<ImportSummary>(
    `/api/organizations/${organizationId}/datasets/${datasetId}/imports/${importId}/commit`,
    { method: 'POST', body: JSON.stringify({ delimiter, mappings }) },
  )
}
