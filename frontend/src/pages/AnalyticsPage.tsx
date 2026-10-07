import { useQuery } from '@tanstack/react-query'
import { Chart, type ChartConfiguration } from 'chart.js/auto'
import { jsPDF } from 'jspdf'
import * as maplibregl from 'maplibre-gl'
import { useEffect, useRef, useState, type MutableRefObject } from 'react'
import { useOutletContext } from 'react-router-dom'
import type { Analytics } from '@/api/analytics'
import { getAnalytics } from '@/api/analytics'
import { base64FromDataUrl, createFileExport } from '@/api/exports'
import type { RecordFilters } from '@/api/recordFilters'
import { getRecordPoints } from '@/api/records'
import { getTractBoundaries } from '@/api/tracts'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { RecordFilterSidebar } from '@/components/RecordFilterSidebar'
import { MAP_STYLE } from '@/lib/mapStyle'
import { CATEGORICAL_COLORS, SEQUENTIAL_BLUE } from '@/lib/chartColors'
import { addPageHeader } from '@/lib/pdfReport'
import { ApiError } from '@/api/client'
import { LoadingState, ErrorState } from '@/components/StateViews'
import type { DatasetOutletContext } from './DatasetLayout'

function useChart(config: ChartConfiguration | null, exportRef?: MutableRefObject<Chart | null>) {
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const chartRef = useRef<Chart | null>(null)

  useEffect(() => {
    if (!canvasRef.current || !config) return

    chartRef.current?.destroy()
    const chart = new Chart(canvasRef.current, config)
    chartRef.current = chart
    if (exportRef) exportRef.current = chart

    return () => {
      chart.destroy()
      chartRef.current = null
      if (exportRef) exportRef.current = null
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [config])

  return canvasRef
}

interface ChartProps {
  labels: string[]
  data: number[]
  label: string
  exportRef?: MutableRefObject<Chart | null>
}

function BarChart({ labels, data, label, exportRef }: ChartProps) {
  const config: ChartConfiguration | null = labels.length > 0
    ? {
        type: 'bar',
        data: { labels, datasets: [{ label, data, backgroundColor: CATEGORICAL_COLORS }] },
        options: { responsive: true, plugins: { legend: { display: false } } },
      }
    : null
  const canvasRef = useChart(config, exportRef)

  if (labels.length === 0) {
    return <p className="text-sm text-muted-foreground">No data yet.</p>
  }
  return <canvas ref={canvasRef} height={180} />
}

function LineChart({ labels, data, label, exportRef }: ChartProps) {
  const config: ChartConfiguration | null = labels.length > 0
    ? {
        type: 'line',
        data: { labels, datasets: [{ label, data, borderColor: SEQUENTIAL_BLUE[400], backgroundColor: SEQUENTIAL_BLUE[100], tension: 0.2 }] },
        options: { responsive: true, plugins: { legend: { display: false } } },
      }
    : null
  const canvasRef = useChart(config, exportRef)

  if (labels.length === 0) {
    return <p className="text-sm text-muted-foreground">No dated records yet.</p>
  }
  return <canvas ref={canvasRef} height={180} />
}

/**
 * Mounts a MapLibre instance off-screen just long enough to render the
 * filtered points + this dataset's already-imported tract outlines and
 * capture a static image for the PDF report — a simplified, non-interactive
 * snapshot, not the Map tab's full clustering/choropleth machinery, since
 * duplicating that for a one-off image isn't worth it. Resolves with null if
 * there's nothing to show (no points and no tracts).
 */
async function captureReportMapImage(
  organizationId: number, datasetId: number, filters: RecordFilters,
): Promise<string | null> {
  const [points, boundaries] = await Promise.all([
    getRecordPoints(organizationId, datasetId, filters),
    getTractBoundaries(organizationId, datasetId).catch(() => []),
  ])

  if (points.length === 0 && boundaries.length === 0) return null

  const container = document.createElement('div')
  container.style.cssText = 'position:fixed;top:-9999px;left:-9999px;width:760px;height:420px;'
  document.body.appendChild(container)

  try {
    // preserveDrawingBuffer: WebGL clears its drawing buffer after
    // compositing by default, so getCanvas().toDataURL() below would
    // otherwise come back blank despite the map rendering correctly.
    const map = new maplibregl.Map({
      container, style: MAP_STYLE, center: [-111.9, 41.3], zoom: 6, interactive: false,
      canvasContextAttributes: { preserveDrawingBuffer: true },
    })

    const imageDataUrl = await new Promise<string>((resolve, reject) => {
      map.on('error', (e) => reject(e.error ?? new Error('Map failed to load')))
      map.on('load', () => {
        map.addSource('tracts', {
          type: 'geojson',
          data: { type: 'FeatureCollection', features: boundaries.map((t) => ({ type: 'Feature', geometry: t.boundary, properties: {} })) },
        })
        map.addLayer({ id: 'tract-line', type: 'line', source: 'tracts', paint: { 'line-color': SEQUENTIAL_BLUE[400], 'line-width': 1.5 } })

        map.addSource('points', {
          type: 'geojson',
          data: { type: 'FeatureCollection', features: points.map((p) => ({ type: 'Feature', geometry: { type: 'Point', coordinates: [p.longitude, p.latitude] }, properties: {} })) },
        })
        map.addLayer({
          id: 'point-circle', type: 'circle', source: 'points',
          paint: { 'circle-color': '#111827', 'circle-radius': 4, 'circle-stroke-width': 1, 'circle-stroke-color': '#ffffff' },
        })

        if (points.length > 0) {
          const bounds = new maplibregl.LngLatBounds()
          points.forEach((p) => bounds.extend([p.longitude, p.latitude]))
          map.fitBounds(bounds, { padding: 30, maxZoom: 13, animate: false })
        } else if (boundaries.length > 0) {
          const bounds = new maplibregl.LngLatBounds()
          boundaries.forEach((t) => {
            const coords = t.boundary.type === 'MultiPolygon' ? t.boundary.coordinates.flat(2) : []
            coords.forEach((c) => bounds.extend(c as [number, number]))
          })
          if (!bounds.isEmpty()) map.fitBounds(bounds, { padding: 30, animate: false })
        }

        map.once('idle', () => resolve(map.getCanvas().toDataURL('image/png')))
      })
    })

    map.remove()
    return imageDataUrl
  } finally {
    container.remove()
  }
}

async function buildReportPdf(
  organizationName: string, datasetName: string, filters: RecordFilters, data: Analytics,
  charts: Record<string, Chart | null>, mapImage: string | null,
): Promise<jsPDF> {
  const doc = new jsPDF({ unit: 'pt', format: 'letter' })
  const pageWidth = doc.internal.pageSize.getWidth()
  const margin = 40
  let y = addPageHeader(doc, { title: 'GeoInsight Analytics Report', organizationName, datasetName, filters })

  doc.setFontSize(13)
  doc.text('Summary', margin, y); y += 18
  doc.setFontSize(10)
  const summaryLines = [
    `Records: ${data.summary.recordCount.toLocaleString()}`,
    `Geocoded: ${data.summary.geocodedRate.toFixed(1)}% (${data.summary.geocodedCount.toLocaleString()})`,
    `Failed: ${data.summary.failedCount.toLocaleString()}`,
    `Total amount: $${Math.round(data.summary.totalAmount).toLocaleString()}`,
    `Tracts touched: ${data.summary.tractCount.toLocaleString()}`,
    `Counties touched: ${data.summary.countyCount.toLocaleString()}`,
  ]
  summaryLines.forEach((line) => { doc.text(line, margin, y); y += 14 })
  y += 10

  const chartWidth = (pageWidth - margin * 2 - 16) / 2
  const chartHeight = chartWidth * 0.6
  let column = 0
  for (const chart of Object.values(charts)) {
    if (!chart) continue
    if (y + chartHeight > doc.internal.pageSize.getHeight() - margin) {
      doc.addPage()
      y = margin
      column = 0
    }
    const x = margin + column * (chartWidth + 16)
    doc.addImage(chart.toBase64Image(), 'PNG', x, y, chartWidth, chartHeight)
    if (column === 1) { y += chartHeight + 16; column = 0 } else { column = 1 }
  }
  if (column === 1) y += chartHeight + 16

  if (mapImage) {
    if (y + 220 > doc.internal.pageSize.getHeight() - margin) { doc.addPage(); y = margin }
    doc.setFontSize(13)
    doc.text('Map', margin, y); y += 16
    const mapWidth = pageWidth - margin * 2
    doc.addImage(mapImage, 'PNG', margin, y, mapWidth, mapWidth * (420 / 760))
    y += mapWidth * (420 / 760) + 20
  }

  if (data.tractStatistics.length > 0) {
    doc.addPage()
    y = margin
    doc.setFontSize(13)
    doc.text(
      `Geographic analysis (${data.summary.demographicsAreEstimated ? 'estimated' : 'real Census ACS'} population)`,
      margin, y,
    ); y += 20
    doc.setFontSize(9)
    const columns = [
      { header: 'Tract', x: margin, width: 160 },
      { header: 'Population', x: margin + 170, width: 70 },
      { header: 'Records', x: margin + 250, width: 60 },
      { header: 'Records/1K', x: margin + 320, width: 70 },
      { header: 'Amount', x: margin + 400, width: 100 },
    ]
    columns.forEach((c) => doc.text(c.header, c.x, y))
    y += 14
    for (const tract of data.tractStatistics) {
      if (y > doc.internal.pageSize.getHeight() - margin) { doc.addPage(); y = margin }
      doc.text(tract.tractName ?? tract.geoid, columns[0].x, y, { maxWidth: columns[0].width })
      doc.text(tract.population?.toLocaleString() ?? '—', columns[1].x, y)
      doc.text(tract.recordCount.toLocaleString(), columns[2].x, y)
      doc.text(tract.recordsPer1000?.toString() ?? '—', columns[3].x, y)
      doc.text(`$${Math.round(tract.totalAmount).toLocaleString()}`, columns[4].x, y)
      y += 13
    }
  }

  return doc
}

export function AnalyticsPage() {
  const { organization, dataset } = useOutletContext<DatasetOutletContext>()
  const [filters, setFilters] = useState<RecordFilters>({})
  const [isGeneratingReport, setIsGeneratingReport] = useState(false)
  const [reportError, setReportError] = useState<string | null>(null)

  const categoryChartRef = useRef<Chart | null>(null)
  const countyChartRef = useRef<Chart | null>(null)
  const incomeChartRef = useRef<Chart | null>(null)
  const statusChartRef = useRef<Chart | null>(null)
  const timeChartRef = useRef<Chart | null>(null)

  const analyticsQuery = useQuery({
    queryKey: ['organizations', organization.id, 'datasets', dataset.id, 'analytics', filters],
    queryFn: () => getAnalytics(organization.id, dataset.id, filters),
  })

  const data = analyticsQuery.data

  const handleGenerateReport = async () => {
    if (!data) return
    setReportError(null)
    setIsGeneratingReport(true)
    try {
      const mapImage = await captureReportMapImage(organization.id, dataset.id, filters)
      const doc = await buildReportPdf(
        organization.name, dataset.name, filters, data,
        {
          category: categoryChartRef.current,
          county: countyChartRef.current,
          income: incomeChartRef.current,
          status: statusChartRef.current,
          time: timeChartRef.current,
        },
        mapImage,
      )

      doc.save(`${dataset.name}-report.pdf`)

      const base64 = base64FromDataUrl(doc.output('datauristring'))
      await createFileExport(organization.id, dataset.id, 'Pdf', base64)
    } catch (err) {
      setReportError(err instanceof ApiError ? err.message : 'Unable to generate the report.')
    } finally {
      setIsGeneratingReport(false)
    }
  }

  return (
    <div className="grid grid-cols-1 items-start gap-4 md:grid-cols-[260px_1fr]">
      <RecordFilterSidebar description="Narrows the dashboard below." onChange={setFilters} />

      <div className="flex min-w-0 flex-col gap-4">
        {analyticsQuery.isPending && <LoadingState />}
        {analyticsQuery.isError && <ErrorState message="Unable to load analytics." />}

        {data && (
          <>
            <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4">
              <SummaryCard label="Records" value={data.summary.recordCount.toLocaleString()} />
              <SummaryCard label="Geocoded" value={`${data.summary.geocodedRate.toFixed(1)}%`} />
              <SummaryCard label="Failed" value={data.summary.failedCount.toLocaleString()} />
              <SummaryCard label="Total amount" value={`$${Math.round(data.summary.totalAmount).toLocaleString()}`} />
              <SummaryCard label="Tracts" value={data.summary.tractCount.toLocaleString()} />
              <SummaryCard label="Counties" value={data.summary.countyCount.toLocaleString()} />
            </div>

            <div>
              <Button size="sm" variant="outline" disabled={isGeneratingReport} onClick={handleGenerateReport}>
                {isGeneratingReport ? 'Generating…' : 'Generate PDF Report'}
              </Button>
            </div>

            {reportError && <ErrorState message={reportError} />}

            <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
              <Card>
                <CardHeader>
                  <CardTitle className="text-base">By category</CardTitle>
                </CardHeader>
                <CardContent>
                  <BarChart
                    labels={data.byCategory.map((c) => c.category ?? '(none)')}
                    data={data.byCategory.map((c) => c.count)}
                    label="Records"
                    exportRef={categoryChartRef}
                  />
                </CardContent>
              </Card>

              <Card>
                <CardHeader>
                  <CardTitle className="text-base">By county</CardTitle>
                </CardHeader>
                <CardContent>
                  <BarChart
                    labels={data.byCounty.map((c) => c.countyFips)}
                    data={data.byCounty.map((c) => c.count)}
                    label="Records"
                    exportRef={countyChartRef}
                  />
                </CardContent>
              </Card>

              <Card>
                <CardHeader>
                  <CardTitle className="text-base">By tract income range</CardTitle>
                  <CardDescription>
                    {data.summary.demographicsAreEstimated ? 'Estimated (sample data)' : 'Real Census ACS data'}
                  </CardDescription>
                </CardHeader>
                <CardContent>
                  <BarChart
                    labels={data.byIncomeRange.map((r) => r.range)}
                    data={data.byIncomeRange.map((r) => r.count)}
                    label="Records"
                    exportRef={incomeChartRef}
                  />
                </CardContent>
              </Card>

              <Card>
                <CardHeader>
                  <CardTitle className="text-base">Geocoding quality</CardTitle>
                </CardHeader>
                <CardContent>
                  <BarChart
                    labels={data.byGeocodeStatus.map((s) => s.status)}
                    data={data.byGeocodeStatus.map((s) => s.count)}
                    label="Records"
                    exportRef={statusChartRef}
                  />
                </CardContent>
              </Card>

              <Card className="lg:col-span-2">
                <CardHeader>
                  <CardTitle className="text-base">Records over time</CardTitle>
                  <CardDescription>By month; records without a date are excluded.</CardDescription>
                </CardHeader>
                <CardContent>
                  <LineChart
                    labels={data.overTime.map((p) => p.period)}
                    data={data.overTime.map((p) => p.count)}
                    label="Records"
                    exportRef={timeChartRef}
                  />
                </CardContent>
              </Card>
            </div>

            <Card>
              <CardHeader>
                <CardTitle className="text-base">Geographic analysis</CardTitle>
                <CardDescription>
                  {data.summary.demographicsAreEstimated
                    ? 'Population figures are estimated (sample data).'
                    : 'Population figures are real Census ACS data.'}
                </CardDescription>
              </CardHeader>
              <CardContent>
                {data.tractStatistics.length === 0 ? (
                  <p className="text-sm text-muted-foreground">
                    No tracts yet — import tract boundaries from the Map tab.
                  </p>
                ) : (
                  <div className="overflow-x-auto rounded-md border">
                    <table className="w-full text-left text-sm">
                      <thead className="bg-muted/50">
                        <tr>
                          <th className="whitespace-nowrap px-3 py-2 font-medium">Tract</th>
                          <th className="whitespace-nowrap px-3 py-2 font-medium">Population</th>
                          <th className="whitespace-nowrap px-3 py-2 font-medium">Records</th>
                          <th className="whitespace-nowrap px-3 py-2 font-medium">Records / 1K</th>
                          <th className="whitespace-nowrap px-3 py-2 font-medium">Amount</th>
                        </tr>
                      </thead>
                      <tbody>
                        {data.tractStatistics.map((t) => (
                          <tr key={t.geoid} className="border-t">
                            <td className="px-3 py-2">
                              {t.tractName ?? t.geoid}
                              <span className="ml-1 text-muted-foreground">({t.geoid})</span>
                            </td>
                            <td className="px-3 py-2">{t.population?.toLocaleString() ?? '—'}</td>
                            <td className="px-3 py-2">{t.recordCount.toLocaleString()}</td>
                            <td className="px-3 py-2">{t.recordsPer1000 ?? '—'}</td>
                            <td className="px-3 py-2">${Math.round(t.totalAmount).toLocaleString()}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                )}
              </CardContent>
            </Card>
          </>
        )}
      </div>
    </div>
  )
}

function SummaryCard({ label, value }: { label: string; value: string }) {
  return (
    <Card>
      <CardContent className="py-4">
        <p className="text-xs text-muted-foreground">{label}</p>
        <p className="text-xl font-semibold">{value}</p>
      </CardContent>
    </Card>
  )
}
