# Version Increment Tool for ConPtyTermEmulator
# This script increments version numbers in project files

param (
    [Parameter(HelpMessage="Increment the major version number")]
    [switch]$Major,

    [Parameter(HelpMessage="Increment the minor version number")]
    [switch]$Minor,

    [Parameter(HelpMessage="Increment the patch version number")]
    [switch]$Patch,

    [Parameter(HelpMessage="Increment the beta version number")]
    [switch]$Beta,

    [Parameter(HelpMessage="Build packages after incrementing version")]
    [switch]$Build,

    [Parameter(HelpMessage="Build configuration (Debug or Release) if you want to build after updating")]
    [string]$Config = "Debug",

    [Parameter(HelpMessage="Skip confirmation prompt")]
    [switch]$NoConfirm
)

Write-Host "===== Version Increment Tool for ConPtyTermEmulator =====" -ForegroundColor Cyan
Write-Host "This script increments version numbers in project files" -ForegroundColor Cyan

# Determine increment type based on parameters
$incrementType = "patch" # Default
if ($Major) { $incrementType = "major" }
elseif ($Minor) { $incrementType = "minor" }
elseif ($Beta) { $incrementType = "beta" }
elseif ($Patch) { $incrementType = "patch" }

Write-Host "`nConfiguration:" -ForegroundColor Yellow
Write-Host "- Increment Type: $incrementType"
Write-Host "- Build Config: $Config"
Write-Host "- Build after increment: $($Build.IsPresent)"

# Project file paths
$easyTerminalProj = "EasyWindowsTerminalControl\EasyWindowsTerminalControl.csproj"
$easyTerminalWinUIProj = "EasyWindowsTerminalControl.WinUI\EasyWindowsTerminalControl.WinUI.csproj"
$terminalWinUI3Proj = "Microsoft.Terminal.WinUI3\Microsoft.Terminal.WinUI3.csproj"
$termExampleProj = "TermExample\TermExample.csproj"
$termExampleWinUIProj = "TermExample.WinUI\TermExample.WinUI.csproj"

# Get current versions from project files
function Get-ProjectVersion {
    param (
        [string]$ProjectPath
    )

    $content = Get-Content $ProjectPath -Raw
    if ($content -match '<Version>([^<]+)</Version>') {
        return $matches[1]
    }

    Write-Error "Could not find Version tag in $ProjectPath"
    exit 1
}

$currentVer = Get-ProjectVersion -ProjectPath $easyTerminalProj
$currentWinUIVer = Get-ProjectVersion -ProjectPath $easyTerminalWinUIProj
$currentTerminalVer = Get-ProjectVersion -ProjectPath $terminalWinUI3Proj

Write-Host "`nCurrent versions:" -ForegroundColor Yellow
Write-Host "- EasyWindowsTerminalControl: $currentVer"
Write-Host "- EasyWindowsTerminalControl.WinUI: $currentWinUIVer"
Write-Host "- CI.Microsoft.Terminal.WinUI3.Unofficial: $currentTerminalVer"

# Calculate new versions
function Get-IncrementedVersion {
    param (
        [string]$Version,
        [string]$IncrementType,
        [switch]$IsBeta
    )

    # Handle beta versioning
    if ($Version -match '([\d\.]+)-(beta)\.(\d+)') {
        $baseVer = $matches[1]
        $betaNum = [int]$matches[3]

        if ($IncrementType -eq "beta") {
            return "$baseVer-beta.$($betaNum + 1)"
        }

        $parts = $baseVer.Split('.')

        if ($IncrementType -eq "major") {
            $newMajor = [int]$parts[0] + 1
            return "$newMajor.0.0-beta.1"
        }
        elseif ($IncrementType -eq "minor") {
            $newMinor = [int]$parts[1] + 1
            return "$($parts[0]).$newMinor.0-beta.1"
        }
        elseif ($IncrementType -eq "patch") {
            $newPatch = [int]$parts[2] + 1
            return "$($parts[0]).$($parts[1]).$newPatch-beta.1"
        }
    }
    # Handle regular versioning
    elseif ($Version -match '([\d\.]+)') {
        $parts = $Version.Split('.')

        # Fill in missing parts (e.g., if version is just "1.0")
        while ($parts.Length -lt 3) {
            $parts += "0"
        }

        if ($IsBeta) {
            # Convert to beta format
            if ($IncrementType -eq "beta") {
                return "$Version-beta.1"
            }
        }

        if ($IncrementType -eq "major") {
            $newMajor = [int]$parts[0] + 1
            if ($IsBeta) {
                return "$newMajor.0.0-beta.1"
            } else {
                return "$newMajor.0.0"
            }
        }
        elseif ($IncrementType -eq "minor") {
            $newMinor = [int]$parts[1] + 1
            if ($IsBeta) {
                return "$($parts[0]).$newMinor.0-beta.1"
            } else {
                return "$($parts[0]).$newMinor.0"
            }
        }
        elseif ($IncrementType -eq "patch") {
            $newPatch = [int]$parts[2] + 1
            if ($IsBeta) {
                return "$($parts[0]).$($parts[1]).$newPatch-beta.1"
            } else {
                return "$($parts[0]).$($parts[1]).$newPatch"
            }
        }
        elseif ($IncrementType -eq "beta" -and $IsBeta) {
            return "$Version-beta.1"
        }
        elseif ($IncrementType -eq "beta") {
            # If we're incrementing beta on a non-beta version, just return the same version
            return $Version
        }

        return $Version
    }

    Write-Error "Invalid version format: $Version"
    exit 1
}

