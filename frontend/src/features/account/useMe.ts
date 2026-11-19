import { useQuery } from '@tanstack/react-query'
import { api } from '../../api/client'

/** Usuario de la sesión: no cambia sin volver a iniciar sesión (los roles vienen en el token). */
export function useMe() {
  return useQuery({ queryKey: ['me'], queryFn: api.me, staleTime: Infinity })
}

/**
 * ¿Puede ver Estado y Configuración, y borrar clips? Falso para el rol de solo lectura (camguard-viewer) y mientras
 * no se sabe quién es, para no pedir lo que la API le negaría. La protección real está en la API.
 */
export function useCanManage() {
  const me = useMe()
  return me.data ? !me.data.isReadOnly : false
}
