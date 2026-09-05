param(
    [string]$InfraPath = $env:DEPLOY_INFRA_PATH,
    [string]$NewImage = $env:DEPLOY_IMAGE,
    [string]$DeploySha = $env:DEPLOY_SHA
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$LockName = 'Global\FiapXDeployLock'
$LockTimeout = [TimeSpan]::FromMinutes(15)
$ServiceName = 'video-management-service'
$MigrationServiceName = 'video-management-migrations'
$ImageVariableName = 'VIDEO_MANAGEMENT_IMAGE'
$HealthUrl = 'http://localhost:8080/health'

$script:ComposeFile = $null
$script:EnvFile = $null
$script:PreviousImage = $null
$script:ExpectedImageId = $null
$script:RuntimeImageId = $null
$script:MigrationPassed = $false
$script:ContainerHealthPassed = $false
$script:HttpHealthPassed = $false
$script:NonTargetPreserved = $false
$script:EnvPersisted = $false
$script:TargetRecreated = $false
$script:TargetContainerId = $null

function Assert-NotBlank {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [AllowNull()][string]$Value
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        throw "$Name must not be empty."
    }
}

function Assert-CommitSha {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [AllowNull()][string]$Value
    )

    Assert-NotBlank -Name $Name -Value $Value
    if ($Value -notmatch '^[a-fA-F0-9]{40}$') {
        throw "$Name must be a full 40-character commit SHA."
    }
}

function Invoke-NativeOutput {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments
    )

    $output = & $FilePath @Arguments
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "$FilePath $($Arguments -join ' ') failed with exit code $exitCode."
    }

    return @($output)
}

function Invoke-DockerOutput {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)
    return Invoke-NativeOutput docker @Arguments
}

function Invoke-ComposeOutput {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)
    $composeArguments = @('compose', '--env-file', $script:EnvFile, '-f', $script:ComposeFile) + $Arguments
    return Invoke-NativeOutput docker @composeArguments
}

function Assert-PathExists {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Description,
        [ValidateSet('Container', 'Leaf')][string]$PathType
    )

    if (-not (Test-Path -LiteralPath $Path -PathType $PathType)) {
        throw "$Description not found: $Path"
    }
}

function Assert-DeployImage {
    param(
        [Parameter(Mandatory = $true)][string]$Image,
        [Parameter(Mandatory = $true)][string]$ExpectedSha
    )

    if ($Image -match '(^|:)latest$') {
        throw 'Deploy image must never use latest.'
    }

    if ($Image -notmatch '^[^:]+/.+:[^:]+$') {
        throw "Deploy image must include an explicit tag: $Image"
    }

    Assert-CommitSha -Name 'DEPLOY_SHA/DeploySha' -Value $ExpectedSha
    $expectedSuffix = ":$ExpectedSha"
    if (-not $Image.EndsWith($expectedSuffix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "DEPLOY_IMAGE must end with DEPLOY_SHA $expectedSuffix."
    }
}

function Get-EnvValue {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Name
    )

    foreach ($line in [IO.File]::ReadAllLines($Path)) {
        $trimmed = $line.TrimStart()
        if ($trimmed.StartsWith('#')) {
            continue
        }

        $separator = $line.IndexOf('=')
        if ($separator -le 0) {
            continue
        }

        $candidateName = $line.Substring(0, $separator).Trim()
        if ($candidateName -eq $Name) {
            return $line.Substring($separator + 1).Trim()
        }
    }

    throw "$Name was not found in .env."
}

function Get-TextEncoding {
    param([Parameter(Mandatory = $true)][string]$Path)

    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        return New-Object Text.UTF8Encoding($true)
    }

    if ($bytes.Length -ge 2 -and $bytes[0] -eq 0xFF -and $bytes[1] -eq 0xFE) {
        return [Text.Encoding]::Unicode
    }

    if ($bytes.Length -ge 2 -and $bytes[0] -eq 0xFE -and $bytes[1] -eq 0xFF) {
        return [Text.Encoding]::BigEndianUnicode
    }

    return New-Object Text.UTF8Encoding($false)
}

