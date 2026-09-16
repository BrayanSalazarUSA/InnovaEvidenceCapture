# Innova Evidence Capture — Guía de instalación

Programa para los PCs de monitoreo. El agente presiona un atajo, selecciona una
región de la pantalla y esa evidencia queda disponible en la app móvil en alta
calidad.

---

## ¿Hay que instalar algo más? No

**No se necesita .NET, ni Java, ni Visual C++, ni ninguna librería adicional.**

El ejecutable es *autocontenido*: trae dentro todo lo que necesita. Por eso pesa
unos 150 MB, y por eso se instala igual en los 30 PCs sin preparar nada antes.

Requisitos reales:

| Requisito | Detalle |
|---|---|
| Sistema | Windows 10 (64 bits) o Windows 11 |
| Permisos | Administrador **solo para instalar**. Después corre como usuario normal |
| Disco | ~200 MB para el programa + lo que ocupen las evidencias |
| Red | Salida HTTPS hacia el backend |

---

## Los archivos

El paquete `InnovaEvidenceCapture-win-x64.zip` trae cuatro archivos:

| Archivo | Para qué es |
|---|---|
| `InnovaEvidenceCapture.exe` | El programa. Es todo: no hay DLLs sueltas |
| `appsettings.json` | La configuración (a qué backend apunta, atajo, retención) |
| `instalar.bat` | **Este es el que se ejecuta** |
| `desinstalar.bat` | Para quitarlo |

---

## Instalación, paso a paso

1. Copia el ZIP al PC de monitoreo (USB, red o descarga directa).
2. **Descomprímelo completo.** No ejecutes nada desde dentro del ZIP.
3. Clic derecho sobre **`instalar.bat`** → **Ejecutar como administrador**.
4. Espera a que diga "Instalación terminada" y presiona una tecla.

Eso es todo. El instalador:

- Copia el programa a `C:\Program Files\Innova Evidence Capture`
- Crea `C:\InnovaEvidence` y le da permiso de escritura a los usuarios normales
- Lo deja arrancando solo cada vez que alguien inicia sesión
- Lo abre de una vez

**Al reinstalar no se pisa `appsettings.json`**, así que si un PC tenía configuración propia, se conserva.

---

## Verificar que quedó bien

1. Busca el ícono dorado junto al reloj, abajo a la derecha.
2. Presiona **`Ctrl + Shift + I`**. La pantalla debe oscurecerse.
3. Arrastra un rectángulo sobre las cámaras y suelta.
4. Confirma con **Guardar y subir**.
5. Doble clic en el ícono de la bandeja → **Mis capturas de hoy**.

La captura debe aparecer con un **punto verde** (subida). Si queda en **ámbar**,
no llegó al servidor: revisa `backendUrl` y la conexión. El punto se pone
**azul** cuando un agente ya la usó en un reporte.

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
  "hotkeyModifiers": "Ctrl+Shift",
  "hotkeyKey": "I"
}
```

| Campo | Qué hace |
|---|---|
| `backendUrl` | El servidor. **Sin** `/api` al final |
| `stationCode` | Déjalo vacío: usa el nombre del PC (`INNMONITOR20`) automáticamente |
| `stationName` | Nombre legible, por ejemplo `Mesa 1`. Vacío = igual al código |
| `retentionHours` | Horas que las capturas se quedan en el PC **después de subirse** |
| `hotkeyKey` | La tecla del atajo, si `I` choca con otro programa |

Después de editarlo hay que cerrar el programa desde la bandeja y volver a abrirlo.

---

## Dónde queda cada cosa

```
C:\Program Files\Innova Evidence Capture\    el programa
C:\InnovaEvidence\captures\2026-09-16\       las capturas del día
C:\InnovaEvidence\logs\                      el registro diario
```

Junto a cada `.png` hay un `.json` con su estado. **No los borres:** son los que
saben si esa captura ya se subió o sigue pendiente.

---

## Cómo se comporta

**Si se cae el internet**, la captura igual se guarda en el PC y la subida se
reintenta sola cada 30 segundos, incluso después de reiniciar. Nunca se pierde
evidencia por un corte de red.

**La limpieza automática** borra del PC lo que tenga más de 48 horas **y ya esté
subido**. Lo que no llegó al servidor no se borra nunca.

**No se puede eliminar evidencia ya subida** desde el PC, a propósito: es cadena
de custodia. El momento de descartar es antes de subir, en la ventana de vista
previa. Para dar de baja una evidencia ya subida, lo hace un supervisor desde el
dashboard, quedando registro de quién y por qué.

---

## Si algo falla

| Síntoma | Qué hacer |
|---|---|
| El atajo no responde | Otro programa usa `Ctrl+Shift+I`. Cambia `hotkeyKey` en `appsettings.json` |
| El video sale negro en la captura | El cliente de cámaras usa aceleración por hardware. Avísame: se cambia el método de captura |
| Todo queda en ámbar | No llega al backend. Revisa `backendUrl` y que el PC tenga salida a internet |
| No arranca solo | Revisa que exista el acceso directo en `C:\ProgramData\Microsoft\Windows\Start Menu\Programs\StartUp` |
| El antivirus lo bloquea | Pide a IT que agregue a la lista blanca `C:\Program Files\Innova Evidence Capture\InnovaEvidenceCapture.exe` |

**Para soporte, manda siempre el log:** `C:\InnovaEvidence\logs\evidence-capture-AAAA-MM-DD.log`.
También se llega desde el programa: bandeja → Mis capturas de hoy → **Ver registro**.

---

## Desinstalar

Clic derecho sobre `desinstalar.bat` → Ejecutar como administrador.

Quita el programa y el arranque automático. **No borra las evidencias** de
`C:\InnovaEvidence`; si ya se subieron todas, esa carpeta se puede borrar a mano.

---

## Nota sobre el antivirus

El programa registra un atajo global y captura la pantalla. Son exactamente las
dos cosas que un antivirus mira con lupa. En los PCs de prueba normalmente no
pasa nada, pero **antes de desplegar a los 30 conviene pedirle a IT que lo
agregue a la lista blanca**, para no descubrirlo PC por PC.

A futuro, firmar el ejecutable con un certificado de código elimina el problema
de raíz.
