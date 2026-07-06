$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = "codex"
# ArgumentList は Windows PowerShell 5.1 に無いため、単一文字列で指定する
$psi.Arguments = "-s read-only -a never app-server"
$psi.RedirectStandardInput = $true
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.UseShellExecute = $false
$psi.CreateNoWindow = $true

$p = New-Object System.Diagnostics.Process
$p.StartInfo = $psi
$p.Start() | Out-Null

function Send-CodexRpc {
    param(
        [int]$Id,
        [string]$Method,
        [object]$Params = @{}
    )

    $payload = @{
        id = $Id
        method = $Method
        params = $Params
    } | ConvertTo-Json -Compress -Depth 20

    $p.StandardInput.WriteLine($payload)
    $p.StandardInput.Flush()
}

Send-CodexRpc -Id 1 -Method "initialize" -Params @{
    clientInfo = @{
        name = "powershell-test"
        version = "0.1.0"
    }
}

Start-Sleep -Milliseconds 500

Send-CodexRpc -Id 2 -Method "account/read"
Send-CodexRpc -Id 3 -Method "account/rateLimits/read"

Start-Sleep -Seconds 3

# app-server は常駐で stdout を閉じないため、EndOfStream では終われない。
# 次の行が一定時間来なければ読み終わりとみなすタイムアウト方式にする。
$readTimeoutMs = 1000
$rateLimits = $null
while (-not $p.HasExited -or -not $p.StandardOutput.EndOfStream) {
    $task = $p.StandardOutput.ReadLineAsync()
    if (-not $task.Wait($readTimeoutMs)) {
        # タイムアウト: これ以上レスポンスは来ないと判断して終了
        break
    }
    $line = $task.Result
    if ($null -eq $line) {
        # ストリーム終端
        break
    }

    # id:3 (account/rateLimits/read) のレスポンスからレートリミットを抽出
    try {
        $obj = $line | ConvertFrom-Json
        if ($obj.id -eq 3 -and $obj.result.rateLimits) {
            $rateLimits = $obj.result.rateLimits
        }
    } catch {
        # JSON でない行は無視
    }
}

if (-not $p.HasExited) {
    $p.Kill()
}
$p.WaitForExit()

function Format-Bar {
    param([string]$Label, [object]$Window)
    if ($null -eq $Window) {
        Write-Host "$Label データなし"
        return
    }
    $remaining = 100 - $Window.usedPercent
    # 残量を10段階のバーで表示（#=残り, -=消費済み）
    $filled = [int][math]::Round($remaining / 10.0)
    if ($filled -lt 0)  { $filled = 0 }
    if ($filled -gt 10) { $filled = 10 }
    $bar = "[" + ("#" * $filled) + ("-" * (10 - $filled)) + "]"
    Write-Host ("{0} {1} 残り{2}%" -f $Label, $bar, $remaining)
}

Write-Host "<codex>"
if ($null -eq $rateLimits) {
    Write-Host "利用量を取得できませんでした。"
} else {
    Format-Bar -Label "5h" -Window $rateLimits.primary
    Format-Bar -Label "1w" -Window $rateLimits.secondary
}
Write-Host ""
Write-Host "<claude>"
Write-Host "https://claude.ai/new#settings/usage"