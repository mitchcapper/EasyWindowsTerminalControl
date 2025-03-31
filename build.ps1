# Unified Build Script for EasyWindowsTerminalControl
# This script handles building both WinUI and non-WinUI projects with proper dependency management

[CmdletBinding()]
param(
    [Parameter()]
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",

    [Parameter()]
    [switch]$SkipClean,

	[Parameter()]
    [switch]$OnlyClean,


    [Parameter()]
    [switch]$SkipWinUI,

    [Parameter()]
    [switch]$SkipNuget
)

Set-StrictMode -version latest;


# Set error action preference to stop on any error
$ErrorActionPreference = "Stop"

function Invoke-CommandWithLogging {
	$Command = @()
	foreach ($arg in $args) {
		if ($arg -is [array]) {
			# If the argument is an array, add each element individually
			foreach ($item in $arg) {
				if ($null -ne $item) {
					$Command += "$item"
				} else {
					$Command += ""
				}
			}
		} else {
			# If the argument is not an array, add it directly
			if ($arg) {
				$Command += "$arg"
			} else {
				$Command += ""
			}
		}
	}
	$cmdStr = $Command -join ' '
	if ($PSCmdlet.MyInvocation.BoundParameters["Verbose"]) {
		Write-Host "Executing command: $cmdStr" -ForegroundColor DarkGray
	}
	$result = & $Command[0] $Command[1..($Command.Length-1)]
	if ($LASTEXITCODE -ne 0) {
		Write-Error $($result | Out-String)
        throw "failed with exit code $LASTEXITCODE for command $cmdStr build output above"
    }
	return $result
}

function Write-Header {
    param([string]$Message)
    Write-Host ""
    Write-Host "===== $Message =====" -ForegroundColor Cyan
}

