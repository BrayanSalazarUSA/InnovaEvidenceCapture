@echo off
setlocal enableextensions
title Innova Evidence Capture - Instalacion

echo.
echo ============================================
echo   Innova Evidence Capture - Instalacion
echo ============================================
echo.

net session >nul 2>&1
if %errorlevel% neq 0 (
  echo  [!] Este instalador necesita permisos de administrador.
  echo.
  echo      Cierra esta ventana, haz clic derecho sobre instalar.bat
  echo      y elige "Ejecutar como administrador".
  echo.
  pause
  exit /b 1
)

set "SRC=%~dp0"
set "DEST=%ProgramFiles%\Innova Evidence Capture"
set "EVID=C:\InnovaEvidence"
set "STARTUP=%ProgramData%\Microsoft\Windows\Start Menu\Programs\StartUp"
set "STARTMENU=%ProgramData%\Microsoft\Windows\Start Menu\Programs"

if not exist "%SRC%InnovaEvidenceCapture.exe" (
  echo  [!] No encuentro InnovaEvidenceCapture.exe junto a este archivo.
  echo      Descomprime el ZIP completo antes de instalar.
  echo.
  pause
  exit /b 1
)

echo  [1/5] Cerrando la version anterior si estaba abierta...
taskkill /IM InnovaEvidenceCapture.exe /F >nul 2>&1
timeout /t 2 /nobreak >nul

echo  [2/5] Copiando el programa a:
echo        %DEST%
if not exist "%DEST%" mkdir "%DEST%"
copy /Y "%SRC%InnovaEvidenceCapture.exe" "%DEST%\" >nul
if %errorlevel% neq 0 goto :copyfail

rem ffmpeg es lo que graba el video. Sin el, la captura de imagen funciona
rem igual pero el boton de grabar avisa que no esta disponible.
if exist "%SRC%ffmpeg.exe" (
  copy /Y "%SRC%ffmpeg.exe" "%DEST%\" >nul
  echo        ffmpeg instalado, grabacion de video habilitada.
) else (
  echo        ATENCION: no se encontro ffmpeg.exe, no se podra grabar video.
)

rem La configuracion existente NO se pisa: cada PC puede tener la suya.
if not exist "%DEST%\appsettings.json" (
  copy /Y "%SRC%appsettings.json" "%DEST%\" >nul
  echo        appsettings.json instalado.
) else (
  echo        appsettings.json ya existia, se conserva.
)

echo  [3/5] Preparando la carpeta de evidencias:
echo        %EVID%
if not exist "%EVID%" mkdir "%EVID%"
rem S-1-5-32-545 = grupo Usuarios, funciona en Windows en cualquier idioma.
icacls "%EVID%" /grant *S-1-5-32-545:(OI)(CI)M /T >nul 2>&1

echo  [4/5] Configurando el arranque automatico...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$w = New-Object -ComObject WScript.Shell;" ^
  "$s = $w.CreateShortcut('%STARTUP%\Innova Evidence Capture.lnk');" ^
  "$s.TargetPath = '%DEST%\InnovaEvidenceCapture.exe';" ^
  "$s.WorkingDirectory = '%DEST%';" ^
  "$s.Description = 'Captura de evidencia para monitoreo';" ^
  "$s.Save();" ^
  "$m = $w.CreateShortcut('%STARTMENU%\Innova Evidence Capture.lnk');" ^
  "$m.TargetPath = '%DEST%\InnovaEvidenceCapture.exe';" ^
  "$m.WorkingDirectory = '%DEST%';" ^
  "$m.Save();"

echo  [5/5] Iniciando el programa...
start "" "%DEST%\InnovaEvidenceCapture.exe"

echo.
echo  ============================================
echo   Instalacion terminada.
echo  ============================================
echo.
echo   - El icono aparece junto al reloj. Un clic abre el menu.
echo   - Ctrl + Shift + I  capturar (elige foto o video al soltar)
echo   - Ctrl + Shift + V  ir directo a grabar video
echo   - Las evidencias quedan en: %EVID%\captures
echo   - Arranca solo cada vez que se inicia sesion.
echo.
pause
exit /b 0

:copyfail
echo.
echo  [!] No se pudo copiar el programa. Cierra InnovaEvidenceCapture
echo      si sigue abierto e intenta de nuevo.
echo.
pause
exit /b 1
