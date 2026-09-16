@echo off
setlocal enableextensions
title Innova Evidence Capture - Desinstalacion

echo.
echo ============================================
echo   Innova Evidence Capture - Desinstalacion
echo ============================================
echo.

net session >nul 2>&1
if %errorlevel% neq 0 (
  echo  [!] Ejecuta este archivo como administrador.
  pause
  exit /b 1
)

set "DEST=%ProgramFiles%\Innova Evidence Capture"
set "STARTUP=%ProgramData%\Microsoft\Windows\Start Menu\Programs\StartUp"
set "STARTMENU=%ProgramData%\Microsoft\Windows\Start Menu\Programs"

echo  [1/3] Cerrando el programa...
taskkill /IM InnovaEvidenceCapture.exe /F >nul 2>&1
timeout /t 2 /nobreak >nul

echo  [2/3] Quitando el arranque automatico...
del /F /Q "%STARTUP%\Innova Evidence Capture.lnk" >nul 2>&1
del /F /Q "%STARTMENU%\Innova Evidence Capture.lnk" >nul 2>&1

echo  [3/3] Borrando el programa...
rmdir /S /Q "%DEST%" >nul 2>&1

echo.
echo  Listo. El programa fue desinstalado.
echo.
echo  IMPORTANTE: las evidencias en C:\InnovaEvidence NO se borraron.
echo  Si ya se subieron todas al servidor, puedes borrar esa carpeta a mano.
echo.
pause
