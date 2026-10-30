import { useEffect, useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { api } from '../../api/client'
import type { Camera, Settings, TestConnectionResult } from '../../api/types'
import { Stepper } from '../../components/Stepper'
import { Switch } from '../../components/Switch'
import { useToast } from '../../components/toastContext'
import { useCameras } from '../cameras/useCameras'
import { CameraPanel } from './CameraPanel'
import { emptyDraft, isChanged, streamFromName, toDraft } from './cameraDraft'
import type { CameraDraft } from './cameraDraft'
import { useSettings } from './useSettings'

type NumericKey = 'maxClipSeconds' | 'cooldownSeconds' | 'retentionDays' | 'lowBatteryPercent' | 'silentCameraHours'

interface Field {
  key: NumericKey
  label: string
  hint: string
  unit: string
  step: number
  min: number
  max: number
}

/** Límites de UpdateSettings en la API. */
const fields: Field[] = [
  {
    key: 'maxClipSeconds',
    label: 'Duración máxima por clip',
    hint: 'Más corto ahorra batería: la cámara no se duerme mientras graba.',
    unit: 's',
    step: 10,
    min: 5,
    max: 600,
  },
  {
    key: 'cooldownSeconds',
    label: 'Pausa entre activaciones',
    hint: 'Tiempo de espera antes de volver a grabar tras un clip.',
    unit: 's',
    step: 5,
    min: 0,
    max: 600,
  },
  {
    key: 'retentionDays',
    label: 'Conservar clips durante',
    hint: 'Los más antiguos se borran automáticamente una vez al día.',
    unit: 'días',
    step: 1,
    min: 1,
    max: 365,
  },
]

const alertFields: Field[] = [
  {
    key: 'lowBatteryPercent',
    label: 'Batería baja',
    hint: 'Avisa cuando una cámara baja de este nivel, o cuando el pronóstico da 3 días o menos.',
    unit: '%',
    step: 5,
    min: 5,
    max: 50,
  },
  {
    key: 'silentCameraHours',
    label: 'Cámara sin despertar',
    hint: 'Avisa si una cámara habilitada no despierta en este tiempo. Súbelo si alguna ve poco movimiento.',
    unit: 'h',
    step: 6,
    min: 6,
    max: 336,
  },
]

const NEW = 'new'
type Key = string // id de cámara, o NEW para el alta

const settingsValid = (s: Settings) =>
  [...fields, ...alertFields].every((f) => Number.isInteger(s[f.key]) && s[f.key] >= f.min && s[f.key] <= f.max)

function FieldRow({ field, form, onChange }: { field: Field; form: Settings; onChange: (s: Settings) => void }) {
  return (
    <div className="row">
      <div className="row-text">
        <div className="row-label">{field.label}</div>
        <div className="row-hint">{field.hint}</div>
      </div>
      <Stepper
        label={field.label}
        value={form[field.key]}
        min={field.min}
        max={field.max}
        step={field.step}
        unit={field.unit}
        onChange={(value) => onChange({ ...form, [field.key]: value })}
      />
    </div>
  )
}

export function SettingsPage() {
  const queryClient = useQueryClient()
  const toast = useToast()
  const settings = useSettings()
  const cameras = useCameras()

  // Borradores: mientras el usuario no edite, se muestra lo del servidor.
  const [settingsDraft, setSettingsDraft] = useState<Settings | null>(null)
  const [drafts, setDrafts] = useState<Record<Key, CameraDraft>>({})
  const [openKey, setOpenKey] = useState<Key | null>(null)
  const [errors, setErrors] = useState<Record<Key, string>>({})
  const [saveError, setSaveError] = useState<string | null>(null)
  const [tests, setTests] = useState<Record<Key, TestConnectionResult | 'pending'>>({})

  const list = cameras.data ?? []
  const byKey = new Map(list.map((c) => [String(c.id), c]))
  const form = settingsDraft ?? settings.data
  const settingsDirty =
    !!settingsDraft &&
    !!settings.data &&
    (Object.keys(settingsDraft) as (keyof Settings)[]).some((k) => settingsDraft[k] !== settings.data![k])
  const changedKeys = Object.keys(drafts).filter((k) => isChanged(drafts[k], byKey.get(k)))
  const dirty = settingsDirty || changedKeys.length > 0

  useEffect(() => {
    if (!dirty) return
    const warn = (e: BeforeUnloadEvent) => e.preventDefault()
    window.addEventListener('beforeunload', warn)
    return () => window.removeEventListener('beforeunload', warn)
  }, [dirty])

  const draftOf = (key: Key) => drafts[key] ?? (byKey.has(key) ? toDraft(byKey.get(key)!) : emptyDraft)

  const change = (key: Key, patch: Partial<CameraDraft>) =>
    setDrafts((prev) => {
      const current = prev[key] ?? draftOf(key)
      const next = { ...current, ...patch }
      if (!next.streamEdited && patch.name !== undefined) next.go2rtcStream = streamFromName(next.name)
      return { ...prev, [key]: next }
    })

  const dropDraft = (key: Key) =>
    setDrafts((prev) => {
      const next = { ...prev }
      delete next[key]
      return next
    })

  const save = useMutation({
    mutationFn: async () => {
      const failures: Record<Key, string> = {}
      if (settingsDirty && settingsDraft) {
        const saved = await api.updateSettings(settingsDraft)
        queryClient.setQueryData(['settings'], saved)
      }
      for (const key of changedKeys) {
        const { streamEdited: _, ...input } = drafts[key]
        try {
          if (key === NEW) await api.createCamera(input)
          else await api.updateCamera(Number(key), input)
        } catch (e) {
          failures[key] = e instanceof Error ? e.message : 'No se pudo guardar.'
        }
      }
      return failures
    },
    onMutate: () => setSaveError(null),
    onSuccess: (failures) => {
      setSettingsDraft(null)
      setErrors(failures)
      // Se conservan solo los borradores que fallaron, para corregirlos.
      setDrafts((prev) => Object.fromEntries(Object.entries(prev).filter(([k]) => k in failures)))
      const failed = Object.keys(failures)
      if (failed.length > 0) setOpenKey(failed[0])
      else {
        if (openKey === NEW) setOpenKey(null)
        toast('Cambios guardados')
      }
      void queryClient.invalidateQueries({ queryKey: ['cameras'] })
    },
    onError: (e) => setSaveError(e.message),
  })

  const remove = useMutation({
    mutationFn: (camera: Camera) => api.deleteCamera(camera.id),
    onSuccess: (_, camera) => {
      dropDraft(String(camera.id))
      void queryClient.invalidateQueries({ queryKey: ['cameras'] })
      toast(`Cámara "${camera.name}" eliminada`)
    },
    onError: (e, camera) => setErrors((prev) => ({ ...prev, [String(camera.id)]: e.message })),
  })

  const runTest = async (key: Key) => {
    const d = draftOf(key)
    setTests((prev) => ({ ...prev, [key]: 'pending' }))
    try {
      const result = await api.testCamera({
        cameraId: key === NEW ? undefined : Number(key),
        kind: d.kind,
        tuyaDeviceId: d.kind === 'Tuya' ? d.tuyaDeviceId : undefined,
        ip: d.ip,
        dvripPort: d.dvripPort,
        username: d.username,
        password: d.password || undefined,
      })
      setTests((prev) => ({ ...prev, [key]: result }))
      // El modelo informado completa el campo si estaba vacío.
      if (result.ok && result.model && !d.model) change(key, { model: result.model })
    } catch (e) {
      setTests((prev) => ({ ...prev, [key]: { ok: false, message: e instanceof Error ? e.message : 'Error', model: null } }))
    }
  }

  const discard = () => {
    setSettingsDraft(null)
    setDrafts({})
    setErrors({})
    setSaveError(null)
    if (openKey === NEW) setOpenKey(null)
  }

  const addCamera = () => {
    if (!drafts[NEW]) setDrafts((prev) => ({ ...prev, [NEW]: emptyDraft }))
    setOpenKey(NEW)
  }

  const canSave = !form || settingsValid(form)

  const panel = (key: Key, camera?: Camera) => (
    <CameraPanel
      key={key}
      camera={camera}
      draft={draftOf(key)}
      open={openKey === key}
      onToggle={() => setOpenKey(openKey === key ? null : key)}
      onChange={(patch) => change(key, patch)}
      error={errors[key]}
      deleting={remove.isPending && remove.variables?.id === camera?.id}
      onDelete={() => {
        if (camera && window.confirm(`¿Eliminar la cámara "${camera.name}"?`)) remove.mutate(camera)
      }}
      test={tests[key]}
      onTest={() => void runTest(key)}
      onCancelNew={() => {
        dropDraft(NEW)
        setOpenKey(null)
      }}
    />
  )

  return (
    <div className="settings">
      <h1 className="page-title">Configuración</h1>

      <section className="settings-section">
        <div className="settings-intro">
          <div>
            <h2 className="section-title">Grabación</h2>
            <p>Cuánto graba cada vez que detecta movimiento, cuánto tiempo se guardan los clips y qué tan segura debe estar la IA.</p>
          </div>
        </div>
        <div className="settings-body">
          {settings.error && <p className="error">{settings.error.message}</p>}
          {form ? (
            <div className="rows">
              {fields.map((f) => (
                <FieldRow key={f.key} field={f} form={form} onChange={setSettingsDraft} />
              ))}
              <div className="row">
                <div className="row-text">
                  <div className="row-label">Confianza mínima de la IA</div>
                  <div className="row-hint">
                    Más alta evita falsos positivos (sombras, reflejos), pero puede perder personas lejanas o de noche.
                  </div>
                </div>
                <div className="range">
                  <input
                    type="range"
                    min={20}
                    max={95}
                    step={5}
                    aria-label="Confianza mínima de la IA"
                    value={Math.round(form.detectionMinConfidence * 100)}
                    onChange={(e) => setSettingsDraft({ ...form, detectionMinConfidence: e.target.valueAsNumber / 100 })}
                  />
                  <span>{Math.round(form.detectionMinConfidence * 100)} %</span>
                </div>
              </div>
            </div>
          ) : (
            !settings.error && <p className="muted">Cargando…</p>
          )}
        </div>
      </section>

      <div className="divider" />

      <section className="settings-section">
        <div className="settings-intro">
          <div>
            <h2 className="section-title">Alertas del sistema</h2>
            <p>
              Avisos por ntfy cuando algo deja de funcionar: batería, cámaras que no despiertan, go2rtc, Tuya, R2 o el
              disco. El detalle está en <Link to="/status">Estado</Link>.
            </p>
          </div>
        </div>
        <div className="settings-body">
          {form ? (
            <div className="rows">
              <div className="row">
                <div className="row-text">
                  <div className="row-label">Avisar problemas</div>
                  <div className="row-hint">Apagado, los problemas solo se ven en el panel Estado.</div>
                </div>
                <Switch
                  label="Avisar problemas"
                  checked={form.healthAlertsEnabled}
                  onChange={(checked) => setSettingsDraft({ ...form, healthAlertsEnabled: checked })}
                />
              </div>
              {alertFields.map((f) => (
                <FieldRow key={f.key} field={f} form={form} onChange={setSettingsDraft} />
              ))}
            </div>
          ) : (
            !settings.error && <p className="muted">Cargando…</p>
          )}
        </div>
      </section>

      <div className="divider" />

      <section className="settings-section">
        <div className="settings-intro">
          <div>
            <h2 className="section-title">Cámaras</h2>
            <p>
              {list.length === 1 ? '1 cámara conectada' : `${list.length} cámaras conectadas`}: iCSee por DVRIP en tu red
              local y dispositivos Tuya por la nube.
            </p>
          </div>
          <button className="btn btn-solid" style={{ padding: '9px 14px', borderRadius: 10 }} onClick={addCamera}>
            + Agregar cámara
          </button>
        </div>
        <div className="settings-body cam-list">
          {cameras.error && <p className="error">{cameras.error.message}</p>}
          {drafts[NEW] && panel(NEW)}
          {list.map((c) => panel(String(c.id), c))}
          {cameras.data && list.length === 0 && !drafts[NEW] && <div className="empty">Aún no hay cámaras.</div>}
        </div>
      </section>

      {dirty && (
        <div className="save-bar" role="region" aria-label="Cambios sin guardar">
          <span className={saveError ? 'error' : undefined}>{saveError ?? 'Cambios sin guardar'}</span>
          <div>
            <button className="btn btn-ghost" onClick={discard} disabled={save.isPending}>
              Descartar
            </button>
            <button className="btn btn-primary" onClick={() => save.mutate()} disabled={save.isPending || !canSave}>
              {save.isPending ? 'Guardando…' : 'Guardar'}
            </button>
          </div>
        </div>
      )}
    </div>
  )
}
