import { useQuery } from '@tanstack/react-query'
import { Link, useOutletContext } from 'react-router-dom'
import { getImports } from '@/api/imports'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { EmptyState, ErrorState, LoadingState } from '@/components/StateViews'
import type { DatasetOutletContext } from './DatasetLayout'

const statusVariant = (status: string): 'destructive' | 'secondary' =>
  status === 'Failed' ? 'destructive' : 'secondary'

export function ImportsListPage() {
  const { organization, dataset, canManage } = useOutletContext<DatasetOutletContext>()

  const importsQuery = useQuery({
    queryKey: ['organizations', organization.id, 'datasets', dataset.id, 'imports'],
    queryFn: () => getImports(organization.id, dataset.id),
  })

  return (
    <Card>
      <CardHeader className="flex flex-row items-start justify-between gap-4">
        <div>
          <CardTitle className="text-xl font-semibold">Imports</CardTitle>
          <CardDescription>CSV files imported into {dataset.name}.</CardDescription>
        </div>
        {canManage && (
          <Button asChild size="sm">
            <Link to={`/organizations/${organization.id}/datasets/${dataset.id}/imports/new`}>Import CSV</Link>
          </Button>
        )}
      </CardHeader>
      <CardContent>
        {importsQuery.isPending && <LoadingState />}

        {importsQuery.isError && <ErrorState message="Unable to load imports for this dataset." />}

        {importsQuery.data?.length === 0 && (
          <EmptyState
            message="No imports yet."
            action={
              canManage && (
                <Button asChild size="sm">
                  <Link to={`/organizations/${organization.id}/datasets/${dataset.id}/imports/new`}>Import CSV</Link>
                </Button>
              )
            }
          />
        )}

        <div className="flex flex-col divide-y">
          {importsQuery.data?.map((item) => (
            <div key={item.id} className="flex items-center justify-between gap-4 py-3">
              <div>
                <p className="text-sm font-medium">{item.originalFileName}</p>
                <p className="text-sm text-muted-foreground">
                  {new Date(item.createdAt).toLocaleString()}
                  {item.completedAt && ` · completed ${new Date(item.completedAt).toLocaleString()}`}
                </p>
              </div>
              <div className="flex items-center gap-3 text-sm">
                <span className="text-muted-foreground">
                  {item.validRows} valid · {item.warningRows} warning · {item.invalidRows} invalid
                </span>
                <Badge variant={statusVariant(item.status)}>{item.status}</Badge>
              </div>
            </div>
          ))}
        </div>
      </CardContent>
    </Card>
  )
}
