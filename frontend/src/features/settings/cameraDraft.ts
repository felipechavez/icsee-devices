import type { Camera, CameraInput } from '../../api/types'

export interface CameraDraft extends CameraInput {
  /** En un alta, el stream sigue al nombre hasta que el usuario lo edite. */
  streamEdited: boolean
}

export function toDraft(camera: Camera): CameraDraft {
  return {
    name: camera.name,
    kind: camera.kind,
    model: camera.model ?? '',
    ip: camera.ip,
    dvripPort: camera.dvripPort,
    go2rtcStream: camera.go2rtcStream,
    enabled: camera.enabled,
    username: camera.username,
    password: '',
    motionSensitivity: camera.motionSensitivity,
    tuyaDeviceId: camera.tuyaDeviceId ?? '',
    tuyaTriggerDeviceIds: camera.tuyaTriggerDeviceIds ?? '',
    rotation: camera.rotation,
    streamEdited: true,
  }
}

export const emptyDraft: CameraDraft = {
  name: '',
  kind: 'Icsee',
  model: '',
  ip: '',
  dvripPort: 34567,
  go2rtcStream: '',
  enabled: true,
  username: 'admin',
  password: '',
  motionSensitivity: 'Medium',
  tuyaDeviceId: '',
  tuyaTriggerDeviceIds: '',
  rotation: 0,
  streamEdited: false,
}

/** Nombre de stream sugerido a partir del nombre de la cámara: "Patio trasero" → "patio_trasero". */
export function streamFromName(name: string) {
  return name
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .toLowerCase()
    .trim()
    .replace(/[^a-z0-9]+/g, '_')
    .replace(/^_+|_+$/g, '')
}

/** ¿El borrador difiere de lo guardado? Sin cámara (alta), siempre. */
export function isChanged(draft: CameraDraft, camera?: Camera) {
  if (!camera) return true
  const saved = toDraft(camera)
  return (Object.keys(saved) as (keyof CameraDraft)[]).some((k) => k !== 'streamEdited' && draft[k] !== saved[k])
}
