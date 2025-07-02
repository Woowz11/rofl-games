@echo off
call "W:\Programs\MVS 2022\VC\Auxiliary\Build\vcvars64.bat"

echo [Компиляция Java...]
javac Script.java
if errorlevel 1 (
  echo [Ошибка компиляции Java]
  pause
  exit /b
)

echo [Создание Native Image с поддержкой Swing...]
native-image ^
  --enable-preview ^
  --initialize-at-build-time=java.awt.Toolkit ^
  --initialize-at-build-time=java.awt.GraphicsEnvironment ^
  --initialize-at-build-time=sun.awt ^
  --initialize-at-build-time=sun.java2d ^
  --initialize-at-run-time=java.awt.Desktop ^
  --initialize-at-run-time=sun.awt.windows ^
  -H:+AddAllCharsets ^
  -H:+ReportExceptionStackTraces ^
  -H:EnableURLProtocols=http,https ^
  --add-modules java.desktop ^
  --no-fallback ^
  Script

if errorlevel 1 (
  echo [Ошибка компиляции Native Image]
  echo.
  echo Попробуйте альтернативные флаги...
  
  echo [Попытка 2: базовая поддержка AWT...]
  native-image ^
    --initialize-at-build-time ^
    --initialize-at-run-time=java.awt.Toolkit ^
    --initialize-at-run-time=sun.awt ^
    -H:+AddAllCharsets ^
    -H:+ReportExceptionStackTraces ^
    --add-modules java.desktop ^
    --no-fallback ^
    Script
    
  if errorlevel 1 (
    echo [Все попытки неудачны]
    echo.
    echo GraalVM Native Image имеет ограниченную поддержку Swing/AWT.
    echo Рекомендуется использовать обычный jar файл для Swing приложений.
    echo.
    echo Создаю jar файл вместо native image...
    
    echo Main-Class: Script > manifest.txt
    jar cfm script.jar manifest.txt *.class
    del manifest.txt
    
    if exist script.jar (
      echo [Успех: создан script.jar]
      echo Запуск: java -jar script.jar
    ) else (
      echo [Ошибка создания jar]
    )
    pause
    exit /b
  )
)

if exist script.exe (
  echo [Успех: создан script.exe с поддержкой Swing]
) else (
  echo [Файл script.exe не найден]
)

pause