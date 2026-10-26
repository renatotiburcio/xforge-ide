<#
.SYNOPSIS
    Sobe a IDE (se necessário), espera ficar pronta e executa o gate de UI/acessibilidade.

.DESCRIPTION
    Uso local ou em CI. Se já houver um servidor respondendo na porta configurada,
    o script o reutiliza; caso contrário, inicia `dotnet run` e mata a árvore de processos no final.

.EXAMPLE
    pwsh ./scripts/qa-gate.ps1
#>
[CmdletBinding()]
param(
    [string]$BaseUrl = 'http://127.0.0.1:5600',
    [string]$Project = 'src/XForge.Ide.Server/XForge.Ide.Server.csproj',
    [string]$ReportPath = 'qa-report.json'
)

$ErrorActionPreference = 'Stop'

$ideRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
Push-Location $ideRoot
try {
    $uri = $BaseUrl.TrimEnd('/') + '/home'
    $port = ([Uri]$BaseUrl).Port

    $existing = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue
    $started = $null

    if (-not $existing) {
        $log = Join-Path $env:TEMP 'xforge-ide-qa-server.log'
        $err = Join-Path $env:TEMP 'xforge-ide-qa-server.err.log'
        Remove-Item $log, $err -ErrorAction SilentlyContinue

        $started = Start-Process `
            -FilePath 'dotnet' `
            -ArgumentList @('run', '--project', $Project, '--urls', $BaseUrl, '--no-build') `
            -PassThru `
            -RedirectStandardOutput $log `
            -RedirectStandardError $err
    }

    try {
        $ready = $false
        for ($i = 0; $i -lt 60; $i++) {
            try {
                $null = Invoke-WebRequest -Uri $uri -UseBasicParsing -TimeoutSec 2
                $ready = $true
                break
            }
            catch {
                Start-Sleep -Seconds 2
            }
        }

        if (-not $ready) {
            if ($started) {
                Get-Content (Join-Path $env:TEMP 'xforge-ide-qa-server.log') -ErrorAction SilentlyContinue
                Get-Content (Join-Path $env:TEMP 'xforge-ide-qa-server.err.log') -ErrorAction SilentlyContinue
            }
            throw "Servidor da IDE não respondeu em $BaseUrl."
        }

        $env:IDE_URL = $BaseUrl
        node scripts/qa-ui-a11y.mjs > $ReportPath
        $code = $LASTEXITCODE

        if ($code -ne 0) {
            if (Test-Path $ReportPath) {
                $report = Get-Content $ReportPath -Raw | ConvertFrom-Json
                $report.problems
            }
            exit $code
        }

        if (Test-Path $ReportPath) {
            $report = Get-Content $ReportPath -Raw | ConvertFrom-Json
            [pscustomobject]@{
                Ok               = $report.ok
                RoutesAudited    = $report.routesAudited
                InteractionsOk   = @($report.interactionResults | Where-Object { $_.ok }).Count
                InteractionsTotal= $report.interactionsAudited
                Problems         = $report.problems
            } | Format-List
        }
    }
    finally {
        if ($started -and -not $started.HasExited) {
            taskkill /PID $started.Id /T /F | Out-Null
        }
    }
}
finally {
    Pop-Location
}
