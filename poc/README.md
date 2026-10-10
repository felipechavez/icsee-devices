# Fase 1: prueba de concepto

Antes de confiar en el sistema hay que medir cómo se comporta la cámara. Estos pasos
se hacen a mano, con go2rtc y FFmpeg instalados en tu PC (sin Docker).

## 0. Preparación

1. Descarga `go2rtc` desde https://github.com/AlexxIT/go2rtc/releases y FFmpeg
   (`winget install Gyan.FFmpeg` en Windows).
2. Crea `poc/go2rtc.poc.yaml` con las credenciales reales (no lo subas a git):

   ```yaml
   streams:
     icsee: dvrip://admin:CONTRASEÑA@192.168.1.86:34567?channel=0&subtype=0
   ```

3. Arranca: `go2rtc -config poc/go2rtc.poc.yaml`

## 1. Video en go2rtc

- [ ] Provoca movimiento frente a la cámara y abre http://localhost:1984. El stream
  `icsee` debe verse con el enlace *stream*.

## 2. Clip manual con FFmpeg

Con la cámara despierta:

```powershell
./poc/record-clip.ps1 -Seconds 20
```

- [ ] El archivo en `poc/clips/` se reproduce bien, con video y audio.

## 3. ¿La cámara se duerme con un cliente conectado?

Es el riesgo principal (batería). Ejecuta el monitor del puerto 34567 y, mientras
tanto, deja abierto el stream en http://localhost:1984:

```powershell
./poc/watch-port.ps1
```

- [ ] Anota cuánto tarda en dormirse **sin** clientes conectados.
- [ ] Anota si se duerme **con** el stream abierto, y cuánto tarda.
- Si no se duerme mientras hay un cliente, deja el tope por activación en 30 a 60 s
  (Configuración en la web) y no dejes la vista en vivo abierta.

## 4. Latencia movimiento → primer cuadro

1. Ejecuta `./poc/watch-port.ps1` y pasa frente a la cámara anotando la hora exacta
   (cronómetro del celular frente a la cámara funciona bien).
2. Graba un clip apenas despierte (`./poc/record-clip.ps1`).
3. Compara la hora del cronómetro en el primer cuadro con la de tu movimiento.

- [ ] Segundos perdidos al inicio: ____ (si son muchos, ver fase opcional "descargar desde la SD").

## Resultados

| Medición | Valor |
| --- | --- |
| Tiempo en dormirse sin clientes | |
| ¿Se duerme con cliente conectado? | |
| Tiempo en dormirse con cliente | |
| Segundos perdidos al inicio | |

## 5. Datos DVRIP (batería, SD, sensibilidad)

Las claves que entrega el firmware XM varían. Con la API en Development y la cámara
despierta, abre `GET /api/cameras/{id}/dvrip-probe` (con sesión iniciada) y revisa qué
consultas responden con `"Ret": 100`:

- [ ] Batería: ¿cuál de `Dev.ElectCapacity`, `Dev.PowerInfo`, `Status.Battery`, `Dev.BatteryInfo` responde, y con qué campo? ____
- [ ] Detección: ¿`Detect.PIRDetect`, `Detect.PirDetect` o `Detect.MotionDetect`? ¿Tiene `Level`? ____
- [ ] `StorageInfo`: ¿`TotalSpace`/`RemainSpace` en MB? ____

Si la clave real no está en la lista, agrégala a `DvripQueries` (`Features/Devices/DeviceInfoParser.cs`)
y una prueba con la respuesta capturada en `DeviceInfoParserTests`.

Resultado con la IPC_GK7201V300_LPG-G3-WQ (firmware V5.03.R02, 2025-03-25):

- Batería: **config** (1042) `Dev.ElectCapacity` → `{"percent": 100, "level": 1, "electable": 0, "extendPercent": -1, …}`.
  Como info (1020) responde `Ret 103`. `percent` va en minúsculas.
- Sin SD: `StorageInfo` responde con `TotalSpace` 0.
- Detección: `Detect.MotionDetect` existe (`Level` 3). `SystemFunction` declara `SupportPirSensitive`,
  pero aún no se encontró la clave de la sensibilidad PIR (`Detect.PirDetect` → `Ret 607`).
