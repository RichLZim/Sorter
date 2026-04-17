@echo off
echo ============================================
echo  SORTER — Build Script
echo ============================================
echo.

where dotnet >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] .NET SDK not found. Please install .NET 8 SDK from:
    echo         https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
)

echo [1/3] Restoring packages...
cd Sorter
dotnet restore
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Package restore failed.
    pause
    exit /b 1
)

echo [2/3] Building Release...
dotnet build -c Release
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Build failed.
    pause
    exit /b 1
)

echo [3/3] Publishing self-contained .exe...
dotnet publish Sorter.csproj -c Release -r win-x64 --self-contained true -o ../Sorter/publish /p:PublishSingleFile=true
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Publish failed.
    pause
    exit /b 1
)

cd ..
echo.
echo ============================================
echo  SUCCESS! Sorter.exe is in: .\publish\
echo ============================================
echo.
pause
