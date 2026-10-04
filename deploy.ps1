# Deploys the Blazor app to the VPS (https://e-tradingtools.de).
#
#   .\deploy.ps1
#
# What it does NOT do: upload screenshots or touch any database. The live screenshots and database are on
# the VPS and stay there; your local ones are never read. See docs/DEPLOYMENT.md.
#
# Logs in with your SSH key (no password). If that fails, install the key once - see docs/DEPLOYMENT.md.

$ErrorActionPreference = 'Stop'

$Server  = 'root@217.154.9.117'
$Project = Join-Path $PSScriptRoot 'TradingTools.Blazor.csproj'
$Stamp   = Get-Date -Format 'yyyyMMdd-HHmmss'
$Work    = Join-Path ([IO.Path]::GetTempPath()) "tradingtools-deploy-$Stamp"
$Publish = Join-Path $Work 'publish'
$Archive = Join-Path $Work 'app.tar.gz'
$SshOptions = @('-o', 'BatchMode=yes', '-o', 'ConnectTimeout=15')

function Step($text) { Write-Host "`n== $text" -ForegroundColor Cyan }
function Fail($text) { Write-Host "DEPLOY FAILED: $text" -ForegroundColor Red; exit 1 }

try {
    Step 'Checking the SSH key login'
    ssh @SshOptions $Server 'true'
    if ($LASTEXITCODE -ne 0) { Fail "can't log in to $Server with your SSH key (no password is ever used). See docs/DEPLOYMENT.md." }

    Step 'Publishing (Release, linux-x64)'
    dotnet publish $Project -c Release -r linux-x64 --self-contained false -o $Publish -nologo
    if ($LASTEXITCODE -ne 0) { Fail 'dotnet publish failed.' }

    Step 'Checking the build contains no data'
    # Local-only settings: the VPS has its own appsettings.Production.json with the live connection string.
    Remove-Item -Force -ErrorAction SilentlyContinue (Join-Path $Publish 'appsettings.Development.json')
    $forbidden = @('wwwroot/Screenshots', 'wwwroot/ScreenshotsDev', 'appsettings.Production.json') |
        Where-Object { Test-Path (Join-Path $Publish $_) }
    if ($forbidden) { Fail "the build contains $($forbidden -join ', ') - screenshots and production settings must never be uploaded." }
    $dumps = Get-ChildItem $Publish -Recurse -File -Include *.dump, *.backup, *.sql, *.zip
    if ($dumps) { Fail "the build contains data files: $($dumps.Name -join ', ')" }
    $size = (Get-ChildItem $Publish -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
    Write-Host ("Build: {0:N1} MB, no screenshots, no databases." -f $size)

    Step 'Packing'
    tar -czf $Archive -C $Publish .
    if ($LASTEXITCODE -ne 0) { Fail 'packing failed.' }

    Step 'Uploading'
    # The server script with Unix line endings, whatever git checked out on Windows.
    $serverScript = Join-Path $Work 'server-deploy.sh'
    $text = (Get-Content -Raw (Join-Path $PSScriptRoot 'deploy/server-deploy.sh')) -replace "`r`n", "`n"
    [IO.File]::WriteAllText($serverScript, $text, (New-Object System.Text.UTF8Encoding $false))

    scp @SshOptions -q $Archive "${Server}:/tmp/tradingtools-blazor-$Stamp.tar.gz"
    if ($LASTEXITCODE -ne 0) { Fail 'upload failed.' }
    scp @SshOptions -q $serverScript "${Server}:/tmp/tradingtools-server-deploy-$Stamp.sh"
    if ($LASTEXITCODE -ne 0) { Fail 'upload failed.' }

    Step 'Installing on the server'
    ssh @SshOptions $Server "bash /tmp/tradingtools-server-deploy-$Stamp.sh /tmp/tradingtools-blazor-$Stamp.tar.gz $Stamp; code=`$?; rm -f /tmp/tradingtools-server-deploy-$Stamp.sh; exit `$code"
    if ($LASTEXITCODE -ne 0) { Fail 'the server install failed - see the messages above. The site keeps running the previous release.' }

    Step 'Checking https://e-tradingtools.de'
    $status = curl.exe -s -o NUL -w '%{http_code}' https://e-tradingtools.de/account/login
    if ($status -ne '200') { Fail "the site answered $status instead of 200." }
    Write-Host "Deployed $Stamp - https://e-tradingtools.de is up." -ForegroundColor Green
}
finally {
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $Work
}