function Set-EnvValueAtomic {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Value
    )

    $encoding = Get-TextEncoding $Path
    $content = [IO.File]::ReadAllText($Path, $encoding)
    $pattern = "(?m)^(?!\s*#)(\s*$([regex]::Escape($Name))\s*=\s*).*$"
    $regex = New-Object regex($pattern)
    if (-not $regex.IsMatch($content)) {
        throw "$Name was not found in .env."
    }

    $newContent = $regex.Replace($content, { param($match) $match.Groups[1].Value + $Value }, 1)
    $directory = Split-Path -Parent $Path
    $tempPath = Join-Path $directory ('.env.tmp.' + [Guid]::NewGuid().ToString('N'))
    $backupPath = Join-Path $directory ('.env.bak.' + [Guid]::NewGuid().ToString('N'))

    [IO.File]::WriteAllText($tempPath, $newContent, $encoding)
    try {
        [IO.File]::Replace($tempPath, $Path, $backupPath, $true)
        if (Test-Path -LiteralPath $backupPath -PathType Leaf) {
            Remove-Item -LiteralPath $backupPath -Force
        }
    }
    catch {
        if (Test-Path -LiteralPath $tempPath -PathType Leaf) {
            Remove-Item -LiteralPath $tempPath -Force
        }
        throw
    }

    $actual = Get-EnvValue -Path $Path -Name $Name
    if ($actual -ne $Value) {
        throw "$Name persistence verification failed."
    }
}

function Get-ComposeConfig {
    $json = (Invoke-ComposeOutput 'config' '--format' 'json') -join [Environment]::NewLine
    if ([string]::IsNullOrWhiteSpace($json)) {
        throw 'docker compose config returned empty JSON.'
    }

    return $json | ConvertFrom-Json
}

function Assert-ComposeServiceImage {
    param(
        [Parameter(Mandatory = $true)][string]$Service,
        [Parameter(Mandatory = $true)][string]$ExpectedImage
    )

    $config = Get-ComposeConfig
    $serviceProperty = $config.services.PSObject.Properties[$Service]
    if ($null -eq $serviceProperty) {
        throw "Service $Service was not found in compose config."
    }

    $actualImage = $serviceProperty.Value.image
    if ($actualImage -ne $ExpectedImage) {
        throw "Service $Service resolved image [$actualImage], expected [$ExpectedImage]."
    }
}

function Get-ServiceContainerIds {
    param([Parameter(Mandatory = $true)][string]$Service)
    return @((Invoke-ComposeOutput 'ps' '-a' '-q' $Service) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object)
}

function Get-ContainerScalar {
    param(
        [Parameter(Mandatory = $true)][string]$ContainerId,
        [Parameter(Mandatory = $true)][string]$Format
    )

    return ((Invoke-DockerOutput 'inspect' '--format' $Format $ContainerId) | Select-Object -First 1).Trim()
}

function Assert-ServiceRunning {
    param([Parameter(Mandatory = $true)][string]$Service)

    $ids = @(Get-ServiceContainerIds $Service)
    if ($ids.Count -eq 0) {
        throw "Required service $Service has no container. Start the integrated environment before deploy."
    }

    foreach ($id in $ids) {
        $running = Get-ContainerScalar -ContainerId $id -Format '{{.State.Running}}'
        if ($running -ne 'true') {
            throw "Required service $Service container $id is not running."
        }
    }
}

function Get-ContainerIdSnapshot {
    param([Parameter(Mandatory = $true)][string[]]$Services)

    $snapshot = @{}
    foreach ($service in $Services) {
        $ids = @(Get-ServiceContainerIds $service)
        $snapshot[$service] = $ids -join ','
    }

    return $snapshot
}

function Assert-ContainerIdsPreserved {
    param(
        [Parameter(Mandatory = $true)][hashtable]$Before,
        [Parameter(Mandatory = $true)][string[]]$Services
    )

    foreach ($service in $Services) {
        $afterIds = @(Get-ServiceContainerIds $service)
        $after = $afterIds -join ','
        if ($Before[$service] -ne $after) {
            throw "Non-target service $service container IDs changed."
        }
    }
}

