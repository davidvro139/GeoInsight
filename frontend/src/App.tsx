import { Navigate, Route, Routes } from 'react-router-dom'
import { RequireAuth } from '@/auth/RequireAuth'
import { AddressReviewPage } from '@/pages/AddressReviewPage'
import { AnalyticsPage } from '@/pages/AnalyticsPage'
import { CreateOrganizationPage } from '@/pages/CreateOrganizationPage'
import { DatasetLayout } from '@/pages/DatasetLayout'
import { DatasetMapPage } from '@/pages/DatasetMapPage'
import { DatasetOverviewPage } from '@/pages/DatasetOverviewPage'
import { DatasetsPage } from '@/pages/DatasetsPage'
import { GeocodingPage } from '@/pages/GeocodingPage'
import { ImportsListPage } from '@/pages/ImportsListPage'
import { ImportWizardPage } from '@/pages/ImportWizardPage'
import { LoginPage } from '@/pages/LoginPage'
import { OrganizationLayout } from '@/pages/OrganizationLayout'
import { OrganizationRedirectPage } from '@/pages/OrganizationRedirectPage'
import { OrganizationSettingsPage } from '@/pages/OrganizationSettingsPage'
import { ExportsPage } from '@/pages/ExportsPage'
import { ForgotPasswordPage } from '@/pages/ForgotPasswordPage'
import { GapAnalysisPage } from '@/pages/GapAnalysisPage'
import { RegisterPage } from '@/pages/RegisterPage'
import { ResetPasswordPage } from '@/pages/ResetPasswordPage'
import { SavedViewsPage } from '@/pages/SavedViewsPage'
import { WelcomePage } from '@/pages/WelcomePage'

function App() {
  return (
    <Routes>
      <Route path="/welcome" element={<WelcomePage />} />
      <Route path="/login" element={<LoginPage />} />
      <Route path="/register" element={<RegisterPage />} />
      <Route path="/forgot-password" element={<ForgotPasswordPage />} />
      <Route path="/reset-password" element={<ResetPasswordPage />} />

      <Route
        path="/organizations/new"
        element={
          <RequireAuth>
            <CreateOrganizationPage />
          </RequireAuth>
        }
      />

      <Route
        path="/organizations/:organizationId"
        element={
          <RequireAuth>
            <OrganizationLayout />
          </RequireAuth>
        }
      >
        <Route index element={<DatasetsPage />} />
        <Route path="settings" element={<OrganizationSettingsPage />} />

        <Route path="datasets/:datasetId/imports/new" element={<ImportWizardPage />} />
        <Route path="datasets/:datasetId" element={<DatasetLayout />}>
          <Route index element={<DatasetOverviewPage />} />
          <Route path="map" element={<DatasetMapPage />} />
          <Route path="imports" element={<ImportsListPage />} />
          <Route path="geocoding" element={<GeocodingPage />} />
          <Route path="review" element={<AddressReviewPage />} />
          <Route path="analytics" element={<AnalyticsPage />} />
          <Route path="gap-analysis" element={<GapAnalysisPage />} />
          <Route path="saved-views" element={<SavedViewsPage />} />
          <Route path="exports" element={<ExportsPage />} />
        </Route>
      </Route>

      <Route
        path="/"
        element={
          <RequireAuth>
            <OrganizationRedirectPage />
          </RequireAuth>
        }
      />

      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  )
}

export default App
