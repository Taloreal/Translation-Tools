# ============================================================================
#  Find-SiglusProfile.ps1  --  work out which build mode a Scene.pck needs
#
#  siglus-ssu compiles every SiglusEngine title the same way except for a
#  handful that need a special mode ("const profile"). Nothing in an archive
#  announces which one it needs, and the wrong one compiles cleanly and then
#  fails in the game. The compiler's 'test' mode, though, extracts an archive,
#  rebuilds it, and reports whether the rebuild matches the original - so the
#  mode that round-trips is the mode the archive was built with.
#
#  Given no mode, 'test' tries every mode itself during the rebuild. This
#  script runs that one test, keeps every line it prints, and reports the mode
#  the compiler settled on - by game name, not number. The archive is only
#  read; the compiler works in its own temp area and cleans up.
#
#  Usage: drop a Scene.pck onto Find-SiglusProfile.bat, or run the .bat and
#  type the path when asked. Every line of compiler output goes to a log file
#  next to this script, one per run.
#
#  Keep this file pure ASCII. Windows PowerShell 5.1 misreads a UTF-8 source
#  file that has no BOM.
# ============================================================================

param(
	[Parameter(Position = 0)] [string] $Archive = ''
)

$ErrorActionPreference = 'Stop'

# ---- settings, all set here -------------------------------------------------

$PackageName    = 'siglus-ssu'
$ExeName        = 'siglus-ssu.exe'
$PythonLauncher = 'py'
$LogFolder      = $PSScriptRoot


# ---- the archive to test ----------------------------------------------------

if ($Archive -eq '') {
	$Archive = Read-Host 'Path to the Scene.pck to test'
}
$Archive = $Archive.Trim('"').Trim()

if ((Test-Path -LiteralPath $Archive -PathType Leaf) -eq $false) {
	throw ('archive not found: ' + $Archive)
}


# ---- find the compiler -----------------------------------------------------
#  Same resolution as Update-SiglusSsu.ps1: the newest installed Python that
#  carries a siglus-ssu.exe in its --user scripts folder.

if ($null -eq (Get-Command $PythonLauncher -ErrorAction SilentlyContinue)) {
	throw 'the Python launcher is not installed - run Update-SiglusSsu.bat first'
}

$GlobalExe = ''
$launcherLines = @(& $PythonLauncher -0p 2>&1 | Out-String) -split "`r?`n"
foreach ($line in $launcherLines) {
	$text = $line.Trim()
	if ($GlobalExe -eq '' -and $text.StartsWith('-V:')) {
		$tag = ($text.Substring(3) -split ' ')[0]
		$tag = $tag.TrimEnd('*').Trim()
		$scriptsDir = (& $PythonLauncher ('-' + $tag) -c "import sysconfig; print(sysconfig.get_path('scripts', 'nt_user'))" 2>&1 | Out-String).Trim()
		$candidate = Join-Path $scriptsDir $ExeName
		if ((Test-Path -LiteralPath $candidate -PathType Leaf) -eq $true) {
			$GlobalExe = $candidate
		}
	}
}

if ($GlobalExe -eq '') {
	throw ($PackageName + ' is not installed - run Update-SiglusSsu.bat first')
}

$version = (& $GlobalExe --version 2>&1 | Out-String).Trim()


# ---- which modes does this compiler offer? -----------------------------------
#  The help line reads, in one piece:
#    --const-profile Select const profile (0, 1, 2, 3; default: 0; 3 supports X; 4 supports Y; ...)
#  Split it on ';' - the first piece lists the numbers, the later pieces each
#  name a game. Numbers are kept for the command line; the user sees names.

$helpLines = @(& $GlobalExe --help 2>&1 | Out-String) -split "`r?`n"
$profileLine = ''
foreach ($line in $helpLines) {
	if ($profileLine -eq '' -and $line.Trim().StartsWith('--const-profile')) {
		$profileLine = $line
	}
}
if ($profileLine -eq '') {
	throw 'could not find the --const-profile line in siglus-ssu --help; the compiler may have changed'
}

