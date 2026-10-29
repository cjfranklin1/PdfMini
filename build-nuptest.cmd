@echo off
setlocal
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
cd /d "%~dp0"
"%CSC%" /nologo /target:exe /platform:x64 /out:dist\NupTest.exe ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  src\NupPrint.cs test\NupTest.cs
if errorlevel 1 (
  echo TEST BUILD FAILED
  exit /b 1
)
echo NUPTEST BUILD OK
