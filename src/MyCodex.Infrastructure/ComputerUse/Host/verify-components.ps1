$ErrorActionPreference = 'Stop'
try {
    $componentRoot = [IO.Path]::GetFullPath($env:MYCODEX_CUA_VERIFY_ROOT)
    foreach ($relative in @('bin\node.exe', 'bin\node_repl.exe', 'bin\node_modules\@oai\sky\bin\windows\codex-computer-use.exe')) {
        $entry = Join-Path $componentRoot $relative
        $signature = Get-AuthenticodeSignature -LiteralPath $entry
        if ($signature.Status -ne 'Valid' -or
            $signature.SignerCertificate.GetNameInfo([Security.Cryptography.X509Certificates.X509NameType]::SimpleName, $false) -ne 'OpenAI OpCo, LLC') {
            exit 1
        }
    }
    Write-Output 'OPENAI_COMPONENTS_VERIFIED'
    exit 0
} catch {
    exit 1
}
