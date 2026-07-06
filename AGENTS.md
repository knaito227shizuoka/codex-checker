# AGENTS.md

## プロジェクトメモ

- メインアプリ: `src/CodexChecker`
- テストランナー: `tests/CodexChecker.Tests`
- 発行済み exe: `src/CodexChecker/bin/Release/net6.0-windows/win-x64/publish/CodexChecker.exe`
- このアプリは常駐する WinForms アプリです。`CodexChecker.exe` が起動中だと発行結果がロックされるため、再発行前に停止してください。

## コード変更後の対応

エージェントがアプリを修正・変更した場合は、以下の1コマンドでアプリを再実行してください。

```powershell
pwsh -File .\redeploy.ps1
```

`redeploy.ps1` はビルド → テスト → 常駐プロセス停止 → 発行 → 再起動 → 常駐確認を順に実行し、最初に失敗したステップで終了コード1で停止します。

個別のステップ（ビルドのみ、再起動のみなど）を実行したい場合は、以下のコマンドを使用してください。

1. アプリをビルドする。

   ```powershell
   dotnet build src/CodexChecker
   ```

2. テストを実行する。

   ```powershell
   dotnet run --project tests/CodexChecker.Tests
   ```

3. 発行前に、現在起動中の常駐プロセスを停止する。

   ```powershell
   Get-Process CodexChecker -ErrorAction SilentlyContinue | Stop-Process
   ```

4. 更新した常駐用 exe を発行する。

   ```powershell
   dotnet publish src/CodexChecker -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
   ```

5. 更新したアプリを起動する。

   ```powershell
   Start-Process -FilePath "$PWD\src\CodexChecker\bin\Release\net6.0-windows\win-x64\publish\CodexChecker.exe" -WindowStyle Hidden
   ```

6. 常駐していることを確認する。

   ```powershell
   Get-Process CodexChecker
   ```

## 手動動作確認

- 通知領域に `codex-checker` アイコンが表示されていることを確認する。
- ポップアップが非表示の状態で `Ctrl + Alt + X` を押す: ポップアップが表示されること。
- ポップアップが表示中の状態で `Ctrl + Alt + X` を押す: ポップアップが閉じること。
- ポップアップの `x` ボタンをクリックする: アプリは常駐したままポップアップが閉じること。
- トレイアイコンを右クリックし `常駐を終了` を選択する: 常駐プロセスが終了すること。

## サンドボックスに関する注意

この環境では、テスト用の `dotnet run` や `dotnet publish` が .NET SDK による `C:\Users\azcat\AppData\Local\Microsoft SDKs` への読み取りのために昇格実行を必要とする場合があります。サンドボックス実行がこのパスに対するアクセス拒否エラーで失敗した場合は、承認の上で同じコマンドを再実行してください。
