import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useRef, useState } from 'react'
import { Link, useOutletContext, useParams } from 'react-router-dom'
import { ApiError } from '@/api/client'
import {
  commitImport,
  uploadImport,
  validateImport,
  type ColumnMapping,
  type ImportSummary,
  type ImportUploadResult,
} from '@/api/imports'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { ErrorState } from '@/components/StateViews'
import type { OrgOutletContext } from './OrganizationLayout'

const STANDARD_FIELDS: { key: string; label: string; required?: boolean }[] = [
  { key: 'address_line_1', label: 'Address Line 1', required: true },
  { key: 'address_line_2', label: 'Address Line 2' },
  { key: 'city', label: 'City', required: true },
  { key: 'state', label: 'State', required: true },
  { key: 'postal_code', label: 'Postal Code' },
  { key: 'external_id', label: 'External ID' },
  { key: 'category', label: 'Category' },
  { key: 'amount', label: 'Amount' },
  { key: 'date', label: 'Date' },
  { key: 'status', label: 'Status' },
  { key: 'description', label: 'Description' },
  // Geocoding isn't built yet (Phase 5) — a CSV can supply known coordinates directly.
  { key: 'latitude', label: 'Latitude' },
  { key: 'longitude', label: 'Longitude' },
]

const REQUIRED_FIELDS = STANDARD_FIELDS.filter((f) => f.required).map((f) => f.key)

type WizardStep = 'upload' | 'map' | 'validated' | 'committed'

