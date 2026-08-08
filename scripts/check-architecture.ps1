$ErrorActionPreference = "Stop"

$rules = @{
  "RetryShield.Domain" = @()
  "RetryShield.Application" = @("RetryShield.Domain")
  "RetryShield.Infrastructure" = @("RetryShield.Domain", "RetryShield.Application")
}

$violations = @()
foreach ($projectName in $rules.Keys) {
  $project = Get-ChildItem -Path src -Filter "$projectName.csproj" -Recurse | Select-Object -First 1
  if (-not $project) {
    $violations += "Missing project: $projectName"
    continue
  }

  [xml]$xml = Get-Content $project.FullName
  $references = @($xml.Project.ItemGroup.ProjectReference.Include)
  foreach ($reference in $references) {
    if (-not $reference) { continue }
    $target = [IO.Path]::GetFileNameWithoutExtension($reference)
    if ($target -notin $rules[$projectName]) {
      $violations += "$projectName must not reference $target"
    }
  }
}

if ($violations.Count -gt 0) {
  $violations | ForEach-Object { Write-Error $_ }
  exit 1
}

Write-Host "Architecture dependency rules passed."
