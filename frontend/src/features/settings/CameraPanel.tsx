import type { Camera, CameraKind, DetectionMode, MotionSensitivity, Rotation, TestConnectionResult } from '../../api/types'
import { BatteryBadge } from '../../components/BatteryBadge'
import { Segmented } from '../../components/Segmented'
import { Switch } from '../../components/Switch'
import { cameraStatus } from '../../lib/cameraStatus'
import type { CameraDraft } from './cameraDraft'

const sensitivities: { value: MotionSensitivity; label: string }[] = [
  { value: 'Low', label: 'Baja' },
  { value: 'Medium', label: 'Media' },
  { value: 'High', label: 'Alta' },
]

const kinds: { value: CameraKind; label: string }[] = [
  { value: 'Icsee', label: 'iCSee' },
  { value: 'Tuya', label: 'Tuya' },
]

const rotations: { value: `${Rotation}`; label: string }[] = [
  { value: '0', label: '0°' },
  { value: '90', label: '90°' },
  { value: '180', label: '180°' },
  { value: '270', label: '270°' },
]

const detectionModes: { value: DetectionMode; label: string }[] = [
  { value: 'Off', label: 'Desactivada' },
  { value: 'Tag', label: 'Solo etiquetar' },
  { value: 'Filter', label: 'Filtrar' },
]

interface Props {
  camera?: Camera
  draft: CameraDraft
  open: boolean
  onToggle: () => void
  onChange: (patch: Partial<CameraDraft>) => void
  error?: string
  onDelete: () => void
  deleting: boolean
  test?: TestConnectionResult | 'pending'
  onTest: () => void
  onCancelNew?: () => void
}

