#Requires -Version 7.0
#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.0.0' }
# NOT CUSTOMER-READY. Internal regression tests only; no Azure resources or credentials are used.

BeforeAll {
    $tokens = $null
    $parseErrors = $null
    $source = Join-Path $PSScriptRoot 'Test-ObserverLogAnalytics.ps1'
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($source, [ref]$tokens, [ref]$parseErrors)
    if ($parseErrors.Count) { throw 'The verification script has parsing errors.' }
    $definition = $ast.Find({
        param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Invoke-Query'
    }, $true)
    if (-not $definition) { throw 'Invoke-Query was not found in the verification script.' }
    . ([scriptblock]::Create($definition.Extent.Text))
    $script:realRestMethod = Get-Command Microsoft.PowerShell.Utility\Invoke-RestMethod -CommandType Cmdlet

    function Get-AzContext { throw 'A real Azure context must not be used by these tests.' }
    function Get-Token([string] $ResourceUrl) { throw 'A real token must not be requested by these tests.' }
}

Describe 'Log Analytics verification query responses' {
    BeforeEach {
        $script:queryResponse = [pscustomobject]@{
            tables = @([pscustomobject]@{
                name = 'PrimaryResult'
                columns = @(
                    [pscustomobject]@{ name = 'value'; type = 'real' }
                    [pscustomobject]@{ name = 'Value'; type = 'real' }
                )
                rows = @(@(42, 13), @(0, -12.5))
            })
        }
        Mock Get-AzContext {
            [pscustomobject]@{ Environment = [pscustomobject]@{
                AzureOperationalInsightsEndpointResourceId = 'https://query-review.invalid'
                AzureOperationalInsightsEndpoint = 'https://query-review.invalid/v1'
            } }
        }
        Mock Get-Token { 'test-placeholder' }
        Mock Invoke-RestMethod { $script:queryResponse }
    }

    It 'rejects response error <Code> before emitting any rows' -ForEach @(
        @{ Code = 'PartialError'; ExpectedCode = 'PartialError' }
        @{ Code = 'UnknownServiceError'; ExpectedCode = 'UnknownServiceError' }
        @{ Code = ''; ExpectedCode = 'UnspecifiedError' }
    ) {
        $script:queryResponse | Add-Member -NotePropertyName error -NotePropertyValue ([pscustomobject]@{
            code = $Code
            message = 'detail-that-must-not-be-echoed'
        })
        $accepted = [System.Collections.Generic.List[object]]::new()

        { Invoke-Query 'test-workspace' 'print test=1' | ForEach-Object { $accepted.Add($_) } } |
            Should -Throw -ExpectedMessage "*($ExpectedCode)*"

        $accepted.Count | Should -Be 0
        Should -Invoke Invoke-RestMethod -Times 1 -Exactly
    }

    It 'rejects a partial error even when its table has no rows' {
        $script:queryResponse.tables[0].rows = @()
        $script:queryResponse | Add-Member -NotePropertyName error -NotePropertyValue ([pscustomobject]@{ code = 'PartialError' })

        { Invoke-Query 'test-workspace' 'print test=1' } | Should -Throw -ExpectedMessage '*PartialError*'
    }

    It 'rejects an error-only response before trying to read tables' {
        $script:queryResponse = [pscustomobject]@{ error = [pscustomobject]@{ code = 'PartialError' } }

        { Invoke-Query 'test-workspace' 'print test=1' } | Should -Throw -ExpectedMessage '*PartialError*'
    }

    It 'does not echo service error details in its rejection message' {
        $script:queryResponse | Add-Member -NotePropertyName error -NotePropertyValue ([pscustomobject]@{
            code = 'PartialError'
            message = 'detail-that-must-not-be-echoed'
        })
        $rejection = $null
        try { Invoke-Query 'test-workspace' 'print test=1' | Out-Null } catch { $rejection = $_ }

        $rejection | Should -BeOfType ([System.Management.Automation.ErrorRecord])
        $rejection.Exception.Message | Should -Not -Match 'detail-that-must-not-be-echoed'
    }

    It 'preserves complete rows and case-distinct column names' {
        $rows = @(Invoke-Query 'test-workspace' 'print test=1')

        $rows.Count | Should -Be 2
        $rows[0].Count | Should -Be 2
        $rows[0]['value'] | Should -Be 42
        $rows[0]['Value'] | Should -Be 13
        $rows[1]['value'] | Should -Be 0
        $rows[1]['Value'] | Should -Be -12.5
        Should -Invoke Invoke-RestMethod -Times 1 -Exactly
    }

    It 'accepts a complete response whose error property is null' {
        $script:queryResponse | Add-Member -NotePropertyName error -NotePropertyValue $null

        @(Invoke-Query 'test-workspace' 'print test=1').Count | Should -Be 2
    }

    It 'accepts a valid empty result' {
        $script:queryResponse.tables[0].rows = @()

        @(Invoke-Query 'test-workspace' 'print test=1').Count | Should -Be 0
    }

    It 'retains transport failure handling without accepting rows' {
        Mock Invoke-RestMethod { throw 'synthetic transport failure' }

        { Invoke-Query 'test-workspace' 'print test=1' } |
            Should -Throw -ExpectedMessage '*Log Analytics query failed: synthetic transport failure*'
    }

    It 'handles a real HTTP-200 <Name> response' -ForEach @(
        @{ Name = 'complete'; HasError = $false }
        @{ Name = 'partial'; HasError = $true }
    ) {
        if ($HasError) {
            $script:queryResponse | Add-Member -NotePropertyName error -NotePropertyValue ([pscustomobject]@{ code = 'PartialError' })
        }
        $body = $script:queryResponse | ConvertTo-Json -Depth 10 -Compress
        $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
        $listener.Start()
        $script:loopbackEndpoint = "http://127.0.0.1:$($listener.LocalEndpoint.Port)/v1"
        $server = [powershell]::Create()
        $null = $server.AddScript({
            param($listener, $body)
            $accept = $listener.AcceptTcpClientAsync()
            if (-not $accept.Wait(10000)) { throw 'The loopback request did not connect within ten seconds.' }
            $client = $accept.Result
            try {
                $stream = $client.GetStream()
                $stream.ReadTimeout = 1000
                $reader = [System.IO.StreamReader]::new($stream, [System.Text.Encoding]::ASCII, $false, 1024, $true)
                $clock = [System.Diagnostics.Stopwatch]::StartNew()
                $headersComplete = $false
                for ($lineNumber = 0; $lineNumber -lt 100 -and $clock.Elapsed.TotalSeconds -lt 5; $lineNumber++) {
                    $line = $reader.ReadLine()
                    if ($null -eq $line) { throw 'The loopback client disconnected before sending headers.' }
                    if ($line -eq '') { $headersComplete = $true; break }
                }
                if (-not $headersComplete) { throw 'The bounded loopback request headers were incomplete.' }
                $bytes = [System.Text.Encoding]::UTF8.GetBytes($body)
                $header = "HTTP/1.1 200 OK`r`nContent-Type: application/json`r`nContent-Length: $($bytes.Length)`r`nConnection: close`r`n`r`n"
                $headerBytes = [System.Text.Encoding]::ASCII.GetBytes($header)
                $stream.Write($headerBytes, 0, $headerBytes.Length)
                $stream.Write($bytes, 0, $bytes.Length)
                $stream.Flush()
            }
            finally { $client.Dispose() }
        }).AddArgument($listener).AddArgument($body)
        Mock Get-AzContext {
            [pscustomobject]@{ Environment = [pscustomobject]@{
                AzureOperationalInsightsEndpointResourceId = 'https://query-review.invalid'
                AzureOperationalInsightsEndpoint = $script:loopbackEndpoint
            } }
        }
        Mock Invoke-RestMethod {
            & $script:realRestMethod -Method $Method -Uri $Uri -Headers $Headers -ContentType $ContentType -Body $Body -TimeoutSec 5
        }
        $pending = $server.BeginInvoke()
        try {
            if ($HasError) {
                { Invoke-Query 'test-workspace' 'print test=1' } | Should -Throw -ExpectedMessage '*PartialError*'
            }
            else {
                $rows = @(Invoke-Query 'test-workspace' 'print test=1')
                $rows.Count | Should -Be 2
                $rows[0]['value'] | Should -Be 42
                $rows[0]['Value'] | Should -Be 13
            }
            $null = $server.EndInvoke($pending)
            $server.HadErrors | Should -BeFalse
        }
        finally {
            $listener.Stop()
            $server.Dispose()
        }
    }
}