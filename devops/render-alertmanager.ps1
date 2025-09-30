param(
    [string]$EnvFile = "../.env",
    [string]$TemplatePath = "../observability/alertmanager.yml",
    [string]$OutputPath = "../observability/alertmanager.rendered.yml"
)

if (-not (Test-Path $EnvFile)) {
    throw "Env file '$EnvFile' not found."
}
if (-not (Test-Path $TemplatePath)) {
    throw "Template file '$TemplatePath' not found."
}

$envValues = @{}
Get-Content $EnvFile | ForEach-Object {
    $line = $_.Trim()
    if (-not $line) { return }
    if ($line.StartsWith('#')) { return }
    $splitIndex = $line.IndexOf('=')
    if ($splitIndex -lt 1) { return }
    $key = $line.Substring(0, $splitIndex)
    $value = $line.Substring($splitIndex + 1)
    if ($value.StartsWith('"') -and $value.EndsWith('"')) {
        $value = $value.Substring(1, $value.Length - 2)
    }
    $envValues[$key] = $value
}

$template = Get-Content $TemplatePath -Raw
$placeholders = @('ALERT_SMTP_SMARTHOST','ALERT_SMTP_FROM','ALERT_SMTP_USERNAME','ALERT_SMTP_PASSWORD')
foreach ($key in $placeholders) {
    if (-not $envValues.ContainsKey($key)) {
        throw "Missing required key '$key' in env file."
    }
    $template = $template.Replace('${' + $key + '}', $envValues[$key])
}

Set-Content -Path $OutputPath -Value $template -Encoding UTF8
Write-Host "Rendered Alertmanager config to $OutputPath."