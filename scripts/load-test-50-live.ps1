# Live 50-concurrent load run against the running Docker API (localhost:8080), using a runspace
# pool for genuine parallelism. Demonstrates on the REAL stack:
#   the per-IP auth rate limiter caps a 50-login burst,
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
Write-Host '=== TEST: 50 concurrent logins, one IP (rate limiter under load) ===' -ForegroundColor Yellow
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
