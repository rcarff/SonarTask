# ============================================================
# SonarTaskUnity Release Builder
#
# Run the following command if ' running scripts is disabled on this system'
# Set-ExecutionPolicy -Scope Process Bypass
#
# Pipeline:
#   1. Locate SignTool
#   2. Locate Azure Artifact Signing dlib
#   3. Check Azure authentication
#   4. Sign Unity EXE
#   5. Verify Unity EXE
#   6. Build MSI with WiX
#   7. Sign MSI
#   8. Verify MSI
# ============================================================

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"


# ============================================================
# CONFIGURATION
# ============================================================

$ProjectRoot = ".."

# Unity Windows build folder
$UnityBuildDir = Join-Path $ProjectRoot "Build\Windows"

# Folder containing your .wixproj and .wxs files
$InstallerDir = Join-Path $ProjectRoot "Build\WindowsInstaller"

# Artifact Signing metadata file.
#
# Recommended location:
# .\metadata.json
#
$MetadataPath = Join-Path $ProjectRoot "Build\metadata.json"

# Artifact Signing timestamp server
$TimestampServer = "http://timestamp.acs.microsoft.com"


# ============================================================
# HELPER FUNCTIONS
# ============================================================

function Write-Step {
    param([string]$Message)

    Write-Host ""
    Write-Host "============================================================"
    Write-Host $Message
    Write-Host "============================================================"
}


function Resolve-SignTool {

    Write-Step "Locating x64 SignTool"

    $WindowsKitsBin = Join-Path `
        ${env:ProgramFiles(x86)} `
        "Windows Kits\10\bin"

    if (-not (Test-Path $WindowsKitsBin)) {
        throw "Windows SDK bin directory not found: $WindowsKitsBin"
    }

    # Find SDK version directories and sort them numerically
    $VersionDirectories = Get-ChildItem `
        $WindowsKitsBin `
        -Directory |
        Where-Object {
            $_.Name -match '^\d+\.\d+\.\d+\.\d+$'
        } |
        Sort-Object {
            [version]$_.Name
        } -Descending

    foreach ($VersionDirectory in $VersionDirectories) {

        $Candidate = Join-Path `
            $VersionDirectory.FullName `
            "x64\signtool.exe"

        if (Test-Path $Candidate) {

            Write-Host "Found SignTool:"
            Write-Host "  $Candidate"

            return $Candidate
        }
    }

    throw "Could not locate an x64 signtool.exe."
}


function Resolve-ArtifactSigningDlib {

    Write-Step "Locating Azure Artifact Signing dlib"

    # --------------------------------------------------------
    # First check Microsoft's MSI installation location
    # --------------------------------------------------------

    $OfficialPath = `
        "C:\Program Files (x86)\Microsoft\ArtifactSigningClientTools\bin\Azure.CodeSigning.Dlib.dll"

    if (Test-Path $OfficialPath) {

        Write-Host "Found Artifact Signing dlib:"
        Write-Host "  $OfficialPath"

        return $OfficialPath
    }


    # --------------------------------------------------------
    # Then check our manual NuGet installation
    # --------------------------------------------------------

    $SearchRoots = @(
        "C:\Tools\ArtifactSigning",
        "${env:ProgramFiles(x86)}\Microsoft",
        "$env:ProgramFiles\Microsoft"
    )


    foreach ($Root in $SearchRoots) {

        if (-not (Test-Path $Root)) {
            continue
        }

        # Prefer explicitly x64 versions.
        $Candidates = @(
            Get-ChildItem `
                $Root `
                -Recurse `
                -File `
                -Filter "Azure.CodeSigning.Dlib.dll" `
                -ErrorAction SilentlyContinue |
            Where-Object {
                $_.FullName -match '\\x64\\'
            }
        )

        if ($Candidates.Count -gt 0) {

            $Selected = $Candidates |
                Select-Object -First 1

            Write-Host "Found Artifact Signing dlib:"
            Write-Host "  $($Selected.FullName)"

            return $Selected.FullName
        }
    }

    throw @"
Could not locate Azure.CodeSigning.Dlib.dll.

Expected either:

C:\Program Files (x86)\Microsoft\ArtifactSigningClientTools\bin\Azure.CodeSigning.Dlib.dll

or an x64 NuGet copy beneath:

C:\Tools\ArtifactSigning
"@
}


