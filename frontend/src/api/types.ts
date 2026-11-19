/** Evicted: se borró del equipo por falta de espacio antes de subirse (sin internet). */
export type RecordingStatus = 'Recording' | 'Analyzing' | 'Uploading' | 'Uploaded' | 'Failed' | 'Discarded' | 'Evicted'

/** Off: sin IA. Tag: etiqueta y sube todo. Filter: descarta los clips sin personas ni animales. */
export type DetectionMode = 'Off' | 'Tag' | 'Filter'

export type MotionSensitivity = 'Low' | 'Medium' | 'High'

/**
 * iCSee: DVRIP en la LAN. Tuya: cámara, timbre o cerradura, todo por la nube de Tuya.
 * Onvif: cámara enchufada en la LAN (p. ej. Imou), siempre encendida; avisa el movimiento por ONVIF.
 */
export type CameraKind = 'Icsee' | 'Tuya' | 'Onvif'

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
  /** Puerto HTTP del servicio ONVIF (solo ONVIF). */
  onvifPort: number
  /** Solo ONVIF: suscripción de eventos activa. Null = no aplica o aún no se intentó. */
  connected: boolean | null
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
  | 'onvifPort'
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
  onvifPort?: number
  username: string
  password?: string
}

export interface TestConnectionResult {
  ok: boolean
  message: string
  model: string | null
}

/** Usuario de la sesión (GET /api/me). */
export interface Me {
  username: string
  isAdmin: boolean
  /** Rol camguard-viewer: solo ve cámaras y grabaciones (sin Estado, Configuración ni borrar). */
  isReadOnly: boolean
}

/** Cámara hallada por "Buscar en la red" que aún no está en CamGuard. */
export interface DiscoveredCamera {
  kind: CameraKind
  ip: string | null
  /** Puerto DVRIP (iCSee) o del servicio ONVIF; null en Tuya. */
  port: number | null
  tuyaDeviceId: string | null
  suggestedName: string
  model: string | null
  detail: string | null
}

export interface DiscoveryResult {
  found: DiscoveredCamera[]
  subnets: string[]
  warnings: string[]
  elapsedMs: number
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
  /** Avisar por ntfy los problemas del sistema; el panel Estado los muestra igual. */
  healthAlertsEnabled: boolean
  /** 5–50 %. */
  lowBatteryPercent: number
  /** 6–336 h sin despertar para dar una cámara por caída. */
  silentCameraHours: number
  /** 1–500 GB de clips esperando subir; al pasarlo se borran los de menor prioridad. */
  localMaxGb: number
}

// Estado del sistema (GET /api/status)
export type HealthSeverity = 'Warning' | 'Critical'

export interface HealthIssue {
  /** "battery:{id}", "silent:{id}", "offline:{id}", "service:{nombre}", "queue", "disk", "backup" o "backup-verify". */
  key: string
  severity: HealthSeverity
  title: string
  detail: string | null
  cameraId: number | null
  since: string
}

export interface BatteryPoint {
  at: string
  percent: number
}

export interface CameraHealth {
  id: number
  name: string
  kind: CameraKind
  enabled: boolean
  batteryPercent: number | null
  deviceInfoAt: string | null
  /** Null hasta tener un día de lecturas bajando. */
  batteryDaysLeft: number | null
  /** Últimos 14 días, en orden cronológico. */
  batteryHistory: BatteryPoint[]
  lastAwakeAt: string | null
  /** Promedio de los últimos 7 días. */
  activationsPerDay: number
  /** Solo ONVIF: suscripción de eventos activa; null = no aplica o aún no se intentó. */
  connected: boolean | null
  /** Solo ONVIF: desde cuándo está conectada o desconectada. */
  connectionSince: string | null
  connectionError: string | null
}

/** ntfy solo se muestra: no abre problema (el aviso llegaría atrasado). */
export type ServiceName = 'go2rtc' | 'tuya' | 'r2' | 'ntfy'

export interface ServiceHealth {
  name: ServiceName
  ok: boolean
  /** Desde cuándo está en el estado actual; en R2 sano, la última subida. */
  since: string | null
  detail: string | null
}

export interface SystemStatus {
  checkedAt: string
  issues: HealthIssue[]
  cameras: CameraHealth[]
  services: ServiceHealth[]
  uploads: { pending: number; analyzing: number; oldestPendingAt: string | null }
  /** Null si no se pudo leer. */
  disk: { freeBytes: number; totalBytes: number } | null
  storage: { usedBytes: number; clips: number; estimatedMonthlyUsd: number }
  /** Null si no se revisan (desarrollo). */
  backups: BackupHealth | null
}

export interface BackupHealth {
  /** Último camguard-*.sql.gz; null = ninguno. */
  latestAt: string | null
  /** Última verificación (scripts/verify-backup.sh); null = nunca. */
  verifiedAt: string | null
  verifyOk: boolean | null
  verifyDetail: string | null
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

export interface HealthUpdatedMessage {
  issueCount: number
}
