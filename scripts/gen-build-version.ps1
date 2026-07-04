# gen-build-version.ps1 — Windows/local equivalent of gen-build-version.sh.
# Emits YYYYMMDD.NN (NN = Nth build today), persisting the per-day counter in
# scripts/.build-seq (git-ignored). Prints only the version for capture:
#
#   $env:BUILD_VERSION = & scripts/gen-build-version.ps1
#
$ErrorActionPreference = 'Stop'

$seqFile = Join-Path $PSScriptRoot '.build-seq'
$today   = Get-Date -Format 'yyyyMMdd'

$lastDate = ''
$lastSeq  = 0
if (Test-Path $seqFile) {
  $parts = (Get-Content $seqFile -Raw).Trim() -split '\s+'
  if ($parts.Count -ge 2) { $lastDate = $parts[0]; $lastSeq = [int]$parts[1] }
}

$seq = if ($lastDate -eq $today) { $lastSeq + 1 } else { 1 }

"$today $seq" | Set-Content -Path $seqFile -Encoding ascii -NoNewline
'{0}.{1:D2}' -f $today, $seq
