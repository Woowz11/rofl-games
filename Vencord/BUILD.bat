@echo off
taskkill /IM Discord.exe /F
pnpm build
pnpm inject