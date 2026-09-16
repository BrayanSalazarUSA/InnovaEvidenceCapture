# Innova Evidence Capture

Captura de evidencia en alta calidad desde los PCs de monitoreo.

## Estado
MVP para demo. Solo imagen (region de pantalla). Video en Fase 2.

## Compilar

No compila en macOS/Linux para *ejecutar*, pero si para *verificar*:

    dotnet build src/InnovaEvidenceCapture/InnovaEvidenceCapture.csproj

Para el .exe distribuible (solo Windows, o via GitHub Actions):

    dotnet publish src/InnovaEvidenceCapture/InnovaEvidenceCapture.csproj \
      -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish

GitHub Actions ya esta configurado: cada push a main deja el .exe como artifact.

## Uso

1. Ejecutar `InnovaEvidenceCapture.exe`. Queda un icono en la bandeja.
2. `Ctrl + Shift + I` -> se oscurece la pantalla, se arrastra el rectangulo.
3. Preview: **Guardar** o **Descartar**.
4. Se guarda en `C:\InnovaEvidence\captures\yyyy-MM-dd\` y se sube al backend.

## Configuracion

`appsettings.json` junto al ejecutable. `stationCode` vacio = nombre del PC.
