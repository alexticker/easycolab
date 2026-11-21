# EasyColab Windows 一键本地编译打包脚本 (WinUI 3 + .NET 10 + Python embeddable bridge runtime)
#
# 产物:
#   dist\EasyColab\EasyColab.exe          WinUI 3 原生客户端 (self-contained, 无需安装 .NET / Windows App SDK)
#   dist\EasyColab\runtime\python.exe     官方 Python embeddable 运行时 (仅用于 colab_bridge.py)
#   dist\EasyColab\site-packages\         colab_bridge 依赖 (google-colab-cli / google-auth / websocket-client ...)
#   EasyColab-Windows-<arch>.zip          免安装发行包
#
# 用法: pwsh windows_app/build_windows_app.ps1 [-Arch x64|arm64] [-Configuration Release] [-SkipZip]
param(
    [ValidateSet("x64", "arm64")]
    [string]$Arch = "x64",
    [string]$Configuration = "Release",
    [switch]$SkipZip
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$ScriptDir   = Split-Path -Parent $MyInvocation.MyCommand.Definition
$ProjectRoot = (Resolve-Path (Join-Path $ScriptDir "..")).Path
$CsProj      = Join-Path $ScriptDir "EasyColab\EasyColab.csproj"
$DistDir     = Join-Path $ProjectRoot "dist\EasyColab"
$RuntimeDir  = Join-Path $DistDir "runtime"
$SiteDir     = Join-Path $DistDir "site-packages"
$CacheDir    = Join-Path $ProjectRoot "build\win_cache"
$PyVersion   = "3.12.10"
$PyTag       = "312"
$PyEmbedArch = if ($Arch -eq "arm64") { "arm64" } else { "amd64" }
$PipPlatform = if ($Arch -eq "arm64") { "win_arm64" } else { "win_amd64" }
$Rid         = "win-$Arch"
$ZipPath     = Join-Path $ProjectRoot "EasyColab-Windows-$Arch.zip"

$BridgeRequirements = @(
    "google-colab-cli>=0.7.5",
    "google-auth>=2.28.0",
    "google-auth-oauthlib>=1.2.0",
    "requests>=2.31.0",
    "requests-oauthlib>=1.3.1",
    "websocket-client>=1.7.0"
)

function Step($msg) { Write-Host "`n==> $msg" -ForegroundColor Cyan }

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  EasyColab Windows 一键编译脚本 (WinUI 3 · $Rid · $Configuration)" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "未检测到 .NET SDK，请先安装 .NET 10 SDK: https://dotnet.microsoft.com/download"
}
$HostPython = $null
$cmd = Get-Command python -ErrorAction SilentlyContinue
if ($cmd -and $cmd.Source -notlike "*WindowsApps*") { $HostPython = $cmd.Source }
if (-not $HostPython) {
    $cmd = Get-Command py -ErrorAction SilentlyContinue
    if ($cmd) { $HostPython = $cmd.Source }
}
if (-not $HostPython) {
    throw "未检测到 Python (用于下载 bridge 依赖 wheel)，请安装 Python 3.10+ 并加入 PATH。"
}

# ── 1. Publish WinUI 3 app ────────────────────────────────────────────────
Step "[1/5] dotnet publish ($Rid, self-contained)"
if (Test-Path $DistDir) { Remove-Item -Recurse -Force $DistDir }
dotnet publish $CsProj -c $Configuration -r $Rid -p:Platform=$Arch -p:PublishReadyToRun=true -o $DistDir --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败" }
# PDBs are not needed by end users
Get-ChildItem $DistDir -Filter *.pdb -Recurse | Remove-Item -Force

# ── 2. Python embeddable runtime ──────────────────────────────────────────
Step "[2/5] Python $PyVersion embeddable runtime ($PyEmbedArch)"
New-Item -ItemType Directory -Force -Path $CacheDir | Out-Null
$EmbedZip = Join-Path $CacheDir "python-$PyVersion-embed-$PyEmbedArch.zip"
if (-not (Test-Path $EmbedZip)) {
    $url = "https://www.python.org/ftp/python/$PyVersion/python-$PyVersion-embed-$PyEmbedArch.zip"
    Write-Host "    Downloading $url"
    Invoke-WebRequest -Uri $url -OutFile $EmbedZip
}
Expand-Archive -Path $EmbedZip -DestinationPath $RuntimeDir -Force
# Isolated module search path: never pick up system PYTHONPATH / registry installs
@(
    "python$PyTag.zip",
    ".",
    "..",
    "..\site-packages",
    "import site"
) -join "`r`n" | Set-Content -Path (Join-Path $RuntimeDir "python$PyTag._pth") -Encoding ascii -NoNewline

# ── 3. Bridge dependencies (binary wheels for the target platform) ───────
Step "[3/5] Installing colab_bridge dependencies into site-packages ($PipPlatform)"
$pipArgs = @("-m", "pip", "install", "--disable-pip-version-check", "--no-compile", "-q",
    "--target", $SiteDir,
    "--platform", $PipPlatform, "--python-version", "3.12", "--implementation", "cp", "--abi", "cp$PyTag",
    "--only-binary=:all:") + $BridgeRequirements
if ((Split-Path -Leaf $HostPython) -eq "py.exe") { $pipArgs = @("-3") + $pipArgs }
& $HostPython @pipArgs
if ($LASTEXITCODE -ne 0) { throw "pip install bridge 依赖失败" }
Get-ChildItem $SiteDir -Directory -Recurse -Filter "__pycache__" | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

# ── 4. Smoke test ─────────────────────────────────────────────────────────
Step "[4/5] Smoke test"
foreach ($f in @("EasyColab.exe", "colab_bridge.py", "app.py", "EasyColab_Server.ipynb", "VERSION")) {
    if (-not (Test-Path (Join-Path $DistDir $f))) { throw "发行目录缺少 $f" }
}
if ($Arch -eq "x64" -or $env:PROCESSOR_ARCHITECTURE -eq "ARM64") {
    $env:PYTHONUTF8 = "1"
    $out = & (Join-Path $RuntimeDir "python.exe") -X utf8 -c "import colab_cli, google.auth, websocket, requests; print('bridge-deps-ok')"
    if ($out -notmatch "bridge-deps-ok") { throw "嵌入式 Python 无法导入 bridge 依赖: $out" }
    $auth = & (Join-Path $RuntimeDir "python.exe") -X utf8 (Join-Path $DistDir "colab_bridge.py") auth_status 2>&1 | Out-String
    Write-Host "    auth_status -> $($auth.Trim())"
    if ($auth -notmatch '"ok"') { throw "colab_bridge.py auth_status 冒烟测试失败" }
} else {
    Write-Host "    (跳过: 当前主机无法运行 $Arch 二进制)"
}

# ── 5. Zip ────────────────────────────────────────────────────────────────
if (-not $SkipZip) {
    Step "[5/5] Packaging $ZipPath"
    if (Test-Path $ZipPath) { Remove-Item -Force $ZipPath }
    Compress-Archive -Path $DistDir -DestinationPath $ZipPath -CompressionLevel Optimal
}

$size = "{0:N1} MB" -f ((Get-ChildItem $DistDir -Recurse -File | Measure-Object Length -Sum).Sum / 1MB)
Write-Host ""
Write-Host "✅ 编译成功！" -ForegroundColor Green
Write-Host "  - 可执行程序: $DistDir\EasyColab.exe  ($size)"
if (-not $SkipZip) { Write-Host "  - 发行压缩包: $ZipPath" }
