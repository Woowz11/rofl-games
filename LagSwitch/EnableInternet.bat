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

:: Удаляем правила брандмауэра LagSwitch (блокировка трафика)
netsh advfirewall firewall delete rule name="LagSwitch Block In"  >nul 2>&1
netsh advfirewall firewall delete rule name="LagSwitch Block Out" >nul 2>&1
echo [OK]     правила LagSwitch удалены (если были)
echo.

:: Адаптер на случай, если его отключила старая версия
call :Enable "Ethernet"

:: Адаптеры из файла старой версии
if exist "disabled_adapters.txt" (
    echo.
    echo Найден disabled_adapters.txt, включаю записанные адаптеры...
    for /f "usebackq delims=" %%N in ("disabled_adapters.txt") do call :Enable "%%N"
    del "disabled_adapters.txt"
)

echo.
echo Готово. Если интернет не появился, подождите 20-30 секунд или переподключите кабель.
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