import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, NavLink, Outlet, useOutletContext, useParams } from 'react-router-dom'
import { ApiError } from '@/api/client'
import { archiveDataset, getDataset, unarchiveDataset, type DatasetDetail } from '@/api/datasets'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { LoadingState } from '@/components/StateViews'
import { canManageDatasets } from '@/organizations/permissions'
import type { OrgOutletContext } from './OrganizationLayout'

export interface DatasetOutletContext {
  organization: OrgOutletContext['organization']
  dataset: DatasetDetail
  canManage: boolean
  invalidateDataset: () => void
}

const tabLinkClass = ({ isActive }: { isActive: boolean }) =>
  `whitespace-nowrap border-b-2 px-1 pb-2 text-sm ${isActive ? 'border-primary font-medium text-primary' : 'border-transparent text-muted-foreground hover:text-foreground'}`

export function DatasetLayout() {
  const { organization } = useOutletContext<OrgOutletContext>()
  const { datasetId } = useParams<{ datasetId: string }>()
  const id = Number(datasetId)
  const canManage = canManageDatasets(organization.myRole)
  const queryClient = useQueryClient()

  const datasetKey = ['organizations', organization.id, 'datasets', id]
  const datasetQuery = useQuery({
    queryKey: datasetKey,
    queryFn: () => getDataset(organization.id, id),
    enabled: Number.isFinite(id),
  })

  const invalidateDataset = () => queryClient.invalidateQueries({ queryKey: datasetKey })

  const archiveMutation = useMutation({
    mutationFn: () =>
      datasetQuery.data?.isArchived
        ? unarchiveDataset(organization.id, id)
        : archiveDataset(organization.id, id),
    onSuccess: invalidateDataset,
  })

  if (datasetQuery.isPending) {
    return <LoadingState />
  }

  if (datasetQuery.isError || !datasetQuery.data) {
    const isNotFound = datasetQuery.error instanceof ApiError && datasetQuery.error.status === 404

    return (
      <Card className="mx-auto max-w-xl">
        <CardHeader>
          <CardTitle className="text-xl font-semibold">{isNotFound ? 'Dataset not found' : 'Unable to load dataset'}</CardTitle>
          <CardDescription>
            <Link to={`/organizations/${organization.id}`} className="underline underline-offset-4">
              Back to datasets
            </Link>
          </CardDescription>
        </CardHeader>
      </Card>
    )
  }

  const dataset = datasetQuery.data

  return (
    <div className="mx-auto flex w-full max-w-5xl flex-col gap-6">
      <Link
        to={`/organizations/${organization.id}`}
        className="text-sm text-muted-foreground underline underline-offset-4"
      >
        ← Back to datasets
      </Link>

      <Card>
        <CardHeader className="flex flex-row items-start justify-between gap-4">
          <div>
            <CardTitle className="flex items-center gap-2 text-xl font-semibold">
              {dataset.name}
              {dataset.isArchived && <Badge variant="secondary">Archived</Badge>}
            </CardTitle>
            <CardDescription>
              {dataset.datasetType} · created by {dataset.createdByDisplayName} on{' '}
              {new Date(dataset.createdAt).toLocaleDateString()} · updated{' '}
              {new Date(dataset.updatedAt).toLocaleDateString()}
            </CardDescription>
          </div>
          {canManage && (
            <Button
              variant="outline"
              size="sm"
              disabled={archiveMutation.isPending}
              onClick={() => archiveMutation.mutate()}
            >
              {dataset.isArchived ? 'Unarchive' : 'Archive'}
            </Button>
          )}
        </CardHeader>
        <CardContent>
          <nav className="flex gap-4 overflow-x-auto border-b">
            <NavLink to={`/organizations/${organization.id}/datasets/${dataset.id}`} end className={tabLinkClass}>
              Overview
            </NavLink>
            <NavLink to={`/organizations/${organization.id}/datasets/${dataset.id}/map`} className={tabLinkClass}>
              Map
            </NavLink>
            <NavLink to={`/organizations/${organization.id}/datasets/${dataset.id}/imports`} className={tabLinkClass}>
              Imports
            </NavLink>
            <NavLink to={`/organizations/${organization.id}/datasets/${dataset.id}/geocoding`} className={tabLinkClass}>
              Geocoding
            </NavLink>
            <NavLink to={`/organizations/${organization.id}/datasets/${dataset.id}/review`} className={tabLinkClass}>
              Review
            </NavLink>
            <NavLink to={`/organizations/${organization.id}/datasets/${dataset.id}/analytics`} className={tabLinkClass}>
              Analytics
            </NavLink>
            <NavLink to={`/organizations/${organization.id}/datasets/${dataset.id}/gap-analysis`} className={tabLinkClass}>
              Gap Analysis
            </NavLink>
            <NavLink to={`/organizations/${organization.id}/datasets/${dataset.id}/saved-views`} className={tabLinkClass}>
              Saved Views
            </NavLink>
            <NavLink to={`/organizations/${organization.id}/datasets/${dataset.id}/exports`} className={tabLinkClass}>
              Exports
            </NavLink>
          </nav>
        </CardContent>
      </Card>

      <Outlet
        context={{ organization, dataset, canManage, invalidateDataset } satisfies DatasetOutletContext}
      />
    </div>
  )
}
