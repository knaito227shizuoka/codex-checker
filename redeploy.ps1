# ビルド → テスト → 常駐停止 → publish → 再起動 → 常駐確認 をまとめて実行する。
# 途中で失敗したらそこで止まる (exit code 1)。
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

function Invoke-Step {
    param([string]$Name, [scriptblock]$Body)
    Write-Host "==> $Name"
    & $Body
    if ($LASTEXITCODE -ne 0) {
        Write-Host "NG: $Name に失敗しました。" -ForegroundColor Red
        exit 1
    }
}

Invoke-Step 'ビルド' { dotnet build (Join-Path $root 'src/CodexChecker') }
Invoke-Step 'テスト' { dotnet run --project (Join-Path $root 'tests/CodexChecker.Tests') }

Write-Host '==> 常駐プロセス停止'
$running = Get-Process CodexChecker -ErrorAction SilentlyContinue
if ($running) {
    $running | Stop-Process

    # Stop-Process は非同期なので、消えるまで最大 10 秒待つ
    $stopped = $false
    foreach ($i in 1..20) {
        if (-not (Get-Process CodexChecker -ErrorAction SilentlyContinue)) {
            $stopped = $true
            break
        }
        Start-Sleep -Milliseconds 500
    }

    if (-not $stopped) {
        Write-Host 'NG: CodexChecker を停止できませんでした。publish 出力がロックされたままです。' -ForegroundColor Red
        exit 1
    }
}

Invoke-Step 'publish' {
    dotnet publish (Join-Path $root 'src/CodexChecker') -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
}

Write-Host '==> 再起動'
$exe = Join-Path $root 'src\CodexChecker\bin\Release\net6.0-windows\win-x64\publish\CodexChecker.exe'
Start-Process -FilePath $exe -WindowStyle Hidden

# 単一ファイル exe は初回展開に少しかかるので、常駐確認は数秒リトライする
$resident = $null
foreach ($i in 1..10) {
    Start-Sleep -Milliseconds 500
    $resident = Get-Process CodexChecker -ErrorAction SilentlyContinue
    if ($resident) { break }
}

if (-not $resident) {
    Write-Host 'NG: 起動後に CodexChecker プロセスを確認できませんでした。' -ForegroundColor Red
    exit 1
}

Write-Host ("OK: CodexChecker が常駐しています (PID {0})。" -f ($resident.Id -join ', ')) -ForegroundColor Green
