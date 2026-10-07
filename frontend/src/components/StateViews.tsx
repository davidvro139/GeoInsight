import type { ReactNode } from 'react'
import { Alert, AlertDescription } from '@/components/ui/alert'

/** Inline "loading" text for content within an already-rendered page/card. */
export function LoadingState({ message = 'Loading…' }: { message?: string }) {
  return <p className="text-sm text-muted-foreground">{message}</p>
}

/** Full-viewport loading state for a top-level route that has nothing else to show yet. */
export function FullPageLoading({ message = 'Loading…' }: { message?: string }) {
  return (
    <div className="flex min-h-svh items-center justify-center text-sm text-muted-foreground">
      {message}
    </div>
  )
}

/** Inline error banner for a section/page that otherwise rendered fine. */
export function ErrorState({ message }: { message: string }) {
  return (
    <Alert variant="destructive">
      <AlertDescription>{message}</AlertDescription>
    </Alert>
  )
}

/** "Nothing here yet" placeholder with an optional call-to-action. */
export function EmptyState({ message, action }: { message: ReactNode; action?: ReactNode }) {
  return (
    <div className="flex flex-col items-start gap-3 py-2">
      <p className="text-sm text-muted-foreground">{message}</p>
      {action}
    </div>
  )
}
