@echo off
:: === Убедимся что Discord закрыт перед патчингом ===
taskkill /IM Discord.exe /F >nul 2>&1
timeout /t 1 /nobreak >nul

:: === Патчинг Vencord ===
"W:\Programs\Vencord\VencordInstallerCli.exe" -install -location "C:\Users\Woowz11\AppData\Local\Discord"
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