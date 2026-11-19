import type { CameraKind, DiscoveredCamera, DiscoveryResult } from '../../api/types'

const kindLabels: Record<CameraKind, string> = { Icsee: 'iCSee', Onvif: 'ONVIF', Tuya: 'Tuya' }

interface Props {
  pending: boolean
  result?: DiscoveryResult
  error?: string
  /** Cámaras que ya se pasaron a un alta en esta búsqueda (para marcar "Agregada"). */
  added: ReadonlySet<string>
  onSearch: () => void
  onAdd: (found: DiscoveredCamera) => void
  onClose: () => void
}

/** Igual que la clave que arma SettingsPage con el alta en curso (Device ID, o IP:puerto). */
const discoveryKey =(c: DiscoveredCamera) => c.tuyaDeviceId ?? `${c.ip}:${c.port}`

function address(c: DiscoveredCamera) {
  if (c.kind === 'Tuya') return c.tuyaDeviceId
  return c.kind === 'Onvif' && c.port !== 80 ? `${c.ip}:${c.port}` : c.ip
}

/** Resultado de "Buscar en la red" (solo administradores): cada hallazgo precarga el alta. */
export function DiscoveryPanel({ pending, result, error, added, onSearch, onAdd, onClose }: Props) {
  return (
    <div className="discovery" aria-live="polite" aria-busy={pending}>
      <div className="discovery-head">
        <div>
          <strong>Buscar cámaras en la red</strong>
          <span className="row-hint">
            {pending
              ? 'Revisando la red local y Tuya… puede tardar unos 20 s.'
              : result
                ? `${result.subnets.length > 0 ? `Revisado: ${result.subnets.join(', ')}` : 'Sin subredes de la LAN'} · ${(result.elapsedMs / 1000).toFixed(0)} s`
                : 'Busca cámaras iCSee y ONVIF en la red local y dispositivos Tuya con cámara que aún no estén agregados.'}
          </span>
          <span className="row-hint">Las iCSee a batería solo aparecen despiertas: actívalas (movimiento o timbre) antes de buscar.</span>
        </div>
        <div className="discovery-actions">
          <button type="button" className="btn btn-solid" onClick={onSearch} disabled={pending}>
            {pending ? 'Buscando…' : result || error ? 'Buscar de nuevo' : 'Buscar'}
          </button>
          <button type="button" className="btn btn-ghost" onClick={onClose} aria-label="Cerrar búsqueda">
            ✕
          </button>
        </div>
      </div>

      {error && <p className="error">{error}</p>}
      {result?.warnings.map((w) => (
        <p key={w} className="discovery-warning">
          {w}
        </p>
      ))}

      {result && !pending && (
        result.found.length === 0 ? (
          <p className="muted discovery-empty">No se encontraron cámaras nuevas.</p>
        ) : (
          <ul className="discovery-list">
            {result.found.map((c) => {
              const key = discoveryKey(c)
              const done = added.has(key)
              return (
                <li key={key} className="discovery-row">
                  <span className="discovery-kind">{kindLabels[c.kind]}</span>
                  <span className="discovery-text">
                    <strong>{c.suggestedName}</strong>
                    <span>
                      <span className="mono">{address(c)}</span>
                      {c.model && ` · ${c.model}`}
                      {c.detail && ` · ${c.detail}`}
                    </span>
                  </span>
                  <button type="button" className="btn btn-solid" onClick={() => onAdd(c)} disabled={done}>
                    {done ? 'En el alta' : 'Agregar'}
                  </button>
                </li>
              )
            })}
          </ul>
        )
      )}
    </div>
  )
}