function Invoke-SignFile {

    param(
        [Parameter(Mandatory)]
        [string]$FilePath
    )

    if (-not (Test-Path $FilePath)) {
        throw "Cannot sign file because it does not exist: $FilePath"
    }

    Write-Step "Signing: $FilePath"

    & $signtool sign `
        /v `
        /debug `
        /fd SHA256 `
        /tr $TimestampServer `
        /td SHA256 `
        /dlib $dlib `
        /dmdf $MetadataPath `
        $FilePath

    if ($LASTEXITCODE -ne 0) {
        throw "SignTool failed while signing: $FilePath"
    }

    Write-Host ""
    Write-Host "Signing succeeded."
}


function Invoke-VerifySignature {

    param(
        [Parameter(Mandatory)]
        [string]$FilePath
    )

    Write-Step "Verifying signature: $FilePath"

    & $signtool verify `
        /pa `
        /v `
        $FilePath

    if ($LASTEXITCODE -ne 0) {
        throw "Signature verification FAILED: $FilePath"
    }

    Write-Host ""
    Write-Host "Signature verified successfully."
}


# ============================================================
# START RELEASE PROCESS
# ============================================================

Write-Step "Starting SonarTaskUnity release build"


# ============================================================
# 1. LOCATE SIGNING TOOLS
# ============================================================

$signtool = Resolve-SignTool

$dlib = Resolve-ArtifactSigningDlib

Write-Host ""
Write-Host "Signing tools:"
Write-Host ""
Write-Host "SignTool:"
Write-Host "  $signtool"
Write-Host ""
Write-Host "Artifact Signing dlib:"
Write-Host "  $dlib"


# ============================================================
# 2. VERIFY METADATA.JSON
# ============================================================

Write-Step "Checking Artifact Signing metadata"

if (-not (Test-Path $MetadataPath)) {

    Write-Host "metadata.json was not found at:"
    Write-Host "  $MetadataPath"
    Write-Host ""
    Write-Host "Searching project directory..."

    $MetadataCandidates = @(
        Get-ChildItem `
            $ProjectRoot `
            -Recurse `
            -File `
            -Filter "metadata.json" `
            -ErrorAction SilentlyContinue
    )

    if ($MetadataCandidates.Count -eq 1) {

        $MetadataPath = $MetadataCandidates[0].FullName

        Write-Host ""
        Write-Host "Found metadata.json:"
        Write-Host "  $MetadataPath"
    }
    elseif ($MetadataCandidates.Count -gt 1) {

        Write-Host ""
        Write-Host "Multiple metadata.json files were found:"

        foreach ($Candidate in $MetadataCandidates) {
            Write-Host "  $($Candidate.FullName)"
        }

        throw "Specify the correct MetadataPath near the top of the script."
    }
    else {
        throw "metadata.json could not be found."
    }
}


# Validate the required JSON fields.

$Metadata = Get-Content `
    $MetadataPath `
    -Raw |
    ConvertFrom-Json

$RequiredMetadataProperties = @(
    "Endpoint",
    "CodeSigningAccountName",
    "CertificateProfileName"
)

foreach ($Property in $RequiredMetadataProperties) {

    if ($Metadata.PSObject.Properties.Name -notcontains $Property) {

        throw "metadata.json is missing required property: $Property"
    }

    if ([string]::IsNullOrWhiteSpace($Metadata.$Property)) {

        throw "metadata.json property '$Property' is empty."
    }
}


Write-Host ""
Write-Host "Artifact Signing configuration:"
Write-Host ""
Write-Host "Endpoint:"
Write-Host "  $($Metadata.Endpoint)"
Write-Host ""
Write-Host "Account:"
Write-Host "  $($Metadata.CodeSigningAccountName)"
Write-Host ""
Write-Host "Certificate profile:"
Write-Host "  $($Metadata.CertificateProfileName)"


# ============================================================
# 3. CHECK AZURE CLI
# ============================================================

Write-Step "Checking Azure CLI authentication"

if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    throw "Azure CLI ('az') is not installed or is not available in PATH."
}

& az account show `
    --only-show-errors `
    --output none

if ($LASTEXITCODE -ne 0) {

    throw @"
Azure CLI is not currently authenticated.

Run:

    az login

Then run this release script again.
"@
}

Write-Host "Azure CLI authentication is available."


# ============================================================
# 4. FIND UNITY APPLICATION EXE
# ============================================================

Write-Step "Locating Unity executable"

if (-not (Test-Path $UnityBuildDir)) {
    throw "Unity build directory does not exist: $UnityBuildDir"
}


# Ignore Unity's crash handler because we don't want to
# accidentally treat it as the primary application.

