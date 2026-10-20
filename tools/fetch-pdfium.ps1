# 拉取 PDF 渲染引擎 pdfium（Windows x64 预编译）
# 用法: powershell -ExecutionPolicy Bypass -File tools\fetch-pdfium.ps1
# 说明: pdfium.dll 约 7.2MB，不入库；构建/运行都需要它，放在 dist\ 与 vendor\bin\ 下。
param(
  [string]$Version = "chromium/8111",
  [string]$Root = ""
)

if ([string]::IsNullOrEmpty($Root)) {
  $Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
}

$vendor = Join-Path $Root "vendor"
$dist = Join-Path $Root "dist"
$tgz = Join-Path $vendor "pdfium-win-x64.tgz"
$url = "https://github.com/bblanchon/pdfium-binaries/releases/download/$Version/pdfium-win-x64.tgz"

New-Item -ItemType Directory -Force -Path $vendor | Out-Null
New-Item -ItemType Directory -Force -Path $dist | Out-Null

Write-Host "→ 下载 $url"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Invoke-WebRequest -Uri $url -OutFile $tgz -UseBasicParsing

Write-Host "→ 解包到 $vendor"
Push-Location $vendor
try {
  tar -xzf "pdfium-win-x64.tgz"
  if ($LASTEXITCODE -ne 0) { throw "tar 解包失败（Windows 10 1803+ 自带 tar.exe）" }
} finally {
  Pop-Location
}

$dll = Join-Path $vendor "bin\pdfium.dll"
if (-not (Test-Path $dll)) { throw "解包后找不到 $dll" }

Copy-Item $dll (Join-Path $dist "pdfium.dll") -Force
$sizeMb = [math]::Round((Get-Item $dll).Length / 1MB, 2)
Write-Host "✔ 完成: $dll ($sizeMb MB)，已复制到 dist\pdfium.dll"
Write-Host "  现在可以运行 build.cmd"
