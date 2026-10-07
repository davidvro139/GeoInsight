import { useQuery } from '@tanstack/react-query'
import { Navigate, Outlet, useNavigate, useParams } from 'react-router-dom'
import { getMyOrganizations, getOrganization, type OrganizationDetail } from '@/api/organizations'
import { ApiError } from '@/api/client'
import { useAuth } from '@/auth/auth-context'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { FullPageLoading } from '@/components/StateViews'
import { Logo } from '@/components/Logo'
import { setLastOrganizationId } from '@/organizations/lastOrganization'

export interface OrgOutletContext {
  organization: OrganizationDetail
}

const NEW_ORGANIZATION_VALUE = '__new__'

export function OrganizationLayout() {
  const { organizationId } = useParams<{ organizationId: string }>()
  const id = Number(organizationId)
  const { user, signOut } = useAuth()
  const navigate = useNavigate()

  const organizationsQuery = useQuery({
    queryKey: ['organizations'],
    queryFn: getMyOrganizations,
  })

  const organizationQuery = useQuery({
    queryKey: ['organizations', id],
    queryFn: () => getOrganization(id),
    enabled: Number.isFinite(id),
  })

  if (!Number.isFinite(id)) {
    return <Navigate to="/" replace />
  }

  if (organizationQuery.isPending) {
    return <FullPageLoading />
  }

  if (organizationQuery.isError) {
    const isForbidden = organizationQuery.error instanceof ApiError && organizationQuery.error.status === 403
    const isNotFound = organizationQuery.error instanceof ApiError && organizationQuery.error.status === 404

    return (
      <div className="flex min-h-svh items-center justify-center bg-muted/40 p-4">
        <Card className="w-full max-w-sm">
          <CardHeader>
            <CardTitle className="text-xl font-semibold">{isNotFound ? 'Organization not found' : 'Access denied'}</CardTitle>
            <CardDescription>
              {isForbidden
                ? "You're not a member of this organization."
                : isNotFound
                  ? "This organization doesn't exist."
                  : 'Something went wrong loading this organization.'}
            </CardDescription>
          </CardHeader>
          <CardContent>
            <Button onClick={() => navigate('/')}>Back to your organizations</Button>
          </CardContent>
        </Card>
      </div>
    )
  }

  const organization = organizationQuery.data
  const organizations = organizationsQuery.data ?? []

  const handleSwitch = (value: string) => {
    if (value === NEW_ORGANIZATION_VALUE) {
      navigate('/organizations/new')
      return
    }
    setLastOrganizationId(Number(value))
    navigate(`/organizations/${value}`)
  }

  return (
    <div className="min-h-svh bg-muted/40">
      <header className="flex flex-wrap items-center justify-between gap-x-4 gap-y-2 border-b bg-card px-4 py-4 sm:px-6">
        <div className="flex flex-wrap items-center gap-2 sm:gap-4">
          <Logo />
          <Button variant="ghost" size="sm" onClick={() => navigate(`/organizations/${organization.id}`)}>
            Datasets
          </Button>
          <Button variant="ghost" size="sm" onClick={() => navigate('/welcome')}>
            How it works
          </Button>
          <Select value={String(organization.id)} onValueChange={handleSwitch}>
            <SelectTrigger className="w-40 sm:w-56" size="sm">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {organizations.map((org) => (
                <SelectItem key={org.id} value={String(org.id)}>
                  {org.name}
                </SelectItem>
              ))}
              <SelectItem value={NEW_ORGANIZATION_VALUE}>+ New organization</SelectItem>
            </SelectContent>
          </Select>
        </div>

        <div className="flex flex-wrap items-center gap-2 sm:gap-4">
          {organization.myRole === 'Administrator' && (
            <Button variant="ghost" size="sm" onClick={() => navigate(`/organizations/${organization.id}/settings`)}>
              Settings
            </Button>
          )}
          <span className="hidden text-sm text-muted-foreground sm:inline">{user?.displayName}</span>
          <Button variant="outline" size="sm" onClick={signOut}>
            Log out
          </Button>
        </div>
      </header>

      <main className="p-4 sm:p-6">
        <Outlet context={{ organization } satisfies OrgOutletContext} />
      </main>
    </div>
  )
}