$UnityExeCandidates = @(
    Get-ChildItem `
        $UnityBuildDir `
        -File `
        -Filter "*.exe" |
    Where-Object {
        $_.Name -notin @(
            "UnityCrashHandler64.exe",
            "UnityCrashHandler32.exe"
        )
    }
)


if ($UnityExeCandidates.Count -eq 0) {

    throw @"
No Unity application executable was found in:

$UnityBuildDir
"@
}


if ($UnityExeCandidates.Count -gt 1) {

    Write-Host "Multiple executable candidates were found:"
    Write-Host ""

    foreach ($Candidate in $UnityExeCandidates) {
        Write-Host "  $($Candidate.FullName)"
    }

    throw @"
More than one possible application executable exists.

Set `$UnityExePath explicitly in the script instead of auto-detecting it.
"@
}


$UnityExePath = $UnityExeCandidates[0].FullName

Write-Host "Unity application:"
Write-Host "  $UnityExePath"


# ============================================================
# 5. SIGN UNITY EXE
# ============================================================

Invoke-SignFile $UnityExePath


# ============================================================
# 6. VERIFY UNITY EXE
# ============================================================

Invoke-VerifySignature $UnityExePath


# ============================================================
# 7. FIND WIX PROJECT
# ============================================================

Write-Step "Locating WiX installer project"

if (-not (Test-Path $InstallerDir)) {

    throw @"
Installer directory does not exist:

$InstallerDir

Create the WiX installer project before running this script.
"@
}


$WixProjects = @(
    Get-ChildItem `
        $InstallerDir `
        -File `
        -Filter "*.wixproj"
)


if ($WixProjects.Count -eq 0) {
    throw "No .wixproj file was found in: $InstallerDir"
}


if ($WixProjects.Count -gt 1) {

    Write-Host "Multiple WiX projects found:"
    Write-Host ""

    foreach ($Project in $WixProjects) {
        Write-Host "  $($Project.FullName)"
    }

    throw "Multiple .wixproj files exist. Specify the desired project explicitly."
}


$WixProjectPath = $WixProjects[0].FullName

Write-Host "WiX project:"
Write-Host "  $WixProjectPath"


# ============================================================
# 8. CHECK .NET
# ============================================================

Write-Step "Checking .NET SDK"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET SDK was not found. Install the .NET SDK before building WiX."
}

& dotnet --version

if ($LASTEXITCODE -ne 0) {
    throw "dotnet is installed but could not be executed."
}


# ============================================================
# 9. BUILD MSI
# ============================================================

Write-Step "Building MSI with WiX"

$BuildStartTime = Get-Date


Push-Location $InstallerDir

try {

    & dotnet build `
        $WixProjectPath `
        --configuration Release

    if ($LASTEXITCODE -ne 0) {
        throw "WiX/MSBuild failed to generate the MSI."
    }

}
finally {

    Pop-Location
}


# ============================================================
# 10. FIND NEWLY GENERATED MSI
# ============================================================

Write-Step "Locating generated MSI"

$InstallerOutputDirectory = Join-Path `
    (Split-Path $WixProjectPath -Parent) `
    "bin"


if (-not (Test-Path $InstallerOutputDirectory)) {
    throw "WiX build completed but no bin directory was found."
}


# Look for an MSI modified during this build.
# The ten-second allowance avoids filesystem timestamp
# resolution annoyances.

$MsiCandidates = @(
    Get-ChildItem `
        $InstallerOutputDirectory `
        -Recurse `
        -File `
        -Filter "*.msi" |
    Where-Object {
        $_.LastWriteTime -ge $BuildStartTime.AddSeconds(-10)
    } |
    Sort-Object LastWriteTime -Descending
)


if ($MsiCandidates.Count -eq 0) {

    throw @"
WiX build completed, but a newly generated MSI could not be located beneath:

$InstallerOutputDirectory
"@
}


$MsiPath = $MsiCandidates[0].FullName

Write-Host "Generated MSI:"
Write-Host "  $MsiPath"


# ============================================================
# 11. SIGN MSI
# ============================================================

Invoke-SignFile $MsiPath


# ============================================================
# 12. VERIFY MSI
# ============================================================

Invoke-VerifySignature $MsiPath


# ============================================================
# COMPLETE
# ============================================================

Write-Step "RELEASE BUILD COMPLETED SUCCESSFULLY"

Write-Host ""
Write-Host "Signed Unity executable:"
Write-Host "  $UnityExePath"
Write-Host ""
Write-Host "Signed MSI installer:"
Write-Host "  $MsiPath"
Write-Host ""
Write-Host "SignTool:"
Write-Host "  $signtool"
Write-Host ""
Write-Host "Artifact Signing dlib:"
Write-Host "  $dlib"
Write-Host ""