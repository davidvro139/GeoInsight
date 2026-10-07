import { expect, test } from '@playwright/test'

/**
 * The full core flow end to end, against a real backend (Census Geocoder
 * included — this test hits the live, free, no-API-key geocoding service,
 * so it depends on that service being reachable and can be slower/flakier
 * than a pure-unit test; see CI workflow notes).
 */
test('register, create org, create dataset, import, geocode, filter, and see it reflected in analytics', async ({ page }) => {
  test.setTimeout(120_000)

  const email = `e2e-flow-${Date.now()}@example.com`
  const orgName = `E2E Org ${Date.now()}`
  const datasetName = 'E2E Test Dataset'

  await page.goto('/register')
  await page.getByLabel('Name').fill('E2E Flow Test')
  await page.getByLabel('Email').fill(email)
  await page.getByLabel('Password').fill('Password123')
  await page.getByRole('button', { name: /create account/i }).click()
  await expect(page).toHaveURL(/organizations\/new/)

  await page.getByLabel('Organization name').fill(orgName)
  await page.getByRole('button', { name: /create organization/i }).click()
  await expect(page).toHaveURL(/\/organizations\/\d+$/)

  await page.getByRole('button', { name: /new dataset/i }).click()
  await page.getByLabel('Name').fill(datasetName)
  await page.getByRole('button', { name: /^create dataset$/i }).click()
  await expect(page.getByText(datasetName)).toBeVisible()

  await page.getByRole('link', { name: datasetName }).click()
  await page.getByRole('link', { name: 'Imports' }).click()
  await page.getByRole('link', { name: /import csv/i }).click()
  await expect(page).toHaveURL(/imports\/new/)

  // A single, well-known, reliably-geocodable real address (no lat/lon
  // columns — this exercises the real Census Geocoder, not the CSV-
  // coordinates workaround).
  const csv = 'address,city,state\n1600 Pennsylvania Avenue NW,Washington,DC\n'
  await page.locator('input[type="file"]').setInputFiles({
    name: 'e2e-import.csv',
    mimeType: 'text/csv',
    buffer: Buffer.from(csv),
  })

  await expect(page.getByText('1 rows detected')).toBeVisible()
  // Each mapping row is a header <span> (exact text = the CSV column name)
  // next to its <select> — matching the span exactly avoids false hits from
  // every select's own option list containing words like "address".
  const mapColumn = async (header: string, systemField: string) => {
    const row = page.locator('span', { hasText: new RegExp(`^${header}$`) }).locator('..')
    await row.getByRole('combobox').selectOption(systemField)
  }
  await mapColumn('address', 'address_line_1')
  await mapColumn('city', 'city')
  await mapColumn('state', 'state')

  await page.getByRole('button', { name: /^validate$/i }).click()
  await expect(page.getByText(/1 total/)).toBeVisible()

  await page.getByRole('button', { name: /commit import/i }).click()
  await expect(page.getByText('Import committed.')).toBeVisible()

  // The wizard is a standalone route (no dataset tab nav) — back to imports
  // first, which is nested under the dataset layout that has the tabs.
  await page.getByRole('link', { name: /back to imports/i }).click()
  await page.getByRole('link', { name: 'Geocoding' }).click()
  await page.getByRole('button', { name: /start geocoding/i }).click()

  // Real network call to the Census Geocoder, via a Hangfire job the Worker
  // picks up on its own polling schedule — generous timeout on purpose.
  await expect(page.getByText('No geocoding job currently running.')).toBeVisible({ timeout: 60_000 })

  await page.getByRole('link', { name: 'Map' }).click()
  await expect(page.locator('.maplibregl-canvas')).toBeVisible()
  await expect(page.getByText(/of 1 filtered records have coordinates/)).toBeVisible()

  // A filter that excludes the one record — confirms filtering reaches the
  // map/table together, and specifically exercises the "no rows match the
  // current filters" empty state (Phase 15).
  await page.getByLabel('City').fill('Nonexistent City')
  await expect(page.getByText('No records match the current filters.')).toBeVisible()
  await page.getByLabel('City').fill('')

  await page.getByRole('link', { name: 'Analytics' }).click()
  const recordsLabel = page.getByText('Records', { exact: true })
  await expect(recordsLabel.locator('xpath=following-sibling::p')).toHaveText('1')
  await expect(page.getByText('100.0%')).toBeVisible()
})
