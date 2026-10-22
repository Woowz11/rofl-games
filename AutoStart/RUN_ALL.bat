@echo off
:: === Запуск Zapret с правами администратора (только этот процесс) ===
powershell -Command "Start-Process -FilePath '\"W:\Programs\Zapret\general (ALT).bat\"' -WorkingDirectory 'W:\Programs\Zapret' -Verb RunAs"

timeout /t 3 /nobreak >nul

:: === Запуск Discord (обычные права) ===
start "" "C:\Users\Woowz11\AppData\Local\Discord\Discord.exe"
timeout /t 3 /nobreak >nul

:: === Запуск OBS (обычные права, с рабочей директорией) ===
start "" /d "W:\Programs\OBS\bin\64bit" "obs64.exe"
timeout /t 3 /nobreak >nul

:: === Запуск Firefox (обычные права) ===
start "" "C:\Program Files\Mozilla Firefox\firefox.exe"

exit