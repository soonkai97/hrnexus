# Live 50-concurrent load run against the running Docker API (localhost:8080), using a runspace
# pool for genuine parallelism. Demonstrates on the REAL stack:
#   (1) the double-clock-in guard holds under 50 simultaneous requests (exactly 1 wins),
#   (2) the per-IP auth rate limiter caps a 50-login burst,
# and reports throughput / latency / any connection-pool errors. Requires api container up.

$ErrorActionPreference = 'Stop'
$api = 'http://localhost:8080/api'

function Send-Json($method, $path, $body, $token) {
    $headers = @{}
    if ($token) { $headers['Authorization'] = "Bearer $token" }
    $json = $null
    if ($null -ne $body) { $json = ($body | ConvertTo-Json -Depth 6) }
    try {
        $r = Invoke-WebRequest -Uri "$api$path" -Method $method -Headers $headers -Body $json -ContentType 'application/json' -UseBasicParsing -ErrorAction Stop
        return [pscustomobject]@{ Status = [int]$r.StatusCode; Body = $r.Content }
    } catch {
        $resp = $_.Exception.Response
        $code = -1
        if ($resp) { $code = [int]$resp.StatusCode }
        return [pscustomobject]@{ Status = $code; Body = $_.ErrorDetails.Message }
    }
}

# Fire $count copies of $script (which returns an int HTTP status) concurrently via a runspace pool.
function Invoke-Concurrent($count, $script, $argList) {
    $pool = [runspacefactory]::CreateRunspacePool(1, $count)
    $pool.Open()
    $jobs = @()
    for ($k = 0; $k -lt $count; $k++) {
        $ps = [powershell]::Create()
        $ps.RunspacePool = $pool
        [void]$ps.AddScript($script)
        foreach ($a in $argList) { [void]$ps.AddArgument($a) }
        $jobs += [pscustomobject]@{ PS = $ps; Handle = $ps.BeginInvoke() }
    }
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $codes = @()
    foreach ($j in $jobs) { $codes += $j.PS.EndInvoke($j.Handle); $j.PS.Dispose() }
    $sw.Stop()
    $pool.Close(); $pool.Dispose()
    return [pscustomobject]@{ Codes = $codes; ElapsedMs = $sw.ElapsedMilliseconds }
}

Write-Host ''
Write-Host '=== Auth + seed guard user ===' -ForegroundColor Cyan
$admin = Send-Json 'POST' '/auth/login' @{ staffId = 'HRADMIN'; password = 'Admin@1234'; portal = 'admin' } $null
if ($admin.Status -ne 200) { throw "Admin login failed ($($admin.Status))." }
$adminToken = ($admin.Body | ConvertFrom-Json).accessToken
$null = Send-Json 'POST' '/staff/roles' @{ name = 'Bartender'; hourlyRate = 20 } $adminToken
foreach ($loc in @('Main Bar')) { $null = Send-Json 'POST' '/locations' @{ name = $loc } $adminToken }
$locs = (Send-Json 'GET' '/locations' $null $adminToken).Body | ConvertFrom-Json
$locId = @($locs)[0].id
$null = Send-Json 'POST' '/staff' @{ staffId = 'RACE50'; firstName = 'Race'; lastName = 'Fifty'; email = 'race50@demo.com'; password = 'Passw0rd1'; roles = @('Bartender') } $adminToken
Write-Host "  guard user RACE50 ready; locationId=$locId"

Write-Host ''
Write-Host '=== TEST 1: 50 concurrent clock-ins, SAME user (guard under load) ===' -ForegroundColor Yellow
$race = Send-Json 'POST' '/auth/login' @{ staffId = 'RACE50'; password = 'Passw0rd1'; portal = 'staff' } $null
if ($race.Status -ne 200) { throw "RACE50 login failed ($($race.Status)) - may be locked from a prior run." }
$raceToken = ($race.Body | ConvertFrom-Json).accessToken
$clockScript = {
    param($api, $token, $locId)
    $headers = @{ Authorization = "Bearer $token" }
    $body = (@{ roleName = 'Bartender'; locationId = $locId; startTime = '09:00'; notes = $null } | ConvertTo-Json)
    try {
        $r = Invoke-WebRequest -Uri "$api/worklogs/start" -Method Post -Headers $headers -Body $body -ContentType 'application/json' -UseBasicParsing -ErrorAction Stop
        return [int]$r.StatusCode
    } catch {
        $resp = $_.Exception.Response
        if ($resp) { return [int]$resp.StatusCode } else { return -1 }
    }
}
$r1 = Invoke-Concurrent 50 $clockScript @($api, $raceToken, $locId)
$ok  = @($r1.Codes | Where-Object { $_ -eq 200 }).Count
$rej = @($r1.Codes | Where-Object { $_ -eq 400 }).Count
$err = @($r1.Codes | Where-Object { $_ -ne 200 -and $_ -ne 400 }).Count
Write-Host "  50 parallel clock-ins -> 200:$ok  400(rejected):$rej  other:$err  in $($r1.ElapsedMs) ms"
Write-Host "  EXPECT exactly 1x200 and 49x400 (active-shift guard holds at 50-way)" -ForegroundColor Yellow

Write-Host ''
Write-Host '=== TEST 2: 50 concurrent logins, one IP (rate limiter under load) ===' -ForegroundColor Yellow
$loginScript = {
    param($api)
    $body = (@{ staffId = 'RATEBOT'; password = 'whatever'; portal = $null } | ConvertTo-Json)
    try {
        $r = Invoke-WebRequest -Uri "$api/auth/login" -Method Post -Body $body -ContentType 'application/json' -UseBasicParsing -ErrorAction Stop
        return [int]$r.StatusCode
    } catch {
        $resp = $_.Exception.Response
        if ($resp) { return [int]$resp.StatusCode } else { return -1 }
    }
}
$r2 = Invoke-Concurrent 50 $loginScript @($api)
$limited = @($r2.Codes | Where-Object { $_ -eq 429 }).Count
$passed  = @($r2.Codes | Where-Object { $_ -ne 429 }).Count
Write-Host "  50 parallel logins -> 429(rate-limited):$limited  passed-through:$passed  in $($r2.ElapsedMs) ms"
Write-Host "  EXPECT a chunk of 429s (per-IP 30/min cap engages under the burst)" -ForegroundColor Yellow

Write-Host ''
Write-Host 'Done.' -ForegroundColor Green
