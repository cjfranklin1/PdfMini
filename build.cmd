@echo off
setlocal
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
cd /d "%~dp0"
if not exist dist mkdir dist
"%CSC%" /nologo /target:winexe /platform:x64 /optimize+ /warnaserror- /out:dist\PdfMini.exe ^
  /win32manifest:app.manifest /win32icon:assets\app.ico ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  src\Program.cs src\PdfApi.cs src\Session.cs src\PageView.cs src\MainForm.cs src\AboutForm.cs src\NupPrint.cs src\Outline.cs src\AssemblyInfo.cs
if errorlevel 1 (
  echo BUILD FAILED
  exit /b 1
)
copy /y vendor\bin\pdfium.dll dist\pdfium.dll >nul
echo BUILD OK
