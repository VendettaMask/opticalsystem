#requires -Version 7.0
param([string]$OutputPath)

$ErrorActionPreference = 'Stop'
$checker = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../verify-documentation.ps1'))
$testParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$testRoot = Join-Path $testParent ('optical-documentation-tests-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($testRoot)
$results = [Collections.Generic.List[object]]::new()
$dup = '同一段正文用于验证两份现行指南之间的重复，明确保留输入、输出、边界和失败身份，不把旧阶段通过数解释为新的验收。' * 3

function Assert-Check([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Invoke-Fixture([hashtable]$Files) {
    $fixture = Join-Path $testRoot ([guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($fixture)
    & git -C $fixture init -q
    if ($LASTEXITCODE -ne 0) { throw 'Cannot initialize documentation fixture.' }
    foreach ($entry in $Files.GetEnumerator()) {
        $target = Join-Path $fixture $entry.Key
        [void][IO.Directory]::CreateDirectory((Split-Path $target))
        if ($entry.Value -is [byte[]]) { [IO.File]::WriteAllBytes($target, $entry.Value) }
        else { [IO.File]::WriteAllText($target, $entry.Value, [Text.UTF8Encoding]::new($false)) }
    }
    $reportPath = Join-Path $testRoot ([guid]::NewGuid().ToString('N') + '.json')
    $start = [Diagnostics.ProcessStartInfo]::new((Get-Command pwsh).Source)
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in @('-NoProfile', '-File', $checker, '-RepositoryRoot', $fixture, '-AuditDate', '2026-10-14', '-Strict', '-OutputPath', $reportPath)) {
        $start.ArgumentList.Add($argument)
    }
    $process = [Diagnostics.Process]::Start($start)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(20000)) { throw 'Documentation fixture timed out.' }
        $log = $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult()
        if (-not (Test-Path -LiteralPath $reportPath)) { throw ('Fixture did not publish a report: ' + $log) }
        [pscustomobject]@{ ExitCode=$process.ExitCode; Report=(Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json); Root=$fixture }
    } finally {
        if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
        $process.Dispose()
    }
}

function Run-Case([string]$Name, [scriptblock]$Body) {
    try {
        & $Body
        $results.Add([pscustomobject]@{ Name=$Name; Passed=$true })
    } catch {
        $results.Add([pscustomobject]@{ Name=$Name; Passed=$false; Error=$_.Exception.Message })
    }
}

try {
    Run-Case 'Two current guides fail strict repetition check' {
        $r = Invoke-Fixture @{ 'a.md'=$dup; 'b.md'=$dup }
        Assert-Check ($r.ExitCode -ne 0 -and $r.Report.GuideRepeatedParagraphs.Count -eq 1) 'Two-copy duplication was not rejected.'
        Assert-Check ($r.Report.RepeatedParagraphs[0].Documents -eq 2 -and $r.Report.RepeatedParagraphs[0].Locations[0].Line -eq 1) 'Document count/location is wrong.'
    }
    Run-Case 'Whitespace and CRLF normalize' {
        $r = Invoke-Fixture @{ 'a.md'=($dup + ' end'); 'b.md'=($dup + "`r`nend") }
        Assert-Check ($r.ExitCode -ne 0 -and $r.Report.GuideRepeatedParagraphs.Count -eq 1) 'Whitespace normalization failed.'
    }
    Run-Case 'One document repeated twice is not cross-document duplication' {
        $r = Invoke-Fixture @{ 'a.md'=($dup + "`n`n" + $dup) }
        Assert-Check ($r.ExitCode -eq 0 -and $r.Report.RepeatedParagraphs.Count -eq 0) 'Single-file repetition was misclassified.'
    }
    Run-Case 'Below 100 characters is ignored' {
        $r = Invoke-Fixture @{ 'a.md'=('字' * 99); 'b.md'=('字' * 99) }
        Assert-Check ($r.ExitCode -eq 0 -and $r.Report.RepeatedParagraphs.Count -eq 0) 'Lower character bound failed.'
    }
    Run-Case 'Exactly 100 characters is detected' {
        $r = Invoke-Fixture @{ 'a.md'=('字' * 100); 'b.md'=('字' * 100) }
        Assert-Check ($r.ExitCode -ne 0 -and $r.Report.GuideRepeatedParagraphs.Count -eq 1) 'Inclusive character bound failed.'
    }
    Run-Case 'Letter case remains distinct' {
        $r = Invoke-Fixture @{ 'a.md'=('A' * 100); 'b.md'=('a' * 100) }
        Assert-Check ($r.ExitCode -eq 0 -and $r.Report.RepeatedParagraphs.Count -eq 0) 'Case-distinct prose was merged.'
    }
    Run-Case 'Historical notices remain reported without strict failure' {
        $warning = '> ' + $dup
        $r = Invoke-Fixture @{ 'docs/history/a.md'=$warning; 'docs/history/b.md'=$warning }
        Assert-Check ($r.ExitCode -eq 0 -and $r.Report.RepeatedParagraphs.Count -eq 1 -and $r.Report.RepeatedParagraphs[0].Scope -eq 'Historical') 'Historical notice was hidden or rejected.'
    }
    Run-Case 'Mixed guide/history group remains visible' {
        $r = Invoke-Fixture @{ 'a.md'=$dup; 'docs/OLD_2026-10-13.md'=$dup }
        Assert-Check ($r.ExitCode -eq 0 -and $r.Report.RepeatedParagraphs[0].Scope -eq 'Mixed') 'Mixed-scope duplication was hidden or rejected.'
    }
    Run-Case 'Historical comparison audit is not a current guide' {
        $r = Invoke-Fixture @{ 'docs/ZEMAX_COMPARISON_TOOL.md'=$dup; 'docs/history/a.md'=$dup }
        Assert-Check ($r.ExitCode -eq 0 -and $r.Report.RepeatedParagraphs[0].Scope -eq 'Historical') 'Historical tool audit classification failed.'
    }
    Run-Case 'Fenced code and links inside it are ignored' {
        $code = '```text' + "`n" + $dup + "`n[ignored](missing.md)`n" + '```'
        $r = Invoke-Fixture @{ 'a.md'=$code; 'b.md'=$code }
        Assert-Check ($r.ExitCode -eq 0 -and $r.Report.RepeatedParagraphs.Count -eq 0 -and $r.Report.BrokenLinks.Count -eq 0) 'Code was treated as prose/links.'
    }
    Run-Case 'Longer fence may contain shorter fence' {
        $code = '````text' + "`n" + '```inner' + "`n" + $dup + "`n" + '```' + "`n" + '````'
        $r = Invoke-Fixture @{ 'a.md'=$code; 'b.md'=$code }
        Assert-Check ($r.ExitCode -eq 0 -and $r.Report.MarkdownFenceIssues.Count -eq 0 -and $r.Report.RepeatedParagraphs.Count -eq 0) 'Nested shorter fence ended a longer fence.'
    }
    Run-Case 'Indented code and Markdown tables are ignored' {
        $code = '    ' + $dup + "`n`n| " + $dup + ' |'
        $r = Invoke-Fixture @{ 'a.md'=$code; 'b.md'=$code }
        Assert-Check ($r.ExitCode -eq 0 -and $r.Report.RepeatedParagraphs.Count -eq 0) 'Code/table was treated as prose.'
    }
    Run-Case 'Evidence and notices keep fingerprints and bytes' {
        $r = Invoke-Fixture @{ 'artifacts/a.md'=$dup; 'artifacts/b.md'=$dup; 'third_party/NOTICE.md'=$dup }
        Assert-Check ($r.ExitCode -eq 0 -and $r.Report.RepeatedParagraphs.Count -eq 0 -and $r.Report.Inventory.Count -eq 3) 'Evidence inventory was not preserved.'
        Assert-Check ([IO.File]::ReadAllText((Join-Path $r.Root 'third_party/NOTICE.md')) -ceq $dup) 'Notice content changed.'
    }
    Run-Case 'Binary documents are hashed without Markdown decoding' {
        $payload = [Text.Encoding]::UTF8.GetBytes("[binary bytes are not links](missing.md)`n" + '```' + "`n" + $dup)
        $pdf = [byte[]](@(0x25,0x50,0x44,0x46,0x2D) + $payload)
        $office = [byte[]](@(0x50,0x4B,0x03,0x04) + $payload)
        $r = Invoke-Fixture @{ 'guide.md'='[PDF](preview.pdf)'; 'preview.pdf'=$pdf; 'document.docx'=$office; 'document.odt'=$office }
        Assert-Check ($r.ExitCode -eq 0 -and $r.Report.Inventory.Count -eq 4) 'Binary files were not inventoried.'
        Assert-Check ($r.Report.BrokenLinks.Count -eq 0 -and $r.Report.MarkdownFenceIssues.Count -eq 0) 'Binary bytes were parsed as Markdown.'
        Assert-Check ([Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $r.Root 'preview.pdf'))) -ceq [Convert]::ToBase64String($pdf)) 'Binary document was modified.'
    }
    Run-Case 'Chinese, duplicate and encoded anchors resolve' {
        $r = Invoke-Fixture @{ 'a.md'='[link](b.md#%E4%B8%AD%E6%96%87-%E8%AF%B4%E6%98%8E-1)'; 'b.md'="# 中文 说明`n`n# 中文 说明" }
        Assert-Check ($r.ExitCode -eq 0 -and $r.Report.BrokenLinks.Count -eq 0 -and $r.Report.Date -eq '2026-10-14') 'Anchor or explicit audit date failed.'
    }
    Run-Case 'Missing file is rejected' {
        $r = Invoke-Fixture @{ 'a.md'='[broken](missing.md)' }
        Assert-Check ($r.ExitCode -ne 0 -and $r.Report.BrokenLinks[0].Issue -eq 'MissingFile') 'Missing file was accepted.'
    }
    Run-Case 'Missing anchor is rejected' {
        $r = Invoke-Fixture @{ 'a.md'='[broken](b.md#missing)'; 'b.md'='# Existing' }
        Assert-Check ($r.ExitCode -ne 0 -and $r.Report.BrokenLinks[0].Issue -eq 'MissingAnchor') 'Missing anchor was accepted.'
    }
    Run-Case 'Wrong fence type cannot close a fence' {
        $r = Invoke-Fixture @{ 'a.md'= ('~~~text' + "`ncode`n" + '```') }
        Assert-Check ($r.ExitCode -ne 0 -and $r.Report.MarkdownFenceIssues.Count -eq 1) 'Unclosed mixed fence was accepted.'
    }
    $failed = @($results | Where-Object { -not $_.Passed })
    $report = [ordered]@{ Date='2026-10-14'; Cases=$results.Count; Passed=($results.Count-$failed.Count); Failed=$failed.Count; Results=$results.ToArray() }
    $json = $report | ConvertTo-Json -Depth 6
    if ($OutputPath) {
        $absolute = [IO.Path]::GetFullPath($OutputPath)
        [void][IO.Directory]::CreateDirectory((Split-Path $absolute))
        [IO.File]::WriteAllText($absolute, $json + "`n", [Text.UTF8Encoding]::new($false))
    }
    $json
    if ($failed.Count -gt 0) { throw "$($failed.Count) documentation regression cases failed." }
} finally {
    $resolved = [IO.Path]::GetFullPath($testRoot)
    $expected = [IO.Path]::GetFullPath((Join-Path $testParent ([IO.Path]::GetFileName($testRoot))))
    if ($resolved -cne $expected -or -not [IO.Path]::GetFileName($resolved).StartsWith('optical-documentation-tests-')) { throw 'Unsafe fixture cleanup target.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
