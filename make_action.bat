@echo off
dotnet restore LenovoLegionToolkit.sln || exit /b

SET VERSION=0.0.1
IF NOT "%1"=="" IF /I NOT "%1"=="raw" SET VERSION=%1

SET PATH=%PATH%;"C:\Program Files (x86)\Inno Setup 6"

SET USE_MANIFEST=
FOR %%A IN (%*) DO IF "%%A"=="raw" SET USE_MANIFEST=-UseManifest

dotnet publish "LenovoLegionToolkit.WPF\LenovoLegionToolkit.WPF.csproj" -c release -o build /p:DebugType=None /p:FileVersion=%VERSION% /p:Version=%VERSION% || exit /b
dotnet publish "LenovoLegionToolkit.SpectrumTester\LenovoLegionToolkit.SpectrumTester.csproj" -c release -o build /p:DebugType=None /p:FileVersion=%VERSION% /p:Version=%VERSION% || exit /b
dotnet publish "LenovoLegionToolkit.Probe\LenovoLegionToolkit.Probe.csproj" -c release -o build /p:DebugType=None /p:FileVersion=%VERSION% /p:Version=%VERSION% || exit /b
dotnet publish "LenovoLegionToolkit.CLI\LenovoLegionToolkit.CLI.csproj" -c release -o build /p:DebugType=None /p:FileVersion=%VERSION% /p:Version=%VERSION% || exit /b

echo Copying packaging files...
powershell -NoProfile -ExecutionPolicy Bypass -File ".\build_identity_package.ps1" -Version %VERSION% -OutputDir "build" %USE_MANIFEST% || exit /b

iscc make_installer_action.iss /DMyAppVersion=%VERSION% || exit /b

echo Stamping installer executable...
powershell -NoProfile -ExecutionPolicy Bypass -File ".\sign_installer.ps1" || exit /b
