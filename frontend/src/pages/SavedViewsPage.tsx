import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { Link, useOutletContext } from 'react-router-dom'
import { ApiError } from '@/api/client'
import { createSavedView, deleteSavedView, getSavedViews, renameSavedView, type SavedView } from '@/api/savedViews'
import { useAuth } from '@/auth/auth-context'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { EmptyState, ErrorState, LoadingState } from '@/components/StateViews'
import type { DatasetOutletContext } from './DatasetLayout'

export function SavedViewsPage() {
  const { organization, dataset } = useOutletContext<DatasetOutletContext>()
  const { user } = useAuth()
  const queryClient = useQueryClient()
  const [renamingId, setRenamingId] = useState<number | null>(null)
  const [renameValue, setRenameValue] = useState('')

  const savedViewsKey = ['organizations', organization.id, 'datasets', dataset.id, 'saved-views']
  const savedViewsQuery = useQuery({
    queryKey: savedViewsKey,
    queryFn: () => getSavedViews(organization.id, dataset.id),
  })

  const invalidate = () => queryClient.invalidateQueries({ queryKey: savedViewsKey })

  const renameMutation = useMutation({
    mutationFn: ({ id, name }: { id: number; name: string }) => renameSavedView(organization.id, dataset.id, id, name),
    onSuccess: () => {
      invalidate()
      setRenamingId(null)
    },
  })

  const duplicateMutation = useMutation({
    mutationFn: (view: SavedView) =>
      createSavedView(organization.id, dataset.id, {
        name: `Copy of ${view.name}`,
        filters: view.filters,
        mapConfiguration: view.mapConfiguration,
      }),
    onSuccess: invalidate,
  })

  const deleteMutation = useMutation({
    mutationFn: (id: number) => deleteSavedView(organization.id, dataset.id, id),
    onSuccess: invalidate,
  })

  const canManageView = (view: SavedView) => view.userId === user?.userId || organization.myRole === 'Administrator'

  const anyError = renameMutation.error ?? duplicateMutation.error ?? deleteMutation.error

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-xl font-semibold">Saved Views</CardTitle>
        <CardDescription>Bookmarked filter and map states for {dataset.name}.</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        {anyError && (
          <ErrorState message={anyError instanceof ApiError ? anyError.message : 'Something went wrong.'} />
        )}

        {savedViewsQuery.isPending && <LoadingState />}

        {savedViewsQuery.isError && <ErrorState message="Unable to load saved views." />}

        {savedViewsQuery.data?.length === 0 && (
          <EmptyState
            message={
              <>
                No saved views yet — save one from the{' '}
                <Link to={`/organizations/${organization.id}/datasets/${dataset.id}/map`} className="underline underline-offset-4">
                  Map tab
                </Link>
                .
              </>
            }
          />
        )}

        <div className="flex flex-col divide-y">
          {savedViewsQuery.data?.map((view) => (
            <div key={view.id} className="flex items-center justify-between gap-4 py-3">
              <div className="flex-1">
                {renamingId === view.id ? (
                  <div className="flex items-center gap-2">
                    <Input
                      autoFocus
                      value={renameValue}
                      onChange={(e) => setRenameValue(e.target.value)}
                      className="h-8 w-56"
                      onKeyDown={(e) => {
                        if (e.key === 'Enter' && renameValue.trim()) renameMutation.mutate({ id: view.id, name: renameValue.trim() })
                        if (e.key === 'Escape') setRenamingId(null)
                      }}
                    />
                    <Button
                      size="sm"
                      disabled={!renameValue.trim() || renameMutation.isPending}
                      onClick={() => renameMutation.mutate({ id: view.id, name: renameValue.trim() })}
                    >
                      Save
                    </Button>
                    <Button variant="outline" size="sm" onClick={() => setRenamingId(null)}>
                      Cancel
                    </Button>
                  </div>
                ) : (
                  <>
                    <p className="text-sm font-medium">{view.name}</p>
                    <p className="text-sm text-muted-foreground">
                      by {view.ownerDisplayName} · modified {new Date(view.updatedAt).toLocaleString()}
                    </p>
                  </>
                )}
              </div>

              {renamingId !== view.id && (
                <div className="flex items-center gap-2">
                  <Button asChild variant="outline" size="sm">
                    <Link to={`/organizations/${organization.id}/datasets/${dataset.id}/map?view=${view.id}`}>Open</Link>
                  </Button>
                  <Button
                    variant="outline"
                    size="sm"
                    disabled={duplicateMutation.isPending}
                    onClick={() => duplicateMutation.mutate(view)}
                  >
                    Duplicate
                  </Button>
                  {canManageView(view) && (
                    <>
                      <Button
                        variant="outline"
                        size="sm"
                        onClick={() => {
                          setRenamingId(view.id)
                          setRenameValue(view.name)
                        }}
                      >
                        Rename
                      </Button>
                      <Button
                        variant="outline"
                        size="sm"
                        disabled={deleteMutation.isPending}
                        onClick={() => {
                          if (window.confirm(`Delete the saved view "${view.name}"?`)) deleteMutation.mutate(view.id)
                        }}
                      >
                        Delete
                      </Button>
                    </>
                  )}
                </div>
              )}
            </div>
          ))}
        </div>
      </CardContent>
    </Card>
  )
}