function Clean-Project {
    param([string]$ProjectPath)

    if (Test-Path $ProjectPath) {
        $binPath = Join-Path $ProjectPath "bin"
        $objPath = Join-Path $ProjectPath "obj"

        if (Test-Path $binPath) {
            Write-Host "Cleaning $binPath" -ForegroundColor Gray
            Remove-Item -Path $binPath -Recurse -Force -ErrorAction SilentlyContinue
        }

        if (Test-Path $objPath) {
            Write-Host "Cleaning $objPath" -ForegroundColor Gray
            Remove-Item -Path $objPath -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

function Clean-PublishFolder {
    $publishDir = Join-Path $PSScriptRoot "publish"
    if (Test-Path $publishDir) {
        Write-Host "Cleaning publish folder: $publishDir" -ForegroundColor Gray
        Get-ChildItem -Path $publishDir -Filter "*.nupkg" | ForEach-Object {
            Write-Host "  Removing $($_.Name)" -ForegroundColor Gray
            Remove-Item $_.FullName -Force -ErrorAction SilentlyContinue
        }
    }
}

function Ensure-PublishDirectory {
    $publishDir = Join-Path $PSScriptRoot "publish"
    if (-not (Test-Path $publishDir)) {
        New-Item -Path $publishDir -ItemType Directory -Force | Out-Null
    }
    return $publishDir
}

function Build-Project {
    param(
        [string]$ProjectPath,
        [string]$Configuration,
        [hashtable]$AdditionalParams = @{}
    )

    Write-Host "Building $ProjectPath ($Configuration)" -ForegroundColor Yellow

    # Build the project
    $buildParams = @(
        $ProjectPath,
        "-c", $Configuration
    )

    # Add platform for WinUI projects
    if ($ProjectPath -match "WinUI") {
        $buildParams += "-p:Platform=x64"
    }

    foreach ($key in $AdditionalParams.Keys) {
        $buildParams += "-p:$key=$($AdditionalParams[$key])"
    }

    Invoke-CommandWithLogging dotnet build $buildParams


    Write-Host "Successfully built $ProjectPath" -ForegroundColor Green
    return $true
}

function Pack-Project {
    param(
        [string]$ProjectPath,
        [string]$Configuration,
        [hashtable]$AdditionalParams = @{}
    )

    Write-Host "Packaging $ProjectPath ($Configuration)" -ForegroundColor Yellow

    # Package the project
    $packParams = @(
        $ProjectPath,
        "-c", $Configuration,
        "--no-build"  # Skip building since we've already built the project
    )

    # Add platform for WinUI projects
    if ($ProjectPath -match "WinUI") {
        $packParams += "-p:Platform=x64"
    }

    foreach ($key in $AdditionalParams.Keys) {
        $packParams += "-p:$key=$($AdditionalParams[$key])"
    }

    Invoke-CommandWithLogging dotnet pack $packParams

    Write-Host "Successfully packaged $ProjectPath" -ForegroundColor Green
    return $true
}

function Get-ProjectVersion {
    param([string]$ProjectPath)

    $content = Get-Content $ProjectPath -Raw
    if ($content -match '<Version>([^<]+)</Version>') {
        return $matches[1]
    }
    Write-Warning "Could not find Version tag in $ProjectPath"
    return $null
}

function Find-NuGetPackage {
    param(
        [string]$Id,
        [string]$Version
    )

    # Search in both bin directories and publish directory
    $packagePattern = Join-Path $PSScriptRoot "*\bin\**\$Id.$Version.nupkg"
    $package = Get-ChildItem -Path $packagePattern -Recurse | Select-Object -First 1

    if (-not $package) {
        $publishDir = Join-Path $PSScriptRoot "publish"
        $publishPackage = Join-Path $publishDir "$Id.$Version.nupkg"
        if (Test-Path $publishPackage) {
            $package = Get-Item $publishPackage
        }
    }

    return $package
}


# Create publish directory if it doesn't exist
$publishDir = Ensure-PublishDirectory

# Define project paths
$solutionPath = Join-Path $PSScriptRoot "EasyWindowsTerminalControl.sln"
$easyTerminalControlPath = Join-Path $PSScriptRoot "EasyWindowsTerminalControl\EasyWindowsTerminalControl.csproj"
$termExamplePath = Join-Path $PSScriptRoot "TermExample\TermExample.csproj"
$microsoftTerminalWinUI3Path = Join-Path $PSScriptRoot "Microsoft.Terminal.WinUI3\Microsoft.Terminal.WinUI3.csproj"
$easyTerminalControlWinUIPath = Join-Path $PSScriptRoot "EasyWindowsTerminalControl.WinUI\EasyWindowsTerminalControl.WinUI.csproj"

# Define WinUI build parameters
$winuiParams = @{
    "GeneratePriEnabled" = "false"
    "MrtCoreEnabled" = "false"
    "WindowsAppSDKSelfContained" = "true"
    "EnableMsixTooling" = "false"
    "DisableMsixProjectCapabilityAddedByProject" = "true"
    "EnableMrtResourceGeneration" = "false"
    "EnableCoreMrtTooling" = "false"
	"GeneratePackageOnBuild" = "false"
}

# Start build process
Write-Header "Starting Build Process for Configuration: $Configuration"

# Step 1: Clean all projects if not skipped
if (-not $SkipClean) {
    Write-Header "Cleaning Projects"
    Clean-Project "EasyWindowsTerminalControl"
    Clean-Project "TermExample"
    Clean-Project "Microsoft.Terminal.WinUI3"
    Clean-Project "EasyWindowsTerminalControl.WinUI"
    Clean-Project "TermExample.WinUI"
    Clean-PublishFolder
	if ($OnlyClean) {
		Write-Host "Only cleaning requested. Exiting after clean." -ForegroundColor Yellow
		exit 0
	}
}

$publishDir = Ensure-PublishDirectory

if (-not $SkipWinUI) {
    # Step 3: Handle Microsoft.Terminal.WinUI3 project first
    Write-Header "Building Microsoft.Terminal.WinUI3"

    # Step 3.1: Try to build Microsoft.Terminal.WinUI3 directly
    Write-Host "Building Microsoft.Terminal.WinUI3 directly..." -ForegroundColor Yellow

    # Try build with a different approach to avoid file locking
    $buildParams = @(
        $microsoftTerminalWinUI3Path,
        "-c", $Configuration,
        "-p:Platform=x64"
    )
    foreach ($key in $winuiParams.Keys) {
        $buildParams += "-p:$key=$($winuiParams[$key])"
    }

    # Try dotnet build with skip restore first
    # Invoke-CommandWithLogging dotnet build $buildParams --no-restore

    #$terminalBuildSuccess = ($LASTEXITCODE -eq 0)

    Invoke-CommandWithLogging dotnet build $buildParams
    $terminalBuildSuccess = ($LASTEXITCODE -eq 0)


    # Step 3.3: Package Microsoft.Terminal.WinUI3
    if (-not $SkipNuget -and $terminalBuildSuccess) {
        $packParams = @(
            $microsoftTerminalWinUI3Path,
            "-c", $Configuration,
            "--no-build",  # Skip building since we've already built
            "-p:Platform=x64"
        )
        foreach ($key in $winuiParams.Keys) {
            $packParams += "-p:$key=$($winuiParams[$key])"
        }

        Write-Host "Packaging Microsoft.Terminal.WinUI3..." -ForegroundColor Yellow
		# Prepare Microsoft.Terminal.WinUI3 project
		$buildDir = Join-Path $PSScriptRoot "Microsoft.Terminal.WinUI3\bin\x64\$Configuration\net8.0-windows10.0.19041.0"
		$srcDir = Join-Path $PSScriptRoot "Microsoft.Terminal.WinUI3"
		$terminalOutputDir = Join-Path $buildDir "Microsoft.Terminal.WinUI3"
		if (-not (Test-Path $terminalOutputDir)) {
			New-Item -Path $terminalOutputDir -ItemType Directory -Force | Out-Null
			Write-Host "Created output directory: $terminalOutputDir" -ForegroundColor Green
		}
		# In the terminal these appear in the main build dir not the subdir like in VS
		Copy-Item (Join-Path $srcDir "TerminalControl.xaml") -Destination $terminalOutputDir -Force
		Copy-Item (Join-Path $buildDir "TerminalControl.xbf") -Destination $terminalOutputDir -Force

        Invoke-CommandWithLogging dotnet pack $packParams
            # Get the version of Microsoft.Terminal.WinUI3
            $terminalVersion = Get-ProjectVersion -ProjectPath $microsoftTerminalWinUI3Path
            if ($terminalVersion) {
                # Find the generated package
                $packageId = "CI.Microsoft.Terminal.WinUI3.Unofficial"
                $packageFile = Find-NuGetPackage -Id $packageId -Version $terminalVersion

                if (-not $packageFile) {
                    Write-Warning "Could not find the generated Microsoft.Terminal.WinUI3 package"
                }
            }

    }

    # Step 4: Restore EasyWindowsTerminalControl.WinUI with proper reference to Terminal package
    # Use the --force flag to ensure it finds the local package
    Write-Host "Restoring dependencies for EasyWindowsTerminalControl.WinUI..." -ForegroundColor Yellow
    Invoke-CommandWithLogging dotnet restore $easyTerminalControlWinUIPath --force
	Build-Project -ProjectPath $easyTerminalControlWinUIPath -Configuration $Configuration -AdditionalParams $winuiParams
	Pack-Project -ProjectPath $easyTerminalControlWinUIPath -Configuration $Configuration -AdditionalParams $winuiParams
}

# Step 7: Build non-WinUI projects
Write-Header "Building Non-WinUI Projects"

# Restore dependencies for non-WinUI projects
Write-Host "Restoring dependencies for non-WinUI projects..." -ForegroundColor Yellow
Invoke-CommandWithLogging dotnet restore $easyTerminalControlPath

# Build EasyWindowsTerminalControl
$easyControlBuildSuccess = Build-Project -ProjectPath $easyTerminalControlPath -Configuration $Configuration

# Build TermExample
$termExampleBuildSuccess = Build-Project -ProjectPath $termExamplePath -Configuration $Configuration

# Step 8: Package non-WinUI projects
if (-not $SkipNuget) {
    Write-Header "Packaging Non-WinUI Projects"

    if ($easyControlBuildSuccess) {
        $easyControlPackSuccess = Pack-Project -ProjectPath $easyTerminalControlPath -Configuration $Configuration

        if ($easyControlPackSuccess) {
            # Copy the generated package to the publish directory
            $easyControlVersion = Get-ProjectVersion -ProjectPath $easyTerminalControlPath
            if ($easyControlVersion) {
                $easyControlPackageFile = Find-NuGetPackage -Id "EasyWindowsTerminalControl" -Version $easyControlVersion

                if (-not $easyControlPackageFile) {
                    Write-Warning "Could not find the generated EasyWindowsTerminalControl package"
                }
            }
        }
    }
}

# Step 9: Collect all NuGet packages
Write-Header "Collecting NuGet Packages"

# # Find all .nupkg files that haven't been copied to publish yet
# $packages = Get-ChildItem -Path $PSScriptRoot -Filter "*.nupkg" -Recurse |
#     Where-Object { $_.DirectoryName -notmatch "publish" }

# if (-not $packages -or $packages.Count -eq 0) {
#     Write-Host "No additional NuGet packages found" -ForegroundColor Yellow
# } else {
#     # Copy packages to publish directory
#     foreach ($package in $packages) {
#         $destPath = Join-Path $publishDir $package.Name
#         if (-not (Test-Path $destPath)) {
#             Write-Host "Copying package: $($package.FullName) -> $destPath" -ForegroundColor Green
#             Copy-Item -Path $package.FullName -Destination $destPath -Force
#         }
#     }
# }

# Complete
Write-Header "Build Process Completed"
Write-Host "Build configuration: $Configuration"
Write-Host "NuGet packages location: $publishDir"
Write-Host ""
Write-Host "To build with different options, try:" -ForegroundColor Gray
Write-Host "  .\build.ps1 -Configuration Release" -ForegroundColor Gray
Write-Host "  .\build.ps1 -SkipWinUI" -ForegroundColor Gray
Write-Host "  .\build.ps1 -SkipNuget" -ForegroundColor Gray
Write-Host "  .\build.ps1 -SkipClean" -ForegroundColor Gray