export function CameraPanel({ camera, draft, open, onToggle, onChange, error, onDelete, deleting, test, onTest, onCancelNew }: Props) {
  const status = camera ? cameraStatus(camera) : null
  const pending = camera && (camera.sensitivityPending || draft.motionSensitivity !== camera.motionSensitivity)
  const id = `cam-${camera?.id ?? 'new'}`
  const tuya = draft.kind === 'Tuya'

  const text = (key: 'name' | 'model' | 'ip' | 'username' | 'password' | 'go2rtcStream' | 'tuyaDeviceId' | 'tuyaTriggerDeviceIds') => ({
    value: draft[key],
    onChange: (e: React.ChangeEvent<HTMLInputElement>) => onChange({ [key]: e.target.value }),
  })

  return (
    <div className="cam-acc" data-open={open || undefined} data-invalid={error ? true : undefined} data-status={status?.key}>
      <button type="button" className="cam-acc-head" aria-expanded={open} aria-controls={id} onClick={onToggle}>
        <span className="cam-acc-thumb">
          {camera?.lastThumbnailUrl ? <img src={camera.lastThumbnailUrl} alt="" /> : <span className="frame-empty" />}
        </span>
        <span className="cam-acc-title">
          <strong>{draft.name || (camera ? camera.name : 'Nueva cámara')}</strong>
          <span>
            {status ? (
              <>
                <span className="dot" aria-hidden />
                {status.label} ·{' '}
                <span className="mono">{camera!.kind === 'Tuya' ? `Tuya · ${camera!.tuyaDeviceId}` : camera!.ip}</span>
              </>
            ) : (
              'Sin guardar'
            )}
          </span>
        </span>
        {camera && <BatteryBadge percent={camera.batteryPercent} />}
        <span className="cam-acc-chev" aria-hidden>
          {open ? '−' : '+'}
        </span>
      </button>

      {open && (
        <>
          <div className="cam-acc-body" id={id}>
            {!camera && (
              <div className="cam-acc-wide">
                <div>
                  <span className="row-label">Tipo</span>
                  <span className="row-hint">
                    {tuya
                      ? 'Cámara, timbre o cerradura con cámara de la app Tuya Smart: graba con el movimiento, el timbre o las alarmas.'
                      : 'Cámara iCSee a batería en tu red local: graba al detectar movimiento.'}
                  </span>
                </div>
                <Segmented className="seg-input" label="Tipo de cámara" value={draft.kind} options={kinds} onChange={(kind) => onChange({ kind })} />
              </div>
            )}
            <label className="field">
              <span>Nombre</span>
              <input className="input" required maxLength={100} {...text('name')} />
            </label>
            <label className="field">
              <span>Modelo</span>
              <input className="input" maxLength={100} placeholder="Se completa al conectar" {...text('model')} />
            </label>
            {tuya ? (
              <>
                <label className="field">
                  <span>Device ID</span>
                  <input className="input mono" maxLength={64} autoComplete="off" placeholder="eb8d8e837c0a688a86f1kb" {...text('tuyaDeviceId')} />
                  <small>Tuya Developer Platform → tu proyecto → Devices.</small>
                </label>
                <label className="field">
                  <span>Sensores que la activan</span>
                  <input
                    className="input mono"
                    maxLength={400}
                    autoComplete="off"
                    placeholder="Opcional: Device ID de un sensor de movimiento"
                    {...text('tuyaTriggerDeviceIds')}
                  />
                  <small>Su movimiento despierta y graba esta cámara. Varios, separados por coma.</small>
                </label>
              </>
            ) : (
              <>
              <label className="field">
                <span>Dirección IP</span>
                <input className="input mono" inputMode="decimal" placeholder="192.168.1.86" {...text('ip')} />
              </label>
              <label className="field">
                <span>Puerto DVRIP</span>
                <input
                  className="input mono"
                  type="number"
                  min={1}
                  max={65535}
                  value={Number.isNaN(draft.dvripPort) ? '' : draft.dvripPort}
                  onChange={(e) => onChange({ dvripPort: e.target.valueAsNumber })}
                />
              </label>
              <label className="field">
                <span>Usuario</span>
                <input className="input" maxLength={64} autoComplete="off" {...text('username')} />
              </label>
              <label className="field">
                <span>Contraseña</span>
                <input
                  className="input"
                  type="password"
                  maxLength={128}
                  autoComplete="new-password"
                  placeholder={camera?.hasPassword ? '••••••••  (sin cambios)' : ''}
                  aria-invalid={camera && !camera.hasPassword && !draft.password ? true : undefined}
                  {...text('password')}
                />
                {camera && !camera.hasPassword && <small className="error">Falta: sin ella no se puede abrir la cámara.</small>}
              </label>
              </>
            )}
            <label className="field">
              <span>Stream en go2rtc</span>
              <input
                className="input mono"
                maxLength={100}
                pattern="[A-Za-z0-9_\-]+"
                value={draft.go2rtcStream}
                onChange={(e) => onChange({ go2rtcStream: e.target.value, streamEdited: true })}
              />
              <small>Nombre interno; se registra solo.</small>
            </label>

            {!tuya && (
              <div className="cam-acc-wide">
                <div>
                  <span className="row-label">Sensibilidad al movimiento</span>
                  <span className="row-hint">Más alta detecta antes, pero graba más falsos positivos.</span>
                  {pending && <span className="hint-pending">Se aplicará cuando la cámara despierte.</span>}
                </div>
                <Segmented
                  className="seg-input"
                  label="Sensibilidad al movimiento"
                  value={draft.motionSensitivity}
                  options={sensitivities}
                  onChange={(motionSensitivity) => onChange({ motionSensitivity })}
                />
              </div>
            )}

            <div className="cam-acc-wide">
              <div>
                <span className="row-label">Rotación</span>
                <span className="row-hint">Para cámaras montadas de lado: gira el video y los clips a la derecha.</span>
              </div>
              <Segmented
                className="seg-input"
                label="Rotación"
                value={`${draft.rotation}` as `${Rotation}`}
                options={rotations}
                onChange={(value) => onChange({ rotation: Number(value) as Rotation })}
              />
            </div>

            <div className="cam-acc-wide">
              <div>
                <span className="row-label">Detección IA</span>
                <span className="row-hint">
                  Filtrar descarta los clips sin personas ni animales; timbre y alarmas siempre se guardan.
                </span>
                {draft.detectionMode === 'Filter' && !draft.detectPeople && !draft.detectAnimals && (
                  <span className="error">Para filtrar, activa personas, animales o ambos.</span>
                )}
              </div>
              <div className="detect-opts">
                <Segmented
                  className="seg-input"
                  label="Detección IA"
                  value={draft.detectionMode}
                  options={detectionModes}
                  onChange={(detectionMode) => onChange({ detectionMode })}
                />
                <div className="checks">
                  <label className="check">
                    <input
                      type="checkbox"
                      checked={draft.detectPeople}
                      disabled={draft.detectionMode === 'Off'}
                      onChange={(e) => onChange({ detectPeople: e.target.checked })}
                    />
                    Personas
                  </label>
                  <label className="check">
                    <input
                      type="checkbox"
                      checked={draft.detectAnimals}
                      disabled={draft.detectionMode === 'Off'}
                      onChange={(e) => onChange({ detectAnimals: e.target.checked })}
                    />
                    Animales
                  </label>
                </div>
              </div>
            </div>

            <div className="cam-acc-wide">
              <div>
                <span className="row-label">{tuya ? 'Grabar con eventos de Tuya' : 'Grabar al detectar movimiento'}</span>
                <span className="row-hint">
                  {tuya
                    ? 'Desactívala para dejar de grabar y de recibir avisos del dispositivo sin eliminarlo.'
                    : 'Desactívala para dejar de grabar sin eliminar la cámara.'}
                </span>
              </div>
              <Switch
                label={tuya ? 'Grabar con eventos de Tuya' : 'Grabar al detectar movimiento'}
                checked={draft.enabled}
                onChange={(enabled) => onChange({ enabled })}
              />
            </div>
          </div>

          <div className="cam-acc-foot">
            {camera ? (
              <button type="button" className="btn btn-link btn-link-danger" onClick={onDelete} disabled={deleting}>
                {deleting ? 'Eliminando…' : 'Eliminar cámara'}
              </button>
            ) : (
              <button type="button" className="btn btn-link" onClick={onCancelNew}>
                Descartar alta
              </button>
            )}
            <div>
              {error && <span className="error">{error}</span>}
              {test && test !== 'pending' && (
                <span className="test-result" data-ok={test.ok}>
                  {test.message}
                  {test.model && ` · ${test.model}`}
                </span>
              )}
              <button type="button" className="btn btn-solid" onClick={onTest} disabled={test === 'pending'}>
                {test === 'pending' ? 'Probando…' : 'Probar conexión'}
              </button>
            </div>
          </div>
        </>
      )}
    </div>
  )
}
