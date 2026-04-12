$cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject "CN=GuildRelay, O=GuildRelay Open Source" -KeyAlgorithm RSA -KeyLength 2048 -CertStoreLocation "Cert:\CurrentUser\My" -NotAfter (Get-Date).AddYears(5) -FriendlyName "GuildRelay Code Signing"

Write-Host "Certificate thumbprint: $($cert.Thumbprint)"

Export-Certificate -Cert $cert -FilePath "$PSScriptRoot\GuildRelay-CodeSigning.cer" -Type CERT -Force | Out-Null

$pw = ConvertTo-SecureString -String "GuildRelaySign2026" -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath "$PSScriptRoot\GuildRelay-CodeSigning.pfx" -Password $pw | Out-Null

Write-Host "Exported .cer (public) and .pfx (signing) to certs/"
Write-Host ""
Write-Host "Next steps:"
Write-Host "  1. Add PFX as a GitHub secret: base64-encode it and store as SIGNING_CERT_PFX"
Write-Host "  2. Add password as GitHub secret: SIGNING_CERT_PASSWORD = GuildRelaySign2026"
Write-Host "  3. Distribute .cer to guildmates for one-time trust installation"
