import type { Camera } from '../api/types'

export type CameraStatusKey = 'paused' | 'recording' | 'awake' | 'asleep'

export interface CameraStatus {
  key: CameraStatusKey
  label: string
}

/**
 * Estado visible de una cámara a partir de lo que informa la API. Una ONVIF está siempre encendida:
 * "despierta" significa que hay movimiento.
 */
export function cameraStatus(
  camera: Pick<Camera, 'enabled' | 'isAwake' | 'isRecording'> & Partial<Pick<Camera, 'kind' | 'connected'>>,
): CameraStatus {
  const onvif = camera.kind === 'Onvif'
  if (!camera.enabled) return { key: 'paused', label: 'Pausada' }
  if (camera.isAwake && camera.isRecording) return { key: 'recording', label: 'Grabando' }
  if (camera.isAwake) return { key: 'awake', label: onvif ? 'Movimiento' : 'Despierta' }
  if (onvif && camera.connected === false) return { key: 'asleep', label: 'Sin conexión' }
  return { key: 'asleep', label: onvif ? 'En espera' : 'En reposo' }
}

export const LOW_BATTERY = 15

/** Nivel de batería para el color: bajo (< 15), medio (< 40) o bien. */
export function batteryLevel(percent: number) {
  return percent < LOW_BATTERY ? 'low' : percent < 40 ? 'mid' : 'ok'
}
