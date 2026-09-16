<#
.SYNOPSIS
  Signs the latest built Jockey Studio installer (+ portable exe) with the
  local self-signed code-signing certificate, creating and trusting it first
  if needed.

.DESCRIPTION
  Creates "CN=Jockey Studio" CodeSigningCert in CurrentUser\My if missing,
  imports its .cer into Trusted Root + Trusted Publishers, then signs the
  newest NSIS setup.exe under src-tauri\target\release\bundle\nsis (SHA-256,
  RFC3161 timestamp) and verifies the signature. Also signs jockey-studio.exe.
#>
$ErrorActionPreference = 'Continue'

$repoRoot = Split-Path -Parent $PSScriptRoot
$signingDir = Join-Path $env:TEMP 'jockey-studio-signing'
New-Item -ItemType Directory -Force -Path $signingDir | Out-Null

$cert = Get-ChildItem 'Cert:\CurrentUser\My' | Where-Object {
  $_.Subject -like '*Jockey Studio*' -and $_.EnhancedKeyUsageList -match 'Code Signing'
} | Select-Object -First 1

if (-not $cert) {
  Write-Host 'Creating self-signed code-signing certificate...'
  $cert = New-SelfSignedCertificate -Subject 'CN=Jockey Studio, O=CJay Capillo' `
    -Type CodeSigningCert -CertStoreLocation 'Cert:\CurrentUser\My' `
    -KeyExportPolicy Exportable -KeySpec Signature -KeyUsage DigitalSignature `
    -HashAlgorithm SHA256 -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3')
  Export-Certificate -Cert $cert -FilePath (Join-Path $signingDir 'jockey-studio.cer') | Out-Null
  & certutil -user -addstore Root (Join-Path $signingDir 'jockey-studio.cer') | Out-Null
  & certutil -user -addstore TrustedPublisher (Join-Path $signingDir 'jockey-studio.cer') | Out-Null
  Write-Host 'Certificate trusted locally (Root + TrustedPublisher).'
} else {
  Write-Host "Using existing certificate: $($cert.Thumbprint)"
}

$setup = Get-ChildItem "$repoRoot\src-tauri\target\release\bundle\nsis\*-setup.exe" -ErrorAction SilentlyContinue |
  Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
$exe = Join-Path $repoRoot 'src-tauri\target\release\jockey-studio.exe'

if (-not $setup) {
  Write-Host 'No NSIS installer found. Build one first: npm run tauri -- build'
  $setup = $null
}

foreach ($f in @($setup, $exe)) {
  if (-not $f -or -not (Test-Path $f)) { continue }
  try {
    Set-AuthenticodeSignature -FilePath $f -Certificate $cert -HashAlgorithm SHA256 `
      -TimeStampServer 'http://timestamp.digicert.com' | Out-Null
    $ts = 'timestamped'
  } catch {
    Write-Warning "Timestamp failed for $f; signing without timestamp."
    Set-AuthenticodeSignature -FilePath $f -Certificate $cert -HashAlgorithm SHA256 | Out-Null
    $ts = 'no timestamp'
  }
  $sig = Get-AuthenticodeSignature -FilePath $f
  Write-Host ("{0} -> {1} ({2}, {3})" -f (Split-Path $f -Leaf), $sig.Status, $sig.StatusMessage, $ts)
}