# Calculate new versions
$newVer = Get-IncrementedVersion -Version $currentVer -IncrementType $incrementType
$newWinUIVer = Get-IncrementedVersion -Version $currentWinUIVer -IncrementType $incrementType -IsBeta
$newTerminalVer = Get-IncrementedVersion -Version $currentTerminalVer -IncrementType $incrementType -IsBeta

Write-Host "`nNew versions:" -ForegroundColor Green
Write-Host "- EasyWindowsTerminalControl: $newVer"
Write-Host "- EasyWindowsTerminalControl.WinUI: $newWinUIVer"
Write-Host "- CI.Microsoft.Terminal.WinUI3.Unofficial: $newTerminalVer"

# Confirm before proceeding
if (-not $NoConfirm) {
    $confirmation = Read-Host "`nDo you want to update the version numbers? (Y/N)"
    if ($confirmation -ne 'Y' -and $confirmation -ne 'y') {
        Write-Host "Operation cancelled." -ForegroundColor Yellow
        exit 0
    }
}

Write-Host "`nUpdating version numbers..." -ForegroundColor Cyan

# Update versions in project files
function Update-ProjectVersion {
    param (
        [string]$ProjectPath,
        [string]$NewVersion
    )

    $content = Get-Content $ProjectPath -Raw
    $updatedContent = $content -replace '<Version>[^<]+</Version>', "<Version>$NewVersion</Version>"
    Set-Content -Path $ProjectPath -Value $updatedContent

    Write-Host "Updated $ProjectPath to version $NewVersion"
}

# Update dependency reference in WinUI project
function Update-DependencyVersion {
    param (
        [string]$ProjectPath,
        [string]$DependencyName,
        [string]$NewVersion
    )

    $content = Get-Content $ProjectPath -Raw
    $pattern = "<PackageReference Include=""$DependencyName"" Version=""[^""]+"""
    $replacement = "<PackageReference Include=""$DependencyName"" Version=""$NewVersion"""

    if ($content -match $pattern) {
        $updatedContent = $content -replace $pattern, $replacement
        Set-Content -Path $ProjectPath -Value $updatedContent
        Write-Host "Updated dependency $DependencyName in $ProjectPath to version $NewVersion"
        return $true
    }

    Write-Warning "Could not find dependency reference for $DependencyName in $ProjectPath"
    return $false
}

Update-ProjectVersion -ProjectPath $easyTerminalProj -NewVersion $newVer
Update-ProjectVersion -ProjectPath $easyTerminalWinUIProj -NewVersion $newWinUIVer
Update-ProjectVersion -ProjectPath $terminalWinUI3Proj -NewVersion $newTerminalVer

# Update dependency reference in WinUI project
Update-DependencyVersion -ProjectPath $easyTerminalWinUIProj -DependencyName "CI.Microsoft.Terminal.WinUI3.Unofficial" -NewVersion $newTerminalVer

# Update the two example apps
Update-DependencyVersion -ProjectPath $termExampleProj -DependencyName "EasyWindowsTerminalControl" -NewVersion $newVer

Update-DependencyVersion -ProjectPath $termExampleWinUIProj -DependencyName "EasyWindowsTerminalControl.WinUI" -NewVersion $newWinUIVer

Write-Host "`nVersion numbers have been updated successfully!" -ForegroundColor Green

# Build if requested
if ($Build) {
    Write-Host "`nBuilding solution with new versions..." -ForegroundColor Cyan

    # Execute the build script with the specified configuration
    $buildCmd = ".\build.ps1 -Configuration $Config"
    Invoke-Expression $buildCmd

    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: Failed to build solution" -ForegroundColor Red
        exit $LASTEXITCODE
    }

    Write-Host "`nSolution built successfully with new versions!" -ForegroundColor Green
}

Write-Host "`n===== Version increment process completed =====" -ForegroundColor Cyan
