# ============================================================================
#  Update-SiglusSsu.ps1  --  install or update the machine-wide siglus-ssu
#
#  The new tool compiles and extracts with the GLOBAL siglus-ssu (a pip --user
#  install), never the fork. This script assumes the machine has NOTHING set
#  up and gets it there:
#
#      1. asks PyPI which Python versions the newest siglus-ssu accepts
#         (its requires_python, e.g. ">=3.12") - never a version we assumed
#      2. lists the Pythons installed through the 'py' launcher and picks the
#         newest one that satisfies that requirement
#      3. if none does, installs the MINIMUM version the package names,
#         through winget, launcher included
#      4. installs siglus-ssu when it is missing, upgrades it when present
#      5. refreshes const.py when the release still needs it (0.4.x had an
#         'init' mode for that; 0.5.4 onward ships const.py in the package)
#      6. shows the version that is now on disk
#
#  Why not pin 3.13: the maintainer has dropped Python versions before. The
#  only durable statement of what works is the one the package itself makes.
#
#  Nothing here touches the fork in tools\SiglusSceneScriptUtility or any
#  game file. After updating, prove the build still works with a compile
#  before trusting the new version.
#
#  Keep this file pure ASCII. Windows PowerShell 5.1 misreads a UTF-8 source
#  file that has no BOM.
# ============================================================================

$ErrorActionPreference = 'Stop'

# ---- settings, all set here -------------------------------------------------

$PackageName    = 'siglus-ssu'
$ExeName        = 'siglus-ssu.exe'
$PythonLauncher = 'py'
$PyPiUrl        = 'https://pypi.org/pypi/' + $PackageName + '/json'
$WingetIdPrefix = 'Python.Python.'


# ---- helpers ----------------------------------------------------------------

# One clause of a requires_python string, e.g. ">=3.12" or "<4". Returns true
# when the given version meets it. Unknown operators are treated as met and
# reported, rather than silently refusing every Python.
function Test-VersionClause {
	param(
		[version] $Candidate,
		[string]  $Clause
	)

	$text = $Clause.Trim()
	$operator = ''
	$number = ''

	foreach ($candidateOperator in @('>=', '<=', '!=', '==', '~=', '>', '<')) {
		if ($operator -eq '' -and $text.StartsWith($candidateOperator)) {
			$operator = $candidateOperator
			$number = $text.Substring($candidateOperator.Length).Trim()
		}
	}

	if ($operator -eq '') {
		Write-Host ("  (requires_python clause not understood, assuming it is met: " + $text + ")") -ForegroundColor Yellow
		return $true
	}

	# PyPI writes "3.12"; [version] needs at least major.minor, which it has.
	# A bare "4" (as in "<4") needs the ".0" added.
	if ($number.Contains('.') -eq $false) {
		$number = $number + '.0'
	}
	$bound = [version] $number

	# Compare on major.minor only; patch levels are not part of the question.
	$candidateShort = [version] ($Candidate.Major.ToString() + '.' + $Candidate.Minor.ToString())
	$boundShort = [version] ($bound.Major.ToString() + '.' + $bound.Minor.ToString())

	$met = $true
	if ($operator -eq '>=') { $met = $candidateShort -ge $boundShort }
	if ($operator -eq '>')  { $met = $candidateShort -gt $boundShort }
	if ($operator -eq '<=') { $met = $candidateShort -le $boundShort }
	if ($operator -eq '<')  { $met = $candidateShort -lt $boundShort }
	if ($operator -eq '!=') { $met = $candidateShort -ne $boundShort }
	if ($operator -eq '==') { $met = $candidateShort -eq $boundShort }
	if ($operator -eq '~=') { $met = ($candidateShort -ge $boundShort) -and ($candidateShort.Major -eq $boundShort.Major) }
	return $met
}

# True when a Python version meets every clause of a requires_python string.
function Test-VersionSatisfies {
	param(
		[version] $Candidate,
		[string]  $Requirement
	)

	$allMet = $true
	foreach ($clause in ($Requirement -split ',')) {
		if ($clause.Trim() -ne '') {
			if ((Test-VersionClause -Candidate $Candidate -Clause $clause) -eq $false) {
				$allMet = $false
			}
		}
	}
	return $allMet
}

