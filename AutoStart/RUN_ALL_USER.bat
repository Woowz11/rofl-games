@echo off

:: === Патчинг Vencord (dev-сборка) ===
call "W:\Other\rofl-games\Vencord\BUILD.bat"
timeout /t 2 /nobreak >nul

:: === Запуск Discord ===
start "" "C:\Users\Woowz11\AppData\Local\Discord\Discord.exe"
timeout /t 3 /nobreak >nul

:: === Запуск OBS ===
start "" /d "W:\Programs\OBS\bin\64bit" "obs64.exe"
timeout /t 3 /nobreak >nul

:: === Запуск Firefox ===
start "" "C:\Program Files\Mozilla Firefox\firefox.exe"

exit