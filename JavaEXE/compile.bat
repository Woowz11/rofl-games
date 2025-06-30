@echo off
call "W:\Programs\MVS 2022\VC\Auxiliary\Build\vcvars64.bat"

javac Script.java
if errorlevel 1 (
  echo [Ошибка компиляции Java]
  pause
  exit /b
)

native-image Script
if errorlevel 1 (
  echo [Ошибка компиляции Native Image]
  pause
  exit /b
)

echo [Успех: создан script.exe]
pause