# The lowest major.minor a requires_python string allows, from its ">=" or ">"
# clause. '' when it names no lower bound.
function Get-MinimumVersion {
	param([string] $Requirement)

	$minimum = ''
	foreach ($clause in ($Requirement -split ',')) {
		$text = $clause.Trim()
		if ($text.StartsWith('>=')) {
			$minimum = $text.Substring(2).Trim()
		}
		if ($text.StartsWith('>') -and $text.StartsWith('>=') -eq $false) {
			$above = [version] ($text.Substring(1).Trim())
			$minimum = $above.Major.ToString() + '.' + ($above.Minor + 1).ToString()
		}
	}
	return $minimum
}

# The Pythons the launcher knows about, newest first, as [version] objects.
# 'py -0p' prints one per line, e.g. " -V:3.13 *        C:\...\python.exe".
function Get-InstalledPythons {
	$found = @()
	if ($null -ne (Get-Command $PythonLauncher -ErrorAction SilentlyContinue)) {
		$lines = @(& $PythonLauncher -0p 2>&1 | Out-String) -split "`r?`n"
		foreach ($line in $lines) {
			$text = $line.Trim()
			if ($text.StartsWith('-V:')) {
				$tag = ($text.Substring(3) -split ' ')[0]
				$tag = $tag.TrimEnd('*').Trim()
				$found += [version] $tag
			}
		}
	}
	return @($found | Sort-Object -Descending)
}


# ---- 1. what does the package require? --------------------------------------

Write-Host 'Package:'

$requirement = ''
$newestRelease = ''
try {
	$info = Invoke-RestMethod -Uri $PyPiUrl -UseBasicParsing
	$requirement = [string] $info.info.requires_python
	$newestRelease = [string] $info.info.version
}
catch {
	throw ("could not reach PyPI at " + $PyPiUrl + " - " + $_.Exception.Message + ". pip would fail too; check the connection and rerun")
}

if ($requirement -eq '') {
	throw ($PackageName + ' ' + $newestRelease + ' declares no requires_python - cannot choose a Python version safely')
}

Write-Host ("  {0,-16} {1}" -f 'newest release', $newestRelease)
Write-Host ("  {0,-16} {1}" -f 'needs Python', $requirement)


# ---- 2. which installed Python satisfies it? --------------------------------

Write-Host ''
Write-Host 'Installed Pythons:'

$installed = Get-InstalledPythons
$chosen = $null

if ($installed.Count -eq 0) {
	Write-Host '  none found through the py launcher'
}
foreach ($candidate in $installed) {
	$ok = Test-VersionSatisfies -Candidate $candidate -Requirement $requirement
	$mark = 'too old or excluded'
	if ($ok -eq $true) { $mark = 'satisfies' }
	Write-Host ("  {0,-8} {1}" -f $candidate.ToString(), $mark)
	if ($ok -eq $true -and $null -eq $chosen) {
		$chosen = $candidate
	}
}


# ---- 3. install the minimum version when nothing satisfies -------------------