function Wait-ContainerRunning {
    param(
        [Parameter(Mandatory = $true)][string]$ContainerId,
        [int]$TimeoutSeconds = 60
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $running = Get-ContainerScalar -ContainerId $ContainerId -Format '{{.State.Running}}'
        if ($running -eq 'true') {
            return
        }

        Start-Sleep -Seconds 2
    }

    throw "Container $ContainerId did not become running."
}

function Wait-ContainerHealth {
    param(
        [Parameter(Mandatory = $true)][string]$ContainerId,
        [int]$TimeoutSeconds = 120
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $health = Get-ContainerScalar -ContainerId $ContainerId -Format '{{if .State.Health}}{{.State.Health.Status}}{{end}}'
        if ([string]::IsNullOrWhiteSpace($health)) {
            return 'not-configured'
        }

        if ($health -eq 'healthy') {
            return 'healthy'
        }

        Start-Sleep -Seconds 3
    }

    throw "Container $ContainerId did not become healthy."
}

function Wait-HttpHealth {
    param(
        [Parameter(Mandatory = $true)][string]$Uri,
        [int]$TimeoutSeconds = 120
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    $lastError = $null
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        try {
            $response = Invoke-WebRequest -Uri $Uri -UseBasicParsing -TimeoutSec 5
            if ([int]$response.StatusCode -ge 200 -and [int]$response.StatusCode -lt 300) {
                return
            }
        }
        catch {
            $lastError = $_.Exception.Message
        }

        Start-Sleep -Seconds 3
    }

    if ($lastError) {
        throw "HTTP health check failed for $Uri. Last error: $lastError"
    }

    throw "HTTP health check failed for $Uri."
}

function Add-StepSummary {
    if ([string]::IsNullOrWhiteSpace($env:GITHUB_STEP_SUMMARY)) {
        return
    }

    $lines = @(
        '### Deploy Management',
        '',
        '| Item | Value |',
        '| --- | --- |',
        "| Service | $ServiceName |",
        "| Previous image | $script:PreviousImage |",
        "| New image | $NewImage |",
        "| Deploy SHA | $DeploySha |",
        "| Runtime Image ID | $script:RuntimeImageId |",
        "| Migration | PASS |",
        "| Docker health | PASS |",
        "| /health | PASS |",
        "| Non-target containers preserved | PASS |",
        "| .env persistence | PASS |"
    )

    Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value $lines
}

function Report-Divergence {
    if (-not $script:TargetRecreated) {
        return
    }

    Write-Warning 'Deployment validation failed after target recreation. Automatic rollback was not performed.'
    Write-Warning "Previous .env image: $script:PreviousImage"
    Write-Warning "Attempted image: $NewImage"
    if (-not [string]::IsNullOrWhiteSpace($script:TargetContainerId)) {
        try {
            $runtimeImage = Get-ContainerScalar -ContainerId $script:TargetContainerId -Format '{{.Config.Image}}'
            $runtimeImageId = Get-ContainerScalar -ContainerId $script:TargetContainerId -Format '{{.Image}}'
            Write-Warning "Runtime Config.Image: $runtimeImage"
            Write-Warning "Runtime Image ID: $runtimeImageId"
        }
        catch {
            Write-Warning "Could not inspect runtime image after failure: $($_.Exception.Message)"
        }
    }
    Write-Warning '.env still represents the last confirmed deploy.'
}