$open = $profileLine.IndexOf('(')
$close = $profileLine.LastIndexOf(')')
$inside = $profileLine.Substring($open + 1, $close - $open - 1)
$pieces = $inside -split ';'

$profiles = @()
foreach ($token in ($pieces[0] -split ',')) {
	$number = $token.Trim()
	if ($number -ne '') {
		$profiles += $number
	}
}

$names = @{}
foreach ($number in $profiles) {
	$names[$number] = 'standard'
}
foreach ($piece in $pieces) {
	$text = $piece.Trim()
	$marker = ' supports '
	$at = $text.IndexOf($marker)
	if ($at -gt 0) {
		$number = $text.Substring(0, $at).Trim()
		$game = $text.Substring($at + $marker.Length).Trim()
		if ($names.ContainsKey($number)) {
			$names[$number] = $game
		}
	}
}


# ---- run the compiler's own search ------------------------------------------

$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$logPath = Join-Path $LogFolder ('Find-SiglusProfile-' + $stamp + '.log')

Write-Host ('Compiler: ' + $version)
Write-Host ('Archive:  ' + $Archive)
Write-Host ('Log:      ' + $logPath)
Write-Host ''
Write-Host 'Letting the compiler extract, rebuild and compare the archive, trying its build modes in turn. This takes a while.'
Write-Host ''

Add-Content -LiteralPath $logPath -Value ('Find-SiglusProfile  ' + (Get-Date).ToString('s'))
Add-Content -LiteralPath $logPath -Value ('compiler: ' + $version)
Add-Content -LiteralPath $logPath -Value ('archive:  ' + $Archive)
Add-Content -LiteralPath $logPath -Value ''
Add-Content -LiteralPath $logPath -Value '===== siglus-ssu test ====='

$output = @(& $GlobalExe test $Archive 2>&1 | ForEach-Object { $_.ToString() })
$exitCode = $LASTEXITCODE

Add-Content -LiteralPath $logPath -Value $output
Add-Content -LiteralPath $logPath -Value ('===== exit code ' + $exitCode + ' =====')


# ---- read what it said ------------------------------------------------------
#  The compiler ends with one summary line of this shape:
#      result: PAYLOAD_SAME profile=3 same=155 text_only=0 real_diff=0 unavailable=0
#  The word after 'result:' is the verdict; the 'profile=' token is the mode
#  it settled on. Anything else is in the log.

$verdict = 'no verdict printed'
$foundNumber = ''
foreach ($line in $output) {
	$text = $line.Trim()
	if ($text.StartsWith('result:')) {
		$tokens = $text.Substring(7).Trim() -split ' '
		if ($tokens.Count -gt 0) {
			$verdict = $tokens[0]
		}
		foreach ($token in $tokens) {
			if ($token.StartsWith('profile=')) {
				$foundNumber = $token.Substring(8)
			}
		}
	}
}
if ($exitCode -ne 0 -and $verdict -eq 'no verdict printed') {
	$verdict = 'compiler exited with code ' + $exitCode
}

$foundName = ''
if ($foundNumber -ne '' -and $names.ContainsKey($foundNumber)) {
	$foundName = $names[$foundNumber]
}

$good = $verdict -eq 'EXACT' -or $verdict -eq 'PAYLOAD_SAME'

Write-Host ('Round-trip verdict: ' + $verdict)
Write-Host ''
if ($good -eq $true -and $foundName -ne '') {
	Write-Host ('This archive builds with the mode for: ' + $foundName) -ForegroundColor Green
}
if ($good -eq $true -and $foundName -eq '') {
	Write-Host 'The archive was reproduced but the compiler did not say with which mode - see the log.' -ForegroundColor Yellow
}
if ($good -eq $false) {
	Write-Host 'No build mode reproduced this archive. The log holds everything the compiler printed.' -ForegroundColor Yellow
}

# One machine-readable line at the very end, for anything that calls this
# script and wants the number without reading the prose.
Write-Host ''
Write-Host ('BUILD-MODE=' + $foundNumber)
