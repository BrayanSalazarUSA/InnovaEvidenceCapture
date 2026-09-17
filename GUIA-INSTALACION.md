# Innova Evidence Capture — Guía de instalación

Programa para los PCs de monitoreo. El agente presiona un atajo, selecciona una
región de la pantalla, y esa evidencia — foto o video — queda disponible en la
app móvil en alta calidad.

---

## ¿Hay que instalar algo más? No

**No se necesita .NET, ni Java, ni códecs, ni ninguna librería adicional.**

El ejecutable es *autocontenido* y el paquete trae todo lo que hace falta,
incluido `ffmpeg.exe` para grabar video. Por eso el ZIP pesa bastante.

| Requisito | Detalle |
|---|---|
| Sistema | Windows 10 (64 bits) o Windows 11 |
| Permisos | Administrador **solo para instalar**. Después corre como usuario normal |
| Disco | ~300 MB para el programa + lo que ocupen las evidencias |
| Red | Salida HTTPS hacia el backend |

---

## Los archivos del paquete

| Archivo | Para qué es |
|---|---|
| `InnovaEvidenceCapture.exe` | El programa |
| `ffmpeg.exe` | Motor de grabación de video. Sin él las fotos funcionan igual, pero no se puede grabar |
| `appsettings.json` | Configuración: backend, atajos, retención, límite de video |
| `instalar.bat` | **Este es el que se ejecuta** |
| `desinstalar.bat` | Para quitarlo |

---

## Instalación, paso a paso

1. Copia el ZIP al PC de monitoreo.
2. **Descomprímelo completo.** No ejecutes nada desde dentro del ZIP.
3. Clic derecho sobre **`instalar.bat`** → **Ejecutar como administrador**.
4. Espera a que diga "Instalación terminada".

Windows va a mostrar el aviso azul de SmartScreen porque el ejecutable no está
firmado: **Más información** → **Ejecutar de todas formas**.

El instalador copia el programa a `C:\Program Files\Innova Evidence Capture`,
crea `C:\InnovaEvidence` con permiso de escritura para usuarios normales, y lo
deja arrancando solo en cada inicio de sesión. **Al reinstalar no se pisa
`appsettings.json`**, así que la configuración de cada PC se conserva.

---

## Cómo se usa

**Un clic** en el ícono junto al reloj abre el menú.

| Acción | Cómo |
|---|---|
| Capturar | `F8`, arrastrar la región, y al soltar elegir **Foto** o **Grabar** |
| Grabar directo | `F9` — se salta la elección y empieza a grabar al soltar |
| Ver lo capturado | Menú del ícono → **Mis capturas de hoy** |

Atajos dentro del overlay: `Enter` toma la foto, `V` graba, `Esc` cancela.

### Señalar en la foto

Antes de confirmar, en la vista previa hay tres herramientas: **flecha**,
**recuadro** y **círculo**, para marcar una persona o una zona. Con **Deshacer**
y **Limpiar**. Las marcas se queman en la imagen al guardar, así que el
investigador ve exactamente lo que el agente quiso señalar.

Si se descarta la captura, la imagen nunca se modifica.

### Grabar video

Al elegir **Grabar** aparece una barra arriba con el cronómetro y el botón
**Detener y guardar**. Corta sola al llegar al límite (60 segundos por defecto).
La barra no roba el foco, así que se puede seguir moviendo iVMS mientras graba.

El video se produce en H.264 listo para reproducir, así que **no pasa por ningún
proceso de conversión** — a diferencia de los videos exportados del playback de
iVMS. Llega al celular al instante.

### Estados

En "Mis capturas de hoy", cada evidencia tiene un punto de color:

- **Ámbar** — todavía no llegó al servidor
- **Verde** — subida, ya aparece en la app móvil
- **Azul** — un agente la usó en un reporte

---

## Configuración

`C:\Program Files\Innova Evidence Capture\appsettings.json`

```json
{
  "backendUrl": "https://innova-dashboard.com",
  "stationCode": "",
  "stationName": "",
  "captureRoot": "C:\\InnovaEvidence",
  "retentionHours": 48,
  "uploadEnabled": true,
  "hotkeyModifiers": "",
  "hotkeyKey": "F8",
  "hotkeyVideoKey": "F9",
  "videoMaxSeconds": 60,
  "videoFps": 15
}
```

| Campo | Qué hace |
|---|---|
| `backendUrl` | El servidor. **Sin** `/api` al final |
| `stationCode` | Vacío = usa el nombre del PC. **Ojo: Windows lo corta a 15 caracteres** |
| `stationName` | Nombre legible, por ejemplo `Mesa 1`. Es lo que ve el agente en el celular |
| `retentionHours` | Horas que las capturas se quedan en el PC **después de subirse** |
| `hotkeyKey` / `hotkeyVideoKey` | Las teclas de los atajos, si chocan con otro programa |

### Cambiar los ajustes desde el programa

Clic en el icono junto al reloj > **Ajustes...**. Desde ahi se cambia el codigo
de la estacion y los dos atajos, sin tocar ningun archivo. Para elegir el atajo
se hace clic en el recuadro y se presiona la tecla: el programa la reconoce
sola.

