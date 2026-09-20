param(
    [string]$TestResultsDirectory = 'TestResults',
    [string]$BadgePath = '.github/badges/coverage.svg',
    [string]$SummaryPath = 'TestResults/coverage-summary.md',
    [double]$MinimumCoverage = 85
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$culture = [Globalization.CultureInfo]::InvariantCulture

if (-not (Test-Path -LiteralPath $TestResultsDirectory -PathType Container)) {
    throw "Test results directory not found: $TestResultsDirectory"
}

$coverageFiles = @(
    Get-ChildItem -LiteralPath $TestResultsDirectory -Recurse -Filter 'coverage.cobertura.xml' |
        Sort-Object LastWriteTimeUtc -Descending
)

if ($coverageFiles.Count -eq 0) {
    throw "No coverage.cobertura.xml file found under $TestResultsDirectory."
}

$coverageFile = $coverageFiles[0]
[xml]$coverage = Get-Content -LiteralPath $coverageFile.FullName

$lineRate = [double]::Parse($coverage.coverage.GetAttribute('line-rate'), $culture)
$lineCoverage = [Math]::Round($lineRate * 100, 2, [MidpointRounding]::AwayFromZero)
$lineCoverageText = $lineCoverage.ToString('0.00', $culture) + '%'

$linesCovered = $coverage.coverage.GetAttribute('lines-covered')
$linesValid = $coverage.coverage.GetAttribute('lines-valid')

$color = if ($lineCoverage -ge 90) {
    '#2ea44f'
}
elseif ($lineCoverage -ge 80) {
    '#4c9f38'
}
elseif ($lineCoverage -ge 60) {
    '#dfb317'
}
else {
    '#e05d44'
}

$badgeDirectory = Split-Path -Parent $BadgePath
if (-not [string]::IsNullOrWhiteSpace($badgeDirectory)) {
    New-Item -ItemType Directory -Path $badgeDirectory -Force | Out-Null
}

$badgeSvg = @"
<svg xmlns="http://www.w3.org/2000/svg" width="132" height="20" role="img" aria-label="Coverage: $lineCoverageText">
  <title>Coverage: $lineCoverageText</title>
  <linearGradient id="s" x2="0" y2="100%">
    <stop offset="0" stop-color="#bbb" stop-opacity=".1"/>
    <stop offset="1" stop-opacity=".1"/>
  </linearGradient>
  <clipPath id="r">
    <rect width="132" height="20" rx="3" fill="#fff"/>
  </clipPath>
  <g clip-path="url(#r)">
    <rect width="76" height="20" fill="#555"/>
    <rect x="76" width="56" height="20" fill="$color"/>
    <rect width="132" height="20" fill="url(#s)"/>
  </g>
  <g fill="#fff" text-anchor="middle" font-family="Verdana,Geneva,DejaVu Sans,sans-serif" text-rendering="geometricPrecision" font-size="110">
    <text aria-hidden="true" x="385" y="150" fill="#010101" fill-opacity=".3" transform="scale(.1)" textLength="660">Coverage</text>
    <text x="385" y="140" transform="scale(.1)" fill="#fff" textLength="660">Coverage</text>
    <text aria-hidden="true" x="1030" y="150" fill="#010101" fill-opacity=".3" transform="scale(.1)" textLength="460">$lineCoverageText</text>
    <text x="1030" y="140" transform="scale(.1)" fill="#fff" textLength="460">$lineCoverageText</text>
  </g>
</svg>
"@

Set-Content -LiteralPath $BadgePath -Value $badgeSvg -Encoding UTF8

$summaryDirectory = Split-Path -Parent $SummaryPath
if (-not [string]::IsNullOrWhiteSpace($summaryDirectory)) {
    New-Item -ItemType Directory -Path $summaryDirectory -Force | Out-Null
}

$summary = @(
    '### Tests',
    '',
    "Coverage: $lineCoverageText",
    '',
    '| Metric | Value |',
    '| --- | --- |',
    "| Line coverage | $lineCoverageText |",
    "| Covered lines | $linesCovered |",
    "| Valid lines | $linesValid |",
    "| Cobertura report | $($coverageFile.FullName) |"
)

Set-Content -LiteralPath $SummaryPath -Value $summary -Encoding UTF8

if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_STEP_SUMMARY)) {
    Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value $summary
}

Write-Host "Coverage: $lineCoverageText"

if ($lineCoverage -lt $MinimumCoverage) {
    throw "Coverage $lineCoverageText is below the required minimum of $($MinimumCoverage.ToString('0.00', $culture))%."
}
