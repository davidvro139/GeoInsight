import { MapPinned } from 'lucide-react'

/** The GeoInsight wordmark + icon badge — the one place both are defined. */
export function Logo({ className = '' }: { className?: string }) {
  return (
    <span className={`flex items-center gap-2 ${className}`}>
      <span className="flex size-7 shrink-0 items-center justify-center rounded-md bg-primary text-primary-foreground">
        <MapPinned className="size-4" />
      </span>
      <span className="font-heading text-lg font-semibold">GeoInsight</span>
    </span>
  )
}
