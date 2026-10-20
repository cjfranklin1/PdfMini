@echo off
setlocal
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
cd /d "%~dp0"
"%CSC%" /nologo /target:exe /platform:x64 /out:dist\EngineTest.exe ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll ^
  src\PdfApi.cs src\Session.cs test\EngineTest.cs
if errorlevel 1 (
  echo TEST BUILD FAILED
  exit /b 1
)
copy /y vendor\bin\pdfium.dll dist\pdfium.dll >nul
echo TEST BUILD OK
