import type { jsPDF } from 'jspdf'
import type { RecordFilters } from '@/api/recordFilters'

// Shared by every PDF report this app generates (AnalyticsPage's general
// report, GapAnalysisPage's CRA compliance report) so a page's active
// filters always read the same way in a downloaded document as they do in
// the RecordFilterSidebar that produced them.
export function buildActiveFiltersSummary(filters: RecordFilters): string {
  const parts: string[] = []
  if (filters.dateFrom) parts.push(`from ${filters.dateFrom}`)
  if (filters.dateTo) parts.push(`to ${filters.dateTo}`)
  if (filters.category) parts.push(`category contains "${filters.category}"`)
  if (filters.minAmount != null) parts.push(`amount ≥ ${filters.minAmount}`)
  if (filters.maxAmount != null) parts.push(`amount ≤ ${filters.maxAmount}`)
  if (filters.city) parts.push(`city contains "${filters.city}"`)
  if (filters.state) parts.push(`state contains "${filters.state}"`)
  if (filters.countyFips) parts.push(`county ${filters.countyFips}`)
  if (filters.tract) parts.push(`tract ${filters.tract}`)
  if (filters.geocodeStatus) parts.push(`geocode status = ${filters.geocodeStatus}`)
  if (filters.minMedianIncome != null) parts.push(`tract income ≥ ${filters.minMedianIncome}`)
  if (filters.maxMedianIncome != null) parts.push(`tract income ≤ ${filters.maxMedianIncome}`)
  return parts.length > 0 ? parts.join(', ') : 'None'
}

export const PDF_MARGIN = 40

/** Draws the title/org/dataset/generated-at/filters block every report opens with. Returns the y cursor to continue from. */
export function addPageHeader(
  doc: jsPDF,
  params: { title: string; organizationName: string; datasetName: string; filters: RecordFilters },
): number {
  const pageWidth = doc.internal.pageSize.getWidth()
  const margin = PDF_MARGIN
  let y = margin

  doc.setFontSize(18)
  doc.text(params.title, margin, y)
  y += 24
  doc.setFontSize(10)
  doc.text(`Organization: ${params.organizationName}`, margin, y); y += 14
  doc.text(`Dataset: ${params.datasetName}`, margin, y); y += 14
  doc.text(`Generated: ${new Date().toLocaleString()}`, margin, y); y += 14
  doc.text(`Active filters: ${buildActiveFiltersSummary(params.filters)}`, margin, y, { maxWidth: pageWidth - margin * 2 }); y += 24

  return y
}
