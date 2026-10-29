@echo off
setlocal
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
cd /d "%~dp0"
"%CSC%" /nologo /target:exe /platform:x64 /out:dist\NupE2E.exe ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  src\PdfApi.cs src\Session.cs src\PageView.cs src\MainForm.cs src\AboutForm.cs ^
  src\Outline.cs src\NupPrint.cs src\AssemblyInfo.cs test\NupE2E.cs
if errorlevel 1 (
  echo E2E BUILD FAILED
  exit /b 1
)
copy /y vendor\bin\pdfium.dll dist\pdfium.dll >nul
echo E2E BUILD OK