Lo que se cambia ahi se guarda en `%ProgramData%\Innova Evidence Capture\config.json`,
no en Program Files, porque el agente no tiene permiso de escritura ahi. El
`appsettings.json` que reparte el instalador queda como los valores de fabrica.

Los atajos se vuelven a registrar al guardar, sin reiniciar el programa.

### Por que F8 y F9

En una sala de monitoreo buscar `Ctrl+Shift+I` a oscuras cuesta segundos que en
un seguimiento policial no hay. Por eso el atajo es **una sola tecla**.

Un atajo global se traga la tecla en **todo el sistema**: la aplicacion que
tenga el foco deja de recibirla. Por eso quedaron descartadas:

| Tecla | Por que no |
|---|---|
| `F1` | Ayuda, en casi todo |
| `F5` | Refrescar: el agente dejaria de poder refrescar el navegador y el cliente de camaras |
| `F11` | Pantalla completa |
| `F12` | Herramientas de desarrollo del navegador |
| `Impr Pant` | Buena candidata, pero no esta en todos los teclados y en Windows 11 se la queda la herramienta de recortes |

`F8` y `F9` estan en todos los teclados, se encuentran sin mirar y no los usa
nada de lo que el agente tiene abierto.

Se pueden cambiar desde **Ajustes...**, o en el archivo. Nombres admitidos:
`F1` a `F12`, `PrintScreen`, `Pause`, `ScrollLock`, `Insert`, `Home`, `End`,
`PageUp`, `PageDown`, o una letra o numero sueltos.

Si otro programa ya usa la tecla, al arrancar aparece "Atajo no disponible" y
basta con elegir otra en Ajustes.
| `videoMaxSeconds` | Corte automático de la grabación |
| `videoFps` | 15 va bien para cámaras. Subirlo engorda el archivo sin ganar mucho |

Después de editarlo, cerrar el programa desde el menú del ícono y volver a abrirlo.

---

## Dónde queda cada cosa

```
C:\Program Files\Innova Evidence Capture\        el programa
C:\InnovaEvidence\captures\INNMONITOR20\2026-09-16\   las capturas, por PC y fecha
C:\InnovaEvidence\logs\                          el registro diario
C:\InnovaEvidence\temp\                          grabaciones en curso
```

La carpeta lleva el nombre del PC aunque cada máquina solo guarde lo suyo: si
alguien copia archivos de un PC a otro, el origen queda claro sin abrir nada.

Junto a cada archivo hay un `.json` **oculto** con su estado. No se ve en el
Explorador, pero es lo que sabe si esa captura ya subió. Si se borra, esa
evidencia vuelve a aparecer como pendiente.

---

## Cómo se comporta

**Si se cae el internet**, la captura igual se guarda en el PC y la subida se
reintenta sola cada 30 segundos, incluso después de reiniciar. Nunca se pierde
evidencia por un corte de red.

**La limpieza automática** borra del PC lo que tenga más de 48 horas **y ya esté
subido**. Lo que no llegó al servidor no se borra nunca.

**No se puede eliminar evidencia ya subida** desde el PC, a propósito: es cadena
de custodia. El momento de descartar es antes de subir, en la vista previa. Para
dar de baja algo ya subido, lo hace un supervisor desde el dashboard, quedando
registro de quién y por qué.

---

## Si algo falla

| Síntoma | Qué hacer |
|---|---|
| El atajo no responde | Otro programa lo usa. Cambia `hotkeyKey` en `appsettings.json` |
| "Grabación no disponible" | Falta `ffmpeg.exe` en la carpeta del programa. Reinstala el paquete completo |
| El video sale negro | El cliente de cámaras usa aceleración por hardware. Avísame: se cambia el método de captura |
| Todo queda en ámbar | No llega al backend. Revisa `backendUrl` y la salida a internet del PC |
| No arranca solo | Revisa el acceso directo en `C:\ProgramData\Microsoft\Windows\Start Menu\Programs\StartUp` |
| El antivirus lo bloquea | Pide a IT la lista blanca de `InnovaEvidenceCapture.exe` y `ffmpeg.exe` |

**Para soporte, manda siempre el log:**
`C:\InnovaEvidence\logs\evidence-capture-AAAA-MM-DD.log`.
También se llega desde el programa: menú del ícono → Mis capturas de hoy → **Ver registro**.

---

## Desinstalar

Clic derecho sobre `desinstalar.bat` → Ejecutar como administrador.

Quita el programa y el arranque automático. **No borra las evidencias** de
`C:\InnovaEvidence`.

---

## Notas

**Antivirus.** El programa registra atajos globales y captura pantalla, que es
justo lo que un antivirus mira con lupa. Antes de desplegar a los 30 PCs conviene
pedirle a IT que agregue ambos ejecutables a la lista blanca, para no descubrirlo
máquina por máquina. Firmar el binario con un certificado de código elimina el
problema de raíz y quita también el aviso de SmartScreen.

**ffmpeg.** Se distribuye la compilación GPL, necesaria para el codificador H.264
(libx264). Se ejecuta como programa aparte, no va enlazado dentro de la
aplicación. Para uso interno de Innova no hay problema; si alguna vez se
distribuye el programa fuera de la empresa, conviene revisar las condiciones de
la licencia.
