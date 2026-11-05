@echo off
chcp 65001 >nul
setlocal

:: Самоповышение прав: netsh требует администратора
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo Запрашиваю права администратора...
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

cd /d "%~dp0"

echo ==============================================
echo  Восстановление интернета
echo ==============================================
echo.

:: Адаптеры, которые LagSwitch может отключить (должны совпадать с AdapterNames в Bootstrap.cs)
call :Enable "Ethernet"

:: Адаптеры, которые LagSwitch записал перед отключением (если приложение упало)
if exist "disabled_adapters.txt" (
    echo.
    echo Найден disabled_adapters.txt, включаю записанные адаптеры...
    for /f "usebackq delims=" %%N in ("disabled_adapters.txt") do call :Enable "%%N"
    del "disabled_adapters.txt"
)

echo.
echo Готово. Если интернет не появился, проверьте список адаптеров ниже:
echo.
netsh interface show interface
echo.
pause
exit /b

:: ----------------------------------------------------------------------

:Enable
netsh interface set interface name=%1 admin=enabled >nul 2>&1
if %errorlevel% equ 0 (
    echo [OK]     %~1
) else (
    echo [ОШИБКА] %~1 - адаптер не найден или не включился
)
exit /b