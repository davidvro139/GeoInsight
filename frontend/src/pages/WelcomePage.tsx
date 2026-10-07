import { useMutation } from '@tanstack/react-query'
import { Link, useLocation, useNavigate } from 'react-router-dom'
import { ApiError } from '@/api/client'
import { demoLogin, type AuthResponse } from '@/api/auth'
import { useAuth } from '@/auth/auth-context'
import { Button } from '@/components/ui/button'
import { Card, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { ErrorState } from '@/components/StateViews'
import { Logo } from '@/components/Logo'

const FEATURES: { title: string; description: string }[] = [
  {
    title: 'Import',
    description: 'Upload a CSV of your records — loans, branches, complaints, whatever you track — and map its columns to GeoInsight’s fields.',
  },
  {
    title: 'Geocoding',
    description: 'Turn street addresses into map coordinates automatically using the U.S. Census Geocoder, or supply your own latitude/longitude.',
  },
  {
    title: 'Review',
    description: 'Resolve addresses the geocoder couldn’t match automatically — pick the right candidate, correct a typo, or drop a pin by hand.',
  },
  {
    title: 'Map',
    description: 'See every record plotted on an interactive map, with Census tract boundaries and demographic choropleths layered underneath.',
  },
  {
    title: 'Analytics',
    description: 'Charts and breakdowns by category, county, income range, and time — recalculated live as you filter.',
  },
  {
    title: 'Gap Analysis',
    description: 'Spot Census tracts with unusually low or high activity compared to their county average — a starting point for further review, not a legal conclusion.',
  },
  {
    title: 'Saved Views',
    description: 'Save a filter and map combination and reopen it later, or hand it to a teammate.',
  },
  {
    title: 'Exports',
    description: 'Download your filtered data as CSV, Excel, GeoJSON, or a formatted PDF report.',
  },
  {
    title: 'Audit Log',
    description: 'Every import, geocode job, export, and role change is recorded — visible to organization Administrators.',
  },
]

export function WelcomePage() {
  const { isAuthenticated, signIn } = useAuth()
  const navigate = useNavigate()
  // RequireAuth redirects here with the originally-requested route in
  // location.state.from — forwarded to /login so signing in from this page
  // still lands the visitor back where they meant to go, not just at "/".
  const location = useLocation()
  const fromState = location.state as { from?: Location } | null

  const demoMutation = useMutation({
    mutationFn: demoLogin,
    onSuccess: (response: AuthResponse) => {
      signIn(response)
      navigate(fromState?.from?.pathname ?? '/', { replace: true })
    },
  })

  return (
    <div className="mx-auto flex w-full max-w-5xl flex-col gap-10 p-4 py-8 sm:p-6 sm:py-12">
      <header className="flex items-center justify-between gap-4">
        <Logo />
        {isAuthenticated ? (
          <Button asChild variant="outline" size="sm">
            <Link to="/">Go to your datasets</Link>
          </Button>
        ) : (
          <div className="flex items-center gap-2">
            <Button asChild variant="ghost" size="sm">
              <Link to="/login" state={fromState}>Sign in</Link>
            </Button>
            <Button asChild size="sm">
              <Link to="/register">Create an account</Link>
            </Button>
          </div>
        )}
      </header>

      <section className="flex flex-col items-start gap-4">
        <h1 className="text-3xl font-semibold sm:text-4xl">
          Geographic data analysis for organizations that need to see where things happen.
        </h1>
        <p className="max-w-2xl text-muted-foreground">
          Import a spreadsheet of addresses, turn them into map coordinates, and see them layered
          against real U.S. Census tract boundaries and demographics — with filtering, charts, and
          exports built in.
        </p>

        {!isAuthenticated && (
          <div className="flex flex-col gap-3 sm:flex-row sm:items-center">
            <Button disabled={demoMutation.isPending} onClick={() => demoMutation.mutate()}>
              {demoMutation.isPending ? 'Starting demo…' : 'Try the demo'}
            </Button>
            <p className="text-sm text-muted-foreground">
              No sign-up required — explores a real, pre-loaded dataset.
            </p>
          </div>
        )}

        {demoMutation.isError && (
          <ErrorState
            message={
              demoMutation.error instanceof ApiError ? demoMutation.error.message : 'Unable to start the demo.'
            }
          />
        )}
      </section>

      <section className="flex flex-col gap-4">
        <h2 className="text-xl font-semibold">How it works</h2>
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {FEATURES.map((feature) => (
            <Card key={feature.title}>
              <CardHeader>
                <CardTitle className="text-base">{feature.title}</CardTitle>
                <CardDescription>{feature.description}</CardDescription>
              </CardHeader>
            </Card>
          ))}
        </div>
      </section>
    </div>
  )
}
