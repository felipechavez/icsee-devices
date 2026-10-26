export type RecordingStatus = 'Recording' | 'Analyzing' | 'Uploading' | 'Uploaded' | 'Failed' | 'Discarded'

/** Off: sin IA. Tag: etiqueta y sube todo. Filter: descarta los clips sin personas ni animales. */
export type DetectionMode = 'Off' | 'Tag' | 'Filter'

export type MotionSensitivity = 'Low' | 'Medium' | 'High'

/** iCSee: DVRIP en la LAN. Tuya: cerradura con cámara, todo por la nube de Tuya. */
export type CameraKind = 'Icsee' | 'Tuya'

/** Giro a la derecha en grados, para cámaras montadas de lado. */
export type Rotation = 0 | 90 | 180 | 270

export interface Camera {
  id: number
  name: string
  kind: CameraKind
  /** Vacía en las Tuya. */
  ip: string
  dvripPort: number
  go2rtcStream: string
  enabled: boolean
  username: string
  /** La contraseña nunca llega al navegador: solo se sabe si está configurada. */
  hasPassword: boolean
  isAwake: boolean
  lastChangedAt: string | null
  model: string | null
  motionSensitivity: MotionSensitivity
  /** La sensibilidad se escribe en la cámara la próxima vez que despierte. */
  sensitivityPending: boolean
  // Último estado leído por DVRIP; null = desconocido o no soportado por el firmware.
  batteryPercent: number | null
  storageUsedBytes: number | null
  storageTotalBytes: number | null
  deviceInfoAt: string | null
  isRecording: boolean
  lastRecordingAt: string | null
  lastThumbnailUrl: string | null
  clipsToday: number | null
  /** Solo en las Tuya. */
  tuyaDeviceId: string | null
  rotation: Rotation
  /** Device IDs de sensores Tuya, separados por coma, cuyo movimiento despierta esta cámara. */
  tuyaTriggerDeviceIds: string | null
  detectionMode: DetectionMode
  detectPeople: boolean
  detectAnimals: boolean
}

/** Alta y edición. Al editar, una contraseña vacía conserva la actual. */
export type CameraInput = Pick<
  Camera,
  | 'name'
  | 'kind'
  | 'ip'
  | 'dvripPort'
  | 'go2rtcStream'
  | 'enabled'
  | 'username'
  | 'motionSensitivity'
  | 'rotation'
  | 'detectionMode'
  | 'detectPeople'
  | 'detectAnimals'
> & {
  model: string
  password: string
  tuyaDeviceId: string
  tuyaTriggerDeviceIds: string
}

export interface TestConnectionRequest {
  cameraId?: number
  kind: CameraKind
  tuyaDeviceId?: string
  ip: string
  dvripPort: number
  username: string
  password?: string
}

export interface TestConnectionResult {
  ok: boolean
  message: string
  model: string | null
}

export interface Recording {
  id: string
  cameraId: number
  cameraName: string
  startedAt: string
  endedAt: string | null
  durationSec: number | null
  sizeBytes: number | null
  status: RecordingStatus
  thumbnailUrl: string | null
  /** Lo que vio la IA: 'persona', 'perro'… o 'nada'. Null = sin analizar. */
  detection: string | null
  detectionScore: number | null
}

export interface RecordingPage {
  items: Recording[]
  page: number
  pageSize: number
  totalCount: number
  hasMore: boolean
}

/** Marca liviana para la línea de tiempo y el histograma del día. */
export interface TimelineItem {
  id: string
  cameraId: number
  startedAt: string
  status: RecordingStatus
}

export interface RecordingSummary {
  totalCount: number
}

export interface PlaybackUrl {
  url: string
  expiresAt: string
}

export interface Settings {
  maxClipSeconds: number
  cooldownSeconds: number
  retentionDays: number
  /** Confianza mínima de la IA (0,2–0,95). */
  detectionMinConfidence: number
}

// Mensajes del hub /hubs/events
export interface CameraStatusMessage {
  cameraId: number
  at: string
}

export interface CameraInfoMessage {
  cameraId: number
  model: string | null
  batteryPercent: number | null
  storageUsedBytes: number | null
  storageTotalBytes: number | null
  motionSensitivity: MotionSensitivity
  sensitivityPending: boolean
  deviceInfoAt: string | null
}

export interface RecordingStartedMessage {
  recordingId: string
  cameraId: number
  startedAt: string
}

export interface RecordingCompletedMessage {
  recordingId: string
  cameraId: number
  startedAt: string
}