export function ImportWizardPage() {
  const { organization } = useOutletContext<OrgOutletContext>()
  const { datasetId } = useParams<{ datasetId: string }>()
  const id = Number(datasetId)
  const queryClient = useQueryClient()
  const fileInputRef = useRef<HTMLInputElement>(null)

  const [step, setStep] = useState<WizardStep>('upload')
  const [isDragging, setIsDragging] = useState(false)
  const [upload, setUpload] = useState<ImportUploadResult | null>(null)
  const [delimiter, setDelimiter] = useState(',')
  const [mapping, setMapping] = useState<Record<string, string>>({})
  const [summary, setSummary] = useState<ImportSummary | null>(null)

  const uploadMutation = useMutation({
    mutationFn: (file: File) => uploadImport(organization.id, id, file),
    onSuccess: (result) => {
      setUpload(result)
      setDelimiter(result.detectedDelimiter)
      setMapping({})
      setStep('map')
    },
  })

  const buildMappings = (): ColumnMapping[] =>
    (upload?.headers ?? []).map((header) => ({
      csvColumn: header,
      systemField: mapping[header] || null,
    }))

  const validateMutation = useMutation({
    mutationFn: () => validateImport(organization.id, id, upload!.importId, delimiter, buildMappings()),
    onSuccess: (result) => {
      setSummary(result)
      setStep('validated')
    },
  })

  const commitMutation = useMutation({
    mutationFn: () => commitImport(organization.id, id, upload!.importId, delimiter, buildMappings()),
    onSuccess: (result) => {
      setSummary(result)
      setStep('committed')
      queryClient.invalidateQueries({ queryKey: ['organizations', organization.id, 'datasets', id, 'imports'] })
    },
  })

  const handleFile = (file: File | undefined) => {
    if (file) uploadMutation.mutate(file)
  }

  const missingRequiredFields = REQUIRED_FIELDS.filter(
    (field) => !Object.values(mapping).includes(field),
  )

  return (
    <div className="mx-auto flex w-full max-w-5xl flex-col gap-4">
      <Link
        to={`/organizations/${organization.id}/datasets/${id}`}
        className="text-sm text-muted-foreground underline underline-offset-4"
      >
        ← Back to dataset
      </Link>

      {step === 'upload' && (
        <Card>
          <CardHeader>
            <CardTitle className="text-xl font-semibold">Import CSV — Upload</CardTitle>
            <CardDescription>Drag and drop a CSV file, or browse to select one.</CardDescription>
          </CardHeader>
          <CardContent className="flex flex-col gap-4">
            {uploadMutation.isError && (
              <ErrorState
                message={
                  uploadMutation.error instanceof ApiError ? uploadMutation.error.message : 'Unable to upload that file.'
                }
              />
            )}

            <div
              onDragOver={(e) => {
                e.preventDefault()
                setIsDragging(true)
              }}
              onDragLeave={() => setIsDragging(false)}
              onDrop={(e) => {
                e.preventDefault()
                setIsDragging(false)
                handleFile(e.dataTransfer.files[0])
              }}
              className={`flex flex-col items-center justify-center gap-3 rounded-lg border-2 border-dashed p-12 text-center ${
                isDragging ? 'border-primary bg-muted/50' : 'border-border'
              }`}
            >
              <p className="text-sm text-muted-foreground">
                {uploadMutation.isPending ? 'Uploading…' : 'Drop a .csv file here'}
              </p>
              <input
                ref={fileInputRef}
                type="file"
                accept=".csv,text/csv"
                className="hidden"
                onChange={(e) => handleFile(e.target.files?.[0])}
              />
              <Button
                type="button"
                variant="outline"
                size="sm"
                disabled={uploadMutation.isPending}
                onClick={() => fileInputRef.current?.click()}
              >
                Browse files
              </Button>
            </div>
          </CardContent>
        </Card>
      )}

      {step === 'map' && upload && (
        <Card>
          <CardHeader>
            <CardTitle className="text-xl font-semibold">Import CSV — Preview &amp; Map Columns</CardTitle>
            <CardDescription>
              {upload.totalRows} rows detected. Map each column to a field, or leave it as a custom field.
            </CardDescription>
          </CardHeader>
          <CardContent className="flex flex-col gap-4">
            <div className="flex items-center gap-2">
              <label htmlFor="delimiter" className="text-sm font-medium">
                Delimiter
              </label>
              <select
                id="delimiter"
                className="h-8 rounded-md border bg-transparent px-2 text-sm"
                value={delimiter}
                onChange={(e) => setDelimiter(e.target.value)}
              >
                <option value=",">Comma (,)</option>
                <option value=";">Semicolon (;)</option>
                <option value="\t">Tab</option>
              </select>
            </div>

            <div className="overflow-x-auto rounded-md border">
              <table className="w-full text-left text-sm">
                <thead className="bg-muted/50">
                  <tr>
                    {upload.headers.map((header) => (
                      <th key={header} className="whitespace-nowrap px-3 py-2 font-medium">
                        {header}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {upload.sampleRows.map((row, i) => (
                    <tr key={i} className="border-t">
                      {row.map((cell, j) => (
                        <td key={j} className="whitespace-nowrap px-3 py-2 text-muted-foreground">
                          {cell}
                        </td>
                      ))}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <div className="flex flex-col gap-3">
              {upload.headers.map((header) => (
                <div key={header} className="flex items-center gap-3">
                  <span className="w-40 shrink-0 truncate text-sm font-medium">{header}</span>
                  <select
                    className="h-8 flex-1 rounded-md border bg-transparent px-2 text-sm"
                    value={mapping[header] ?? ''}
                    onChange={(e) => setMapping((m) => ({ ...m, [header]: e.target.value }))}
                  >
                    <option value="">Custom field</option>
                    {STANDARD_FIELDS.map((field) => (
                      <option key={field.key} value={field.key}>
                        {field.label}
                        {field.required ? ' (required)' : ''}
                      </option>
                    ))}
                  </select>
                </div>
              ))}
            </div>

            {missingRequiredFields.length > 0 && (
              <Alert>
                <AlertDescription>
                  Map a column to: {missingRequiredFields.join(', ')} before validating.
                </AlertDescription>
              </Alert>
            )}

            {validateMutation.isError && (
              <ErrorState
                message={
                  validateMutation.error instanceof ApiError
                    ? validateMutation.error.message
                    : 'Unable to validate this import.'
                }
              />
            )}

            <Button
              className="self-start"
              disabled={missingRequiredFields.length > 0 || validateMutation.isPending}
              onClick={() => validateMutation.mutate()}
            >
              {validateMutation.isPending ? 'Validating…' : 'Validate'}
            </Button>
          </CardContent>
        </Card>
      )}

      {step === 'validated' && summary && (
        <Card>
          <CardHeader>
            <CardTitle className="text-xl font-semibold">Import CSV — Validation</CardTitle>
            <CardDescription>Review the results, then commit to add records to the dataset.</CardDescription>
          </CardHeader>
          <CardContent className="flex flex-col gap-4">
            <SummaryCounts summary={summary} />
            <IssuesList summary={summary} />

            {commitMutation.isError && (
              <ErrorState
                message={
                  commitMutation.error instanceof ApiError
                    ? commitMutation.error.message
                    : 'Unable to commit this import.'
                }
              />
            )}

            <div className="flex gap-2">
              <Button variant="outline" onClick={() => setStep('map')}>
                Back to mapping
              </Button>
              <Button disabled={commitMutation.isPending} onClick={() => commitMutation.mutate()}>
                {commitMutation.isPending ? 'Committing…' : `Commit import (${summary.validRows + summary.warningRows} rows)`}
              </Button>
            </div>
          </CardContent>
        </Card>
      )}

      {step === 'committed' && summary && (
        <Card>
          <CardHeader>
            <CardTitle className="text-xl font-semibold">Import CSV — Summary</CardTitle>
            <CardDescription>Import committed.</CardDescription>
          </CardHeader>
          <CardContent className="flex flex-col gap-4">
            <SummaryCounts summary={summary} />
            <IssuesList summary={summary} />
            <Button asChild className="self-start">
              <Link to={`/organizations/${organization.id}/datasets/${id}/imports`}>Back to imports</Link>
            </Button>
          </CardContent>
        </Card>
      )}
    </div>
  )
}

function SummaryCounts({ summary }: { summary: ImportSummary }) {
  return (
    <div className="flex gap-4 text-sm">
      <span>{summary.totalRows} total</span>
      <Badge variant="secondary">{summary.validRows} valid</Badge>
      <Badge variant="secondary">{summary.warningRows} warning</Badge>
      <Badge variant="secondary">{summary.invalidRows} invalid</Badge>
    </div>
  )
}

function IssuesList({ summary }: { summary: ImportSummary }) {
  if (summary.issues.length === 0) {
    return <p className="text-sm text-muted-foreground">No issues found.</p>
  }

  return (
    <div className="max-h-64 overflow-y-auto rounded-md border">
      <table className="w-full text-left text-sm">
        <thead className="bg-muted/50">
          <tr>
            <th className="px-3 py-2 font-medium">Row</th>
            <th className="px-3 py-2 font-medium">Severity</th>
            <th className="px-3 py-2 font-medium">Issue</th>
          </tr>
        </thead>
        <tbody>
          {summary.issues.map((issue, i) => (
            <tr key={i} className="border-t">
              <td className="px-3 py-2">{issue.rowNumber}</td>
              <td className="px-3 py-2">{issue.severity}</td>
              <td className="px-3 py-2 text-muted-foreground">{issue.message}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