if ($null -eq $chosen) {
	$minimum = Get-MinimumVersion -Requirement $requirement
	if ($minimum -eq '') {
		throw ('no installed Python satisfies "' + $requirement + '" and it names no lower bound to install - install a Python that meets it and rerun')
	}

	$wingetId = $WingetIdPrefix + $minimum
	Write-Host ''
	Write-Host ('No installed Python satisfies "' + $requirement + '". Installing the minimum it names: Python ' + $minimum) -ForegroundColor Yellow

	if ($null -eq (Get-Command 'winget' -ErrorAction SilentlyContinue)) {
		throw ('winget is not available - install Python ' + $minimum + ' from https://www.python.org/downloads/ (keep the "py launcher" option ticked), then rerun')
	}

	Write-Host (">> winget install --id " + $wingetId + " --exact --source winget --accept-package-agreements --accept-source-agreements") -ForegroundColor Cyan
	& winget install --id $wingetId --exact --source winget --accept-package-agreements --accept-source-agreements
	if ($LASTEXITCODE -ne 0) {
		throw ('winget exited with code ' + $LASTEXITCODE + ' - install Python ' + $minimum + ' from python.org and rerun')
	}

	# A fresh install registers its launcher entry on the system PATH, which
	# this process does not see yet. Re-read PATH, then look again.
	$env:Path = [System.Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [System.Environment]::GetEnvironmentVariable('Path', 'User')

	foreach ($candidate in (Get-InstalledPythons)) {
		if ($null -eq $chosen -and (Test-VersionSatisfies -Candidate $candidate -Requirement $requirement) -eq $true) {
			$chosen = $candidate
		}
	}
	if ($null -eq $chosen) {
		throw ('Python ' + $minimum + ' was installed but the py launcher does not list it yet - open a new window and rerun')
	}
}

$pythonVersion = '-' + $chosen.Major.ToString() + '.' + $chosen.Minor.ToString()

Write-Host ''
Write-Host ('Using Python ' + $chosen.ToString() + '  (' + $PythonLauncher + ' ' + $pythonVersion + ')') -ForegroundColor Green


# ---- where will the exe land? ------------------------------------------------
#  The per-user scripts folder differs per user and per machine; ask Python.

$scriptsDir = (& $PythonLauncher $pythonVersion -c "import sysconfig; print(sysconfig.get_path('scripts', 'nt_user'))" 2>&1 | Out-String).Trim()
if ($scriptsDir -eq '') {
	throw 'could not ask Python for its --user scripts folder'
}
$GlobalExe = Join-Path $scriptsDir $ExeName
Write-Host ("  {0,-10} {1}" -f 'exe path', $GlobalExe)


# ---- before -----------------------------------------------------------------

Write-Host ''
Write-Host 'Before:'

$alreadyInstalled = Test-Path -LiteralPath $GlobalExe -PathType Leaf
$installedBefore = '(not installed)'
if ($alreadyInstalled -eq $true) {
	$installedBefore = (& $GlobalExe --version 2>&1 | Out-String).Trim()
}
Write-Host ("  {0,-10} {1}" -f 'installed', $installedBefore)


# ---- 4. install or upgrade ---------------------------------------------------
#  A plain install always fetches the newest release; --upgrade does the same
#  for one that is already there. Shown separately so the log says which.

$pipVerb = 'install --user --upgrade'
if ($alreadyInstalled -eq $false) {
	$pipVerb = 'install --user'
}

Write-Host ''
Write-Host (">> " + $PythonLauncher + " " + $pythonVersion + " -m pip " + $pipVerb + " " + $PackageName) -ForegroundColor Cyan

$pipArguments = @('-m', 'pip') + ($pipVerb -split ' ') + @($PackageName)
& $PythonLauncher $pythonVersion @pipArguments
if ($LASTEXITCODE -ne 0) {
	throw ('pip exited with code ' + $LASTEXITCODE + ' - nothing else was done')
}

if ((Test-Path -LiteralPath $GlobalExe -PathType Leaf) -eq $false) {
	throw ('pip finished but ' + $GlobalExe + ' did not appear - check the output above')
}


# ---- 5. refresh const.py, only on releases that need it ----------------------
#  Through 0.4.x, 'init' downloaded the const.py for the installed release and
#  the const profiles we build with (--const-profile 3) lived in it. From 0.5.4
#  const.py ships inside the package and 'init' is gone. Ask the installed exe
#  which kind it is rather than assuming either.

Write-Host ''
$helpText = (& $GlobalExe --help 2>&1 | Out-String)
$hasInit = $helpText.Contains('|init|') -or $helpText.Contains('siglus-ssu init')

if ($hasInit -eq $true) {
	Write-Host '>> siglus-ssu init --force' -ForegroundColor Cyan
	& $GlobalExe init --force
	if ($LASTEXITCODE -ne 0) {
		throw ('siglus-ssu init exited with code ' + $LASTEXITCODE + ' - the package installed but const.py was NOT refreshed')
	}
}
if ($hasInit -eq $false) {
	Write-Host 'const.py ships inside this release - no init step needed' -ForegroundColor DarkGray
}


# ---- 6. after ----------------------------------------------------------------

$installedAfter = (& $GlobalExe --version 2>&1 | Out-String).Trim()

Write-Host ''
Write-Host 'After:'
Write-Host ("  {0,-10} {1}" -f 'was', $installedBefore)
Write-Host ("  {0,-10} {1}" -f 'now', $installedAfter)
Write-Host ("  {0,-10} {1}" -f 'python', $chosen.ToString())

if ($installedAfter -eq $installedBefore) {
	Write-Host '  version unchanged - already current, or pip found nothing newer' -ForegroundColor Yellow
}
if ($installedAfter -ne $installedBefore) {
	Write-Host '  installed. Prove it with a build before relying on it.' -ForegroundColor Green
}
