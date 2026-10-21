@echo off
:: === Проверка прав администратора, автоповышение если их нет ===
net session >nul 2>&1
if %errorLevel% neq 0 (
    powershell -Command "Start-Process '%~f0' -Verb RunAs"
    exit /b
)

:: === Небольшая пауза, чтобы система "устаканилась" ===
timeout /t 5 /nobreak >nul

:: === Запуск Zapret (с явной рабочей директорией) ===
pushd "W:\Programs\Zapret"
start "" /d "W:\Programs\Zapret" "general (ALT).bat"
popd
timeout /t 3 /nobreak >nul

:: === Запуск Discord ===
start "" "C:\Users\Woowz11\AppData\Local\Discord\Discord.exe"
timeout /t 3 /nobreak >nul

:: === Запуск OBS (с рабочей директорией!) ===
start "" /d "W:\Programs\OBS\bin\64bit" "obs64.exe"
timeout /t 3 /nobreak >nul

:: === Запуск Firefox ===
start "" "C:\Program Files\Mozilla Firefox\firefox.exe"

exit