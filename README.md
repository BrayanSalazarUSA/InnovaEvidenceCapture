# Innova Evidence Capture

Captura de evidencia en alta calidad desde los PCs de monitoreo de Innova.

El agente ve un evento en iVMS, SmartPSS o Axis, presiona `Ctrl + Shift + I`,
selecciona la región de la pantalla y confirma. La evidencia se guarda en el PC,
se sube al backend y queda disponible en la app móvil para adjuntarla a un
pending report — sin fotos de celular a los monitores.

## Para instalar en un PC de monitoreo

Ver **[GUIA-INSTALACION.md](GUIA-INSTALACION.md)**. Resumen: descomprimir el ZIP
y ejecutar `instalar.bat` como administrador. No hace falta instalar .NET ni
ninguna otra librería.

## Estado

Fase 1 completa: imagen. El video llega en la Fase 2.

- Atajo global, overlay de selección de región y vista previa para confirmar
- Guardado local organizado por fecha, con el estado de cada captura
- Cola de subida con reintentos: un corte de internet no pierde evidencia
- Ventana "Mis capturas de hoy" con el estado de cada una
- Retención automática de 48 h que solo borra lo ya subido
- Log diario en disco para soporte

## Compilar

El `.exe` lo produce GitHub Actions en cada push a `main`: pestaña **Actions** →
artifact `InnovaEvidenceCapture-win-x64`. No hace falta tener Windows.

Para compilarlo a mano, en Windows:

```bash
dotnet publish src/InnovaEvidenceCapture/InnovaEvidenceCapture.csproj \
  -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

Desde macOS o Linux se puede verificar que compila (no ejecutar):

```bash
dotnet build src/InnovaEvidenceCapture/InnovaEvidenceCapture.csproj
```

## Probar el backend sin el .exe

```bash
./test/sembrar-evidencias.sh https://innova-dashboard.com
```

Sube varias evidencias de prueba simulando tres PCs distintos.

## Estructura

```
src/InnovaEvidenceCapture/
  Capture/     captura de pantalla (GDI hoy, DXGI si hace falta)
  Models/      CaptureRecord: una captura y su estado
  Services/    configuración, atajo, almacenamiento, subida, log
  UI/          overlay de selección, vista previa, galería del día
installer/     instalar.bat y desinstalar.bat
test/          script para sembrar evidencias de prueba
```
