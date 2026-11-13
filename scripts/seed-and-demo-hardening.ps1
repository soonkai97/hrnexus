# Seeds sample data into the running local Docker stack and demonstrates the new hardening
# (account lockout) against the LIVE API at localhost:8080.
# Idempotent for seeding; safe to re-run. Requires the api container to be up.

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

function Login($staffId, $password, $portal) {
    return Send-Json 'POST' '/auth/login' @{ staffId = $staffId; password = $password; portal = $portal } $null
}

Write-Host ''
Write-Host '=== 1. Authenticating as HRADMIN ===' -ForegroundColor Cyan
$admin = Login 'HRADMIN' 'Admin@1234' 'admin'
if ($admin.Status -ne 200) { throw "Admin login failed ($($admin.Status)). Is the api container current?" }
$adminToken = ($admin.Body | ConvertFrom-Json).accessToken
Write-Host '  admin token acquired (200)'

Write-Host ''
Write-Host '=== 2. Seeding job role (idempotent) ===' -ForegroundColor Cyan
$role = Send-Json 'POST' '/staff/roles' @{ name = 'Bartender'; hourlyRate = 20 } $adminToken
Write-Host "  role 'Bartender' -> $($role.Status) (204 created / 409 already exists)"

Write-Host ''
Write-Host '=== 3. Seeding sample staff (idempotent) ===' -ForegroundColor Cyan
$sampleStaff = @(
    @{ staffId = 'EMP001'; firstName = 'Aisha';  lastName = 'Rahman'; roles = @('Bartender') }
    @{ staffId = 'EMP002'; firstName = 'Bao';     lastName = 'Nguyen'; roles = @('Bartender') }
    @{ staffId = 'EMP003'; firstName = 'Carlos';  lastName = 'Diaz';   roles = @('Bartender') }
    @{ staffId = 'MGR001'; firstName = 'Mei';     lastName = 'Tan';    roles = @('MANAGER')   }
)
foreach ($s in $sampleStaff) {
    $body = @{ staffId = $s.staffId; firstName = $s.firstName; lastName = $s.lastName; email = "$($s.staffId.ToLower())@demo.com"; password = 'Passw0rd1'; roles = $s.roles }
    $res = Send-Json 'POST' '/staff' $body $adminToken
    Write-Host "  staff $($s.staffId) [$($s.roles -join ',')] -> $($res.Status) (201 created / 409 exists)"
}

Write-Host ''
Write-Host '=== 4. LIVE DEMO: account lockout (5 failed logins) ===' -ForegroundColor Yellow
$null = Send-Json 'POST' '/staff' @{ staffId = 'LOCKDEMO'; firstName = 'Lock'; lastName = 'Demo'; email = 'lockdemo@demo.com'; password = 'Passw0rd1'; roles = @('Bartender') } $adminToken
$seq = @()
for ($i = 1; $i -le 6; $i++) {
    $r = Login 'LOCKDEMO' 'WrongPassword!' 'staff'
    $seq += $r.Status
    Write-Host "  attempt #${i}: HTTP $($r.Status)"
}
Write-Host "  sequence: $($seq -join ', ')  (fresh account expects 401,401,401,401,429,429)"
$correct = Login 'LOCKDEMO' 'Passw0rd1' 'staff'
Write-Host "  correct password while locked -> HTTP $($correct.Status) (expect 429)" -ForegroundColor Yellow

Write-Host ''
Write-Host 'Done.' -ForegroundColor Green
