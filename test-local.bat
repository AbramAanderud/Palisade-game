@echo off
REM Launches the local relay + two game windows (Alice and Bob) for multiplayer testing.
REM
REM Prerequisites:
REM   1. Node.js installed (so "node" works)
REM   2. `cd server && npm install` has been run once
REM   3. The game has been exported to build\windows\Palisade.exe via Godot
REM
REM Each window writes to its own account folder under user://accounts/,
REM so their mazes/gold never collide.

setlocal

set EXE=build\windows\Palisade.exe
if not exist "%EXE%" (
  echo.
  echo  [!] %EXE% not found.
  echo      Open Godot and use Project ^> Export to build a Windows binary into build\windows\.
  echo.
  pause
  exit /b 1
)

echo Starting relay server on ws://localhost:3000 ...
start "Palisade Relay" cmd /k "cd server && node relay.js"

REM Give the relay a beat to bind the port before clients try to connect.
timeout /t 2 /nobreak >nul

echo Launching Alice ...
start "Palisade - Alice" "%EXE%" --account alice --local

timeout /t 1 /nobreak >nul

echo Launching Bob ...
start "Palisade - Bob"   "%EXE%" --account bob   --local

echo.
echo  Three windows opened: relay, Alice, Bob.
echo  Close them all when finished testing.
echo.
endlocal
