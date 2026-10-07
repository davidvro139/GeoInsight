import { useQuery } from '@tanstack/react-query'
import { Link, useOutletContext } from 'react-router-dom'
import { downloadExport, getExports, type ExportRecord, type ExportType } from '@/api/exports'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { EmptyState, ErrorState, LoadingState } from '@/components/StateViews'
import type { DatasetOutletContext } from './DatasetLayout'

const EXTENSIONS: Record<ExportType, string> = {
  Csv: 'csv',
  Xlsx: 'xlsx',
  GeoJson: 'geojson',
  Png: 'png',
  Pdf: 'pdf',
}

const LABELS: Record<ExportType, string> = {
  Csv: 'CSV',
  Xlsx: 'Excel (XLSX)',
  GeoJson: 'GeoJSON',
  Png: 'PNG map image',
  Pdf: 'PDF report',
}

export function ExportsPage() {
  const { organization, dataset } = useOutletContext<DatasetOutletContext>()

  const exportsQuery = useQuery({
    queryKey: ['organizations', organization.id, 'datasets', dataset.id, 'exports'],
    queryFn: () => getExports(organization.id, dataset.id),
  })

  const handleDownload = (item: ExportRecord) => {
    const fileName = `${dataset.name}-${new Date(item.createdAt).toISOString().slice(0, 10)}.${EXTENSIONS[item.exportType]}`
    downloadExport(organization.id, dataset.id, item.id, fileName)
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-xl font-semibold">Exports</CardTitle>
        <CardDescription>CSV/XLSX/GeoJSON exports and PNG/PDF reports generated from {dataset.name}.</CardDescription>
      </CardHeader>
      <CardContent>
        {exportsQuery.isPending && <LoadingState />}

        {exportsQuery.isError && <ErrorState message="Unable to load exports for this dataset." />}

        {exportsQuery.data?.length === 0 && (
          <EmptyState
            message={
              <>
                No exports yet — use the Export menu on the{' '}
                <Link to={`/organizations/${organization.id}/datasets/${dataset.id}/map`} className="underline underline-offset-4">
                  Map tab
                </Link>
                , or generate a PDF report from{' '}
                <Link to={`/organizations/${organization.id}/datasets/${dataset.id}/analytics`} className="underline underline-offset-4">
                  Analytics
                </Link>
                .
              </>
            }
          />
        )}

        <div className="flex flex-col divide-y">
          {exportsQuery.data?.map((item) => (
            <div key={item.id} className="flex items-center justify-between gap-4 py-3">
              <div>
                <p className="text-sm font-medium">{LABELS[item.exportType]}</p>
                <p className="text-sm text-muted-foreground">{new Date(item.createdAt).toLocaleString()}</p>
              </div>
              <Button variant="outline" size="sm" onClick={() => handleDownload(item)}>
                Download
              </Button>
            </div>
          ))}
        </div>
      </CardContent>
    </Card>
  )
}
