import { useQuery } from '@tanstack/react-query'
import { Navigate } from 'react-router-dom'
import { getMyOrganizations } from '@/api/organizations'
import { ErrorState, FullPageLoading } from '@/components/StateViews'
import { getLastOrganizationId } from '@/organizations/lastOrganization'

export function OrganizationRedirectPage() {
  const { data, isPending, isError } = useQuery({
    queryKey: ['organizations'],
    queryFn: getMyOrganizations,
  })

  if (isPending) {
    return <FullPageLoading />
  }

  if (isError) {
    return (
      <div className="flex min-h-svh items-center justify-center p-4">
        <div className="w-full max-w-sm">
          <ErrorState message="Unable to load your organizations. Please refresh the page." />
        </div>
      </div>
    )
  }

  if (!data || data.length === 0) {
    return <Navigate to="/organizations/new" replace />
  }

  const lastId = getLastOrganizationId()
  const target = data.find((org) => org.id === lastId) ?? data[0]

  return <Navigate to={`/organizations/${target.id}`} replace />
}
