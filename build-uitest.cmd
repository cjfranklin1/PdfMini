@echo off
setlocal
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
cd /d "%~dp0"
"%CSC%" /nologo /target:exe /platform:x64 /main:PdfMini.UiTest /win32icon:assets\app.ico /out:dist\UiTest.exe ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  src\PdfApi.cs src\Session.cs src\PageView.cs src\MainForm.cs src\AboutForm.cs src\NupPrint.cs src\Outline.cs src\AssemblyInfo.cs test\UiTest.cs
if errorlevel 1 (
  echo UITEST BUILD FAILED
  exit /b 1
)
copy /y vendor\bin\pdfium.dll dist\pdfium.dll >nul
echo UITEST BUILD OK
