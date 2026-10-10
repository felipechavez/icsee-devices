import { NavLink, Outlet } from 'react-router-dom'
import { useAuth } from '../hooks/useAuth'
import { useHubStatus } from '../hooks/useEventsHub'
import { useNow } from '../hooks/useNow'
import { useTheme } from '../hooks/useTheme'
import type { Theme } from '../hooks/useTheme'
import { formatClock } from '../lib/format'
import { Segmented } from './Segmented'

const themes: { value: Theme; label: string }[] = [
  { value: 'dark', label: 'Oscuro' },
  { value: 'light', label: 'Claro' },
]

function Clock() {
  const now = useNow()
  return <span className="clock">{formatClock(now)}</span>
}

export function Layout() {
  const { username, logout } = useAuth()
  const hubStatus = useHubStatus()
  const [theme, setTheme] = useTheme()

  return (
    <div className="shell">
      <header className="topbar">
        <div className="brand">
          <span className="brand-mark" aria-hidden />
          CamGuard
        </div>
        <nav className="nav">
          <NavLink to="/" end>Inicio</NavLink>
          <NavLink to="/recordings">Grabaciones</NavLink>
          <NavLink to="/settings">Configuración</NavLink>
        </nav>
        <div className="topbar-end">
          {hubStatus !== 'connected' && (
            <span className="hub-warning" title="Sin conexión en tiempo real con el servidor">
              {hubStatus === 'disconnected' ? 'Sin conexión' : 'Reconectando…'}
            </span>
          )}
          <Segmented className="seg-sm" label="Tema" value={theme} options={themes} onChange={setTheme} />
          <Clock />
          <button className="btn" title={`Sesión de ${username}`} onClick={() => void logout()}>
            Salir
          </button>
        </div>
      </header>
      <main className="content">
        <Outlet />
      </main>
    </div>
  )
}
