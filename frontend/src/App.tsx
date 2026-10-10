import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'
import { ApiError } from './api/client'
import { Layout } from './components/Layout'
import { ToastProvider } from './components/Toast'
import { HomePage } from './features/live/HomePage'
import { RecordingsPage } from './features/recordings/RecordingsPage'
import { SettingsPage } from './features/settings/SettingsPage'
import { EventsHubProvider } from './hooks/useEventsHub'

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      // No reintentar errores del cliente (401, 404, 409…).
      retry: (count, error) => !(error instanceof ApiError && error.status < 500) && count < 2,
    },
  },
})

// La sesión ya está garantizada al montar (bootstrap.tsx + autoLogin de oidc-spa).
export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <EventsHubProvider>
        <ToastProvider>
        <BrowserRouter>
          <Routes>
            <Route element={<Layout />}>
              <Route index element={<HomePage />} />
              <Route path="recordings" element={<RecordingsPage />} />
              <Route path="settings" element={<SettingsPage />} />
            </Route>
            <Route path="*" element={<Navigate to="/" replace />} />
          </Routes>
        </BrowserRouter>
        </ToastProvider>
      </EventsHubProvider>
    </QueryClientProvider>
  )
}
