import { getAccessToken, getOidc } from '../auth/oidc'
import type {
  Camera,
  CameraInput,
  PlaybackUrl,
  RecordingPage,
  RecordingStatus,
  RecordingSummary,
  Settings,
  TestConnectionRequest,
  TestConnectionResult,
  TimelineItem,
} from './types'

export class ApiError extends Error {
  readonly status: number

  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers)
  headers.set('Authorization', `Bearer ${await getAccessToken()}`)
  if (init.body && typeof init.body === 'string' && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json')
  }

  const response = await fetch(path, { ...init, headers })

  // Token rechazado por la API (sesión revocada en Keycloak): se vuelve a iniciar sesión.
  if (response.status === 401) {
    await getOidc().logout({ redirectTo: 'current page' })
  }

  if (!response.ok) {
    throw new ApiError(response.status, await errorMessage(response))
  }

  if (response.status === 204) return undefined as T
  const type = response.headers.get('Content-Type') ?? ''
  return (type.includes('json') ? response.json() : response.text()) as Promise<T>
}

async function errorMessage(response: Response): Promise<string> {
  try {
    const body = await response.json()
    if (body.errors) return Object.values(body.errors as Record<string, string[]>).flat().join(' ')
    return body.message ?? body.detail ?? body.title ?? `Error ${response.status}`
  } catch {
    return `Error ${response.status}`
  }
}

const json = (data: unknown) => JSON.stringify(data)

function query(params: Record<string, string | number | undefined>) {
  const q = new URLSearchParams()
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined) q.set(key, String(value))
  }
  return q
}

export const api = {
  /** today: rango del día local, para que la API cuente "clips hoy" en esta zona horaria. */
  cameras: (today?: { from: string; to: string }) =>
    request<Camera[]>('/api/cameras' + (today ? `?${query({ todayFrom: today.from, todayTo: today.to })}` : '')),

  testCamera: (data: TestConnectionRequest) =>
    request<TestConnectionResult>('/api/cameras/test', { method: 'POST', body: json(data) }),

  createCamera: (data: CameraInput) =>
    request<Camera>('/api/cameras', { method: 'POST', body: json(data) }),

  updateCamera: (id: number, data: CameraInput) =>
    request<void>(`/api/cameras/${id}`, { method: 'PUT', body: json(data) }),

  deleteCamera: (id: number) => request<void>(`/api/cameras/${id}`, { method: 'DELETE' }),

  recordings: (params: {
    from?: string
    to?: string
    cameraId?: number
    status?: RecordingStatus
    page?: number
    pageSize?: number
  }) => request<RecordingPage>(`/api/recordings?${query(params)}`),

  timeline: (params: { from: string; to: string; cameraId?: number }) =>
    request<TimelineItem[]>(`/api/recordings/timeline?${query(params)}`),

  recordingSummary: () => request<RecordingSummary>('/api/recordings/summary'),

  playbackUrl: (id: string, download = false) =>
    request<PlaybackUrl>(`/api/recordings/${id}/play${download ? '?download=true' : ''}`),

  deleteRecording: (id: string) => request<void>(`/api/recordings/${id}`, { method: 'DELETE' }),

  settings: () => request<Settings>('/api/settings'),

  updateSettings: (data: Settings) =>
    request<Settings>('/api/settings', { method: 'PUT', body: json(data) }),

  webrtcAnswer: (cameraId: number, offerSdp: string) =>
    request<string>(`/api/live/${cameraId}/webrtc`, {
      method: 'POST',
      body: offerSdp,
      headers: { 'Content-Type': 'application/sdp' },
    }),
}
