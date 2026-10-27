@echo off
taskkill /IM Discord.exe /F >nul 2>&1
timeout /t 1 /nobreak >nul

cd /d "W:\Other\rofl-games\Vencord"
pnpm build
pnpm inject -- --location "C:\Users\Woowz11\AppData\Local\Discord"