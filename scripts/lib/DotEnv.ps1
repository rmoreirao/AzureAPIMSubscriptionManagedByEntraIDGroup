<#
.SYNOPSIS
  Shared .env loader for the repo's PowerShell scripts.

.DESCRIPTION
  Import-DotEnv parses a `.env` file (KEY=VALUE lines) and returns the values as a hashtable. By
  default it also sets each key as a process environment variable so downstream tools/scripts can
  read them. Lines that are blank or start with `#` are ignored. Surrounding single/double quotes
  around a value are stripped. Existing environment variables are only overwritten when -Force is
  supplied, so explicit parameters/exports always win over the file.

.PARAMETER Path
  Path to the .env file. Defaults to a `.env` at the repository root (two levels above this file).

.PARAMETER SetEnv
  When set (default), each parsed key is exported as a process environment variable.

.PARAMETER Force
  Overwrite environment variables that are already set. Off by default.

.EXAMPLE
  . "$PSScriptRoot/lib/DotEnv.ps1"
  $env = Import-DotEnv
  $rg  = $env.AZURE_RESOURCE_GROUP
#>
function Import-DotEnv {
  [CmdletBinding()]
  param(
    [string]$Path,
    [bool]$SetEnv = $true,
    [switch]$Force
  )

  if (-not $Path) {
    # This file lives in <repo>/scripts/lib; the repo root is two levels up.
    $repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    $Path = Join-Path $repoRoot ".env"
  }

  $values = [ordered]@{}

  if (-not (Test-Path $Path)) {
    Write-Verbose "No .env file found at '$Path'."
    return $values
  }

  foreach ($line in Get-Content -LiteralPath $Path) {
    $trimmed = $line.Trim()
    if ($trimmed -eq "" -or $trimmed.StartsWith("#")) { continue }

    $eq = $trimmed.IndexOf("=")
    if ($eq -lt 1) { continue }

    $key = $trimmed.Substring(0, $eq).Trim()
    $val = $trimmed.Substring($eq + 1).Trim()

    # Strip matching surrounding quotes.
    if ($val.Length -ge 2 -and (
        ($val.StartsWith('"') -and $val.EndsWith('"')) -or
        ($val.StartsWith("'") -and $val.EndsWith("'")))) {
      $val = $val.Substring(1, $val.Length - 2)
    }

    $values[$key] = $val

    if ($SetEnv) {
      $existing = [System.Environment]::GetEnvironmentVariable($key, "Process")
      if ($Force -or [string]::IsNullOrEmpty($existing)) {
        Set-Item -Path "Env:$key" -Value $val
      }
    }
  }

  return $values
}

<#
.SYNOPSIS
  Writes/updates a set of KEY=VALUE pairs in a .env file, preserving comments and unrelated keys.

.DESCRIPTION
  For each key in -Values, an existing `KEY=...` line is replaced in place; keys not already
  present are appended. Comment/blank lines and keys not in -Values are left untouched. Useful for
  folding deployment outputs back into an existing .env without clobbering the user's other values.

.PARAMETER Path
  Path to the .env file to create or update.

.PARAMETER Values
  Ordered hashtable / hashtable of KEY = VALUE pairs to set.
#>
function Set-DotEnvValues {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory)][string]$Path,
    [Parameter(Mandatory)][System.Collections.IDictionary]$Values
  )

  $lines = if (Test-Path -LiteralPath $Path) { @(Get-Content -LiteralPath $Path) } else { @() }
  $remaining = [ordered]@{}
  foreach ($k in $Values.Keys) { $remaining[$k] = $Values[$k] }

  for ($i = 0; $i -lt $lines.Count; $i++) {
    $trimmed = $lines[$i].Trim()
    if ($trimmed -eq "" -or $trimmed.StartsWith("#")) { continue }
    $eq = $trimmed.IndexOf("=")
    if ($eq -lt 1) { continue }
    $key = $trimmed.Substring(0, $eq).Trim()
    if ($remaining.Contains($key)) {
      $lines[$i] = "$key=$($remaining[$key])"
      $remaining.Remove($key)
    }
  }

  $output = [System.Collections.Generic.List[string]]::new()
  $output.AddRange([string[]]$lines)
  foreach ($k in $remaining.Keys) { $output.Add("$k=$($remaining[$k])") }

  Set-Content -LiteralPath $Path -Value $output -Encoding UTF8
}