function Invoke-Deploy {
    Assert-NotBlank -Name 'DEPLOY_INFRA_PATH/InfraPath' -Value $InfraPath
    Assert-NotBlank -Name 'DEPLOY_IMAGE/NewImage' -Value $NewImage
    Assert-DeployImage -Image $NewImage -ExpectedSha $DeploySha

    $resolvedInfraPath = (Resolve-Path -LiteralPath $InfraPath).Path
    $script:ComposeFile = Join-Path $resolvedInfraPath 'docker-compose.yml'
    $script:EnvFile = Join-Path $resolvedInfraPath '.env'

    Assert-PathExists -Path $resolvedInfraPath -Description 'Infra path' -PathType Container
    Assert-PathExists -Path $script:ComposeFile -Description 'Compose file' -PathType Leaf
    Assert-PathExists -Path $script:EnvFile -Description '.env file' -PathType Leaf

    Push-Location $resolvedInfraPath
    try {
        [void](Invoke-DockerOutput 'version')
        [void](Invoke-DockerOutput 'compose' 'version')
        [void](Invoke-ComposeOutput 'config' '--quiet')

        Assert-ServiceRunning 'postgres'
        Assert-ServiceRunning 'redis'
        Assert-ServiceRunning 'rabbitmq'
        Assert-ServiceRunning 'minio'
        Assert-ServiceRunning 'keycloak'
        Assert-ServiceRunning 'mailpit'

        $nonTargetServices = @('video-processing-service', 'postgres', 'redis', 'rabbitmq', 'minio', 'keycloak', 'mailpit')
        $beforeIds = Get-ContainerIdSnapshot $nonTargetServices

        $script:PreviousImage = Get-EnvValue -Path $script:EnvFile -Name $ImageVariableName
        $env:VIDEO_MANAGEMENT_IMAGE = $NewImage

        [void](Invoke-ComposeOutput 'config' '--quiet')
        Assert-ComposeServiceImage -Service $MigrationServiceName -ExpectedImage $NewImage
        Assert-ComposeServiceImage -Service $ServiceName -ExpectedImage $NewImage

        [void](Invoke-DockerOutput 'pull' $NewImage)
        $script:ExpectedImageId = Get-ContainerScalar -ContainerId $NewImage -Format '{{.Id}}'

        [void](Invoke-ComposeOutput 'run' '--rm' '--no-deps' $MigrationServiceName)
        $script:MigrationPassed = $true

        [void](Invoke-ComposeOutput 'up' '-d' '--no-deps' '--force-recreate' $ServiceName)
        $script:TargetRecreated = $true

        $targetIds = @(Get-ServiceContainerIds $ServiceName)
        if ($targetIds.Count -ne 1) {
            throw "$ServiceName expected 1 container, found $($targetIds.Count)."
        }

        $script:TargetContainerId = $targetIds[0]
        Wait-ContainerRunning -ContainerId $script:TargetContainerId
        $script:RuntimeImageId = Get-ContainerScalar -ContainerId $script:TargetContainerId -Format '{{.Image}}'
        if ($script:RuntimeImageId -ne $script:ExpectedImageId) {
            throw "$ServiceName image ID mismatch. Expected $script:ExpectedImageId, got $script:RuntimeImageId."
        }

        $healthStatus = Wait-ContainerHealth -ContainerId $script:TargetContainerId
        if ($healthStatus -eq 'healthy' -or $healthStatus -eq 'not-configured') {
            $script:ContainerHealthPassed = $true
        }

        Wait-HttpHealth -Uri $HealthUrl
        $script:HttpHealthPassed = $true

        Assert-ContainerIdsPreserved -Before $beforeIds -Services $nonTargetServices
        $script:NonTargetPreserved = $true

        Set-EnvValueAtomic -Path $script:EnvFile -Name $ImageVariableName -Value $NewImage
        $script:EnvPersisted = $true

        Add-StepSummary

        Write-Host "Deploy Management PASS. Service=$ServiceName Image=$NewImage ImageId=$script:RuntimeImageId"
    }
    finally {
        Pop-Location
    }
}

$mutex = New-Object System.Threading.Mutex($false, $LockName)
$lockAcquired = $false
try {
    try {
        $lockAcquired = $mutex.WaitOne($LockTimeout)
    }
    catch [System.Threading.AbandonedMutexException] {
        $lockAcquired = $true
        Write-Warning 'Deploy mutex was abandoned by a previous process. Continuing with acquired lock.'
    }

    if (-not $lockAcquired) {
        throw "Could not acquire deploy lock $LockName within $($LockTimeout.TotalMinutes) minutes."
    }

    Invoke-Deploy
}
catch {
    Report-Divergence
    throw
}
finally {
    if ($lockAcquired) {
        $mutex.ReleaseMutex()
    }
    $mutex.Dispose()
}
