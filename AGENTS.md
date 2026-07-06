# AGENTS.md

## Project Notes

- Main app: `src/CodexChecker`
- Test runner: `tests/CodexChecker.Tests`
- Published exe: `src/CodexChecker/bin/Release/net6.0-windows/win-x64/publish/CodexChecker.exe`
- The app is a resident WinForms app. A running `CodexChecker.exe` locks the publish output, so stop it before republishing.

## After Making Code Changes

When an agent fixes or changes the app, rerun the app with this single command:

```powershell
pwsh -File .\redeploy.ps1
```

`redeploy.ps1` runs build → tests → stop resident process → publish → restart → verify residency, and stops with exit code 1 at the first failing step.

If you need to run an individual step (e.g. only build, or only restart), use the commands below:

1. Build the app.

   ```powershell
   dotnet build src/CodexChecker
   ```

2. Run the tests.

   ```powershell
   dotnet run --project tests/CodexChecker.Tests
   ```

3. Stop any currently running resident process before publishing.

   ```powershell
   Get-Process CodexChecker -ErrorAction SilentlyContinue | Stop-Process
   ```

4. Publish the updated resident exe.

   ```powershell
   dotnet publish src/CodexChecker -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
   ```

5. Start the updated resident app.

   ```powershell
   Start-Process -FilePath "$PWD\src\CodexChecker\bin\Release\net6.0-windows\win-x64\publish\CodexChecker.exe" -WindowStyle Hidden
   ```

6. Verify that it is resident.

   ```powershell
   Get-Process CodexChecker
   ```

## Manual Smoke Check

- Confirm the `codex-checker` icon appears in the notification area.
- Press `Ctrl + Alt + X` while the popup is hidden: it should show.
- Press `Ctrl + Alt + X` while the popup is visible: it should close.
- Click the popup `x` button: it should close while the app remains resident.
- Right-click the tray icon and use `常駐を終了`: the resident process should exit.

## Sandbox Notes

In this environment, `dotnet run` for tests and `dotnet publish` may need elevated execution because the .NET SDK reads `C:\Users\azcat\AppData\Local\Microsoft SDKs`. If sandboxed execution fails with an access denied error for that path, rerun the same command with approval.
