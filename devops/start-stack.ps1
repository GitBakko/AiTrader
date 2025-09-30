param(
    [string]$EnvFile = "../.env",
    [switch]$WaitForReset,
    [switch]$Force
)

$scriptDir = $PSScriptRoot
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $scriptDir ".."))
$composeFile = Join-Path $scriptDir "docker-compose.yml"
$envFilePath = if ([System.IO.Path]::IsPathRooted($EnvFile)) { $EnvFile } else { Join-Path $repoRoot $EnvFile }
$limiterPath = Join-Path $repoRoot "state/alphavantage_limiter.json"

if (-not (Test-Path $composeFile)) {
    throw "Compose file not found at $composeFile"
}

if (-not (Test-Path $envFilePath)) {
    throw "Environment file not found at $envFilePath"
}

if (-not (Test-Path $limiterPath)) {
    throw "Alpha Vantage limiter state missing at $limiterPath"
}

$limiterRaw = Get-Content -Path $limiterPath -Raw
$limiterState = $limiterRaw | ConvertFrom-Json
$currentUtc = (Get-Date).ToUniversalTime()
$limiterDateTime = [DateTime]::Parse($limiterState.DateUtc, [System.Globalization.CultureInfo]::InvariantCulture, [System.Globalization.DateTimeStyles]::AssumeUniversal)
$limiterDateTime = [DateTime]::SpecifyKind($limiterDateTime, [DateTimeKind]::Utc)
$limiterDate = $limiterDateTime.Date
$used = [int]$limiterState.Used

if ($limiterDate -eq $currentUtc.Date -and $used -ge 25) {
    if ($Force) {
        Write-Warning "Alpha Vantage quota reached ($used/25 for $limiterDate); proceeding due to -Force switch."
    } elseif ($WaitForReset) {
        $nextReset = $limiterDateTime.Date.AddDays(1)
        $wait = $nextReset - $currentUtc
        if ($wait.TotalSeconds -le 0) {
            Write-Host "Quota window has already reset; continuing."
        } else {
            $waitSeconds = [Math]::Ceiling($wait.TotalSeconds)
            Write-Host "Alpha Vantage quota exhausted. Waiting $waitSeconds seconds until 00:00 UTC reset..."
            Start-Sleep -Seconds $waitSeconds
        }
    } else {
        throw "Alpha Vantage quota exhausted ($used/25 for $limiterDate UTC). Re-run after 00:00 UTC or use -WaitForReset/-Force."
    }
}

Write-Host "Launching AiTrader FREE stack via docker compose" -ForegroundColor Green

$composeArgs = @("compose", "--env-file", $envFilePath, "-f", $composeFile, "up", "-d")
Push-Location $scriptDir
try {
    & docker @composeArgs
    if ($LASTEXITCODE -ne 0) {
        throw "docker compose exited with code $LASTEXITCODE"
    }
} finally {
    Pop-Location
}

Write-Host "Stack started. Current service status:" -ForegroundColor Cyan
& docker compose --env-file $envFilePath -f $composeFile ps
