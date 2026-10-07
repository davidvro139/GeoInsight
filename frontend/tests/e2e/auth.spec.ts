import { expect, test } from '@playwright/test'

test.describe('Auth', () => {
  test('register, log out, log back in', async ({ page }) => {
    const email = `e2e-auth-${Date.now()}@example.com`

    await page.goto('/register')
    await page.getByLabel('Name').fill('E2E Auth Test')
    await page.getByLabel('Email').fill(email)
    await page.getByLabel('Password').fill('Password123')
    await page.getByRole('button', { name: /create account/i }).click()

    // A brand-new user has no organizations yet — lands on org creation.
    await expect(page).toHaveURL(/organizations\/new/)

    await page.getByRole('button', { name: /log out/i }).click()
    await expect(page).toHaveURL(/login/)

    await page.getByLabel('Email').fill(email)
    await page.getByLabel('Password').fill('Password123')
    await page.getByRole('button', { name: /^sign in$/i }).click()
    await expect(page).toHaveURL(/organizations\/new/)
  })

  test('forgot password issues a working reset link', async ({ page }) => {
    const email = `e2e-reset-${Date.now()}@example.com`

    await page.goto('/register')
    await page.getByLabel('Name').fill('E2E Reset Test')
    await page.getByLabel('Email').fill(email)
    await page.getByLabel('Password').fill('OldPassword123')
    await page.getByRole('button', { name: /create account/i }).click()
    await expect(page).toHaveURL(/organizations\/new/)

    await page.getByRole('button', { name: /log out/i }).click()
    await page.getByRole('link', { name: /forgot password/i }).click()
    await expect(page).toHaveURL(/forgot-password/)
    // Wait for the new page's own heading, not just the URL — on a fast SPA
    // route change the previous page's same-named "Email" field can still be
    // present for a tick, and filling too early lands on the wrong element.
    await expect(page.getByText('Reset your password', { exact: true })).toBeVisible()

    await page.getByLabel('Email').fill(email)
    await page.getByRole('button', { name: /send reset link/i }).click()
    await expect(page.getByText('Continue to reset password')).toBeVisible()

    await page.getByRole('link', { name: /continue to reset password/i }).click()
    await expect(page).toHaveURL(/reset-password/)
    await expect(page.getByText('Choose a new password', { exact: true })).toBeVisible()

    await page.getByLabel('New password').fill('NewPassword456')
    await page.getByRole('button', { name: /^reset password$/i }).click()

    // Resetting signs the user straight in.
    await expect(page).toHaveURL(/organizations\/new/)

    await page.getByRole('button', { name: /log out/i }).click()
    await page.getByLabel('Email').fill(email)
    await page.getByLabel('Password').fill('OldPassword123')
    await page.getByRole('button', { name: /^sign in$/i }).click()
    await expect(page.getByText('Invalid email or password')).toBeVisible()

    await page.getByLabel('Password').fill('NewPassword456')
    await page.getByRole('button', { name: /^sign in$/i }).click()
    await expect(page).toHaveURL(/organizations\/new/)
  })
})
