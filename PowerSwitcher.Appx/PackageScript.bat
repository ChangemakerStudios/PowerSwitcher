@echo off
setlocal

rem Newer Windows SDKs keep their tools in versioned folders that aren't on PATH.
where MakeAppx >nul 2>nul
if errorlevel 1 (
    for /f "delims=" %%v in ('dir /b /ad /on "%ProgramFiles(x86)%\Windows Kits\10\bin\10.*" 2^>nul') do (
        if exist "%ProgramFiles(x86)%\Windows Kits\10\bin\%%v\x64\MakeAppx.exe" set "SDK_TOOLS=%ProgramFiles(x86)%\Windows Kits\10\bin\%%v\x64"
    )
)
if defined SDK_TOOLS set "PATH=%SDK_TOOLS%;%PATH%"

where MakeAppx >nul 2>nul
if errorlevel 1 (
    echo PackageScript: error: MakeAppx not found. Install the Windows SDK.
    exit /b 1
)

if not exist "..\Build" mkdir ..\Build
if not exist "..\Build\Appx" mkdir ..\Build\Appx

MakeAppx pack /o /f MappingFile.txt /p ..\Build\Appx\PowerSwitcher.appx || exit /b 1
copy /y ..\Build\Appx\PowerSwitcher.appx ..\Build\Appx\PowerSwitcher_ns.appx || exit /b 1
signtool.exe sign -f ..\Build\Certs\my.pfx -fd SHA256 -v ..\Build\Appx\PowerSwitcher.appx || exit /b 1
