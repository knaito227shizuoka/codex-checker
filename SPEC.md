# codex-checker 常駐アプリ 仕様書

## 1. 概要

Codex の利用量(レートリミット)をいつでも確認できる Windows 常駐アプリ。
画面右下 (タスクバーの上) に小型のステータスポップアップを表示する。

- 常駐開始後、**10 分間隔で自動的に表示** (数秒で自動消滅)
- `Ctrl + Alt + X` でいつでも同じポップアップを即時表示/非表示

既存の [status.ps1](status.ps1) はワンショット確認用としてそのまま残す。
本アプリは app-server を起動しっぱなしにすることで、毎回のプロセス起動
(約 3〜4 秒) を省き、即座に表示することを目的とする。

## 2. 要件

| # | 要件 | 内容 |
|---|------|------|
| R1 | 常駐 | ログイン中バックグラウンドで動作し続ける |
| R2 | 定期表示 | 起動直後に 1 回表示し、以後 10 分間隔で自動表示 → 自動で消える |
| R3 | ホットキー | `Ctrl + Alt + X` でポップアップの即時表示/非表示をトグル |
| R4 | 表示位置 | プライマリモニタ右下 (WorkingArea 右下、タスクバーに被らない) |
| R5 | タスクバー | タスクバーにボタンを出さない (`ShowInTaskbar = false`) |
| R6 | app-server | `codex -s read-only -a never app-server` を常駐中つけっぱなしにする |
| R7 | 二重起動防止 | 名前付き Mutex で単一インスタンスを保証 |

## 3. 実装方式

| 項目 | 内容 |
|------|------|
| 言語 | C# |
| UI フレームワーク | WinForms (Windows Forms) |
| ランタイム | .NET 6.0 (`net6.0-windows`) |
| 外部パッケージ | なし (依存ゼロ。JSON は `System.Text.Json`、P/Invoke は `DllImport` で標準ライブラリのみ) |
| テストランナー | 自前コンソールランナー (xunit/MSTest は NuGet 復元できないため) |
| ビルド | `dotnet build` / `dotnet publish` |

- **ホットキー**: `user32.dll` の `RegisterHotKey` を P/Invoke で登録
  - modifiers = `MOD_CONTROL | MOD_ALT | MOD_NOREPEAT`, key = `X`
  - 非表示のメッセージ専用フォーム (または `NativeWindow`) で `WM_HOTKEY (0x0312)` を受信
- **通知領域**: `NotifyIcon` を表示し、常駐状態をタスクトレイから確認できるようにする。
  アイコンは外部ファイルなしで `System.Drawing` により runtime 生成する
- **定期表示**: `System.Windows.Forms.Timer` (interval = 10 分)。UI スレッド上で
  動くためスレッド間マーシャリング不要
- **コンソール非表示**: `OutputType = WinExe` のためランチャー不要。exe 直起動で窓は出ない
  - 自動起動する場合は `shell:startup` に exe へのショートカットを置く (手動・任意)

### ファイル構成

```
codex-checker/
├── status.ps1                    # 既存: ワンショット確認用 (変更しない)
├── SPEC.md                       # 本仕様書
├── src/
│   └── CodexChecker/
│       ├── CodexChecker.csproj   # WinExe, net6.0-windows, UseWindowsForms
│       ├── Program.cs            # エントリポイント (Mutex、ApplicationContext 起動)
│       ├── ResidentContext.cs    # 常駐本体 (ホットキー登録、タイマー、ライフサイクル)
│       ├── AppServerClient.cs    # codex app-server の起動・JSON-RPC・死活監視 (UI 非依存)
│       ├── RateLimitFormatter.cs # usedPercent → バー文字列などの整形ロジック (純粋関数)
│       └── StatusPopupForm.cs    # 右下ポップアップ UI
└── tests/
    └── CodexChecker.Tests/
        ├── CodexChecker.Tests.csproj  # OutputType=Exe の自前ランナー
        └── Program.cs                 # テスト本体 + Main (結果集計、失敗時 exit code 1)
```

### ビルド・実行

```powershell
dotnet build src/CodexChecker                 # デバッグビルド
dotnet run --project src/CodexChecker         # そのまま起動
dotnet publish src/CodexChecker -c Release    # 配布用 (フレームワーク依存)
```

### テスト

- `tests/CodexChecker.Tests` は `CodexChecker` 本体をプロジェクト参照し、
  UI 非依存のロジックを対象にテストする:
  - `RateLimitFormatter`: usedPercent 0/50/100/範囲外 → バー文字列・残り% の検証
  - JSON-RPC レスポンスのパース (id 対応付け、`rateLimits` 抽出、不正 JSON の無視)
- 実行: `dotnet run --project tests/CodexChecker.Tests`
  - 各テストの pass/fail を列挙し、1 件でも失敗すれば exit code 1
- 本体側は UI とロジックを分離し (`AppServerClient` / `RateLimitFormatter` は
  WinForms 非依存)、テスト可能な形を保つ

## 4. app-server の管理

### 起動

- 常駐開始時に `codex -s read-only -a never app-server` を
  `CreateNoWindow = true`, stdin/stdout リダイレクトで子プロセスとして起動
- 起動直後に `initialize` (clientInfo: `codex-checker-resident`) を送信

### 通信

- JSON-RPC (改行区切り JSON) over stdio。id は単調増加のカウンタで採番
- 表示のたび (定期タイマー / ホットキー) に `account/rateLimits/read` を送信し、
  対応する id のレスポンスを待つ (タイムアウト 5 秒)
- stdout は専用の読み取りループ (`ReadLineAsync` + タイムアウト) で常時読み、
  id とレスポンスを対応付ける。JSON でない行・通知は無視

### 死活監視・再起動

- RPC 送信前に `HasExited` を確認し、死んでいたら再起動 → `initialize` からやり直す
- 再起動しても応答が取れない場合はポップアップにエラーメッセージを表示する
  (アプリ自体は落とさない)

### 終了時

- 常駐アプリ終了時に app-server の子プロセスを `Kill()` して回収する
  (孤児プロセスを残さない)

## 5. ステータスポップアップ UI

### ウィンドウ属性

| 属性 | 値 |
|------|----|
| FormBorderStyle | None (枠なし) |
| 位置 | プライマリ画面 WorkingArea の右下角から 12px 内側 |
| TopMost | true |
| ShowInTaskbar | false |
| フォーカス | 奪わない (`ShowWithoutActivation = true`)。作業中に出ても入力を妨げない |
| サイズ | 約 460 x 142 px (1w の日付付きリセット時刻が見切れない範囲で抑える) |
| 背景 | ダーク系単色 + 白文字 (等幅フォント: Consolas) |

### 表示内容

status.ps1 の出力フォーマットを踏襲する:

```
<codex>
5h [########--] 残り80%
1w [#####-----] 残り52%
Claude Usage を開く        ← クリックでブラウザ起動
```

- バー: 残量 10 段階 (`#` = 残り, `-` = 消費済み)。`100 - usedPercent` で算出
- rateLimits に `resetsAt` / `resetsInSeconds` 相当のフィールドがあれば
  5h は「残りN%　R: HH:mm」、1w は「残りN%　R: yyyy-MM-dd HH:mm」の形で併記する
  (無ければ省略)
- `Claude Usage を開く` はリンクラベルとし、クリックで
  `https://claude.ai/new#settings/usage` を既定ブラウザで開く
- 取得中は「取得中...」を表示し、レスポンス到着後に書き換える
  (app-server 常駐のため通常は 1 秒以内)
- 取得失敗時は「利用量を取得できませんでした。」を表示

### 表示・消滅の動作

| トリガー | 動作 |
|----------|------|
| 起動直後 / 10 分タイマー | 最新値を取得して表示 → **8 秒後に自動で消える** |
| `Ctrl + Alt + X` | 非表示なら表示 (自動消滅なし・出しっぱなし)。表示中なら閉じる |
| バツボタン | 閉じる |
| ポップアップを左クリック | 閉じる |
| 右クリック | コンテキストメニュー表示: 「再取得」「常駐を終了」 |

- 定期表示で出ている間にホットキーを押した場合も閉じる
- 手動表示中に 10 分タイマーが来た場合は、表示を更新して出しっぱなしを維持する
- 「閉じる」はフォームの Hide であり、アプリと app-server は常駐を続ける
- 「常駐を終了」でホットキー解除 (`UnregisterHotKey`)・タイマー停止・
  app-server Kill・プロセス終了

## 6. エラー処理

| ケース | 挙動 |
|--------|------|
| ホットキー登録失敗 (他アプリと衝突) | メッセージボックスで通知して終了 |
| 二重起動 | Mutex 取得失敗 → 何もせず即終了 (既存インスタンスが有効) |
| codex コマンドが見つからない | メッセージボックスで通知して終了 |
| RPC タイムアウト | ポップアップにエラー表示。次回表示時に app-server 再起動を試行 |
| フルスクリーンアプリ使用中の定期表示 | TopMost のため上に被る (許容)。気になる場合は将来課題の抑制オプションで対応 |

## 7. 非機能・制約

- 定期取得は 10 分に 1 回の RPC のみ。それ以外の待機中はメッセージループと
  タイマーだけで CPU をほぼ使わない
- 通知領域 (システムトレイ) にアイコンを置き、常駐状態を見えるようにする。
  終了手段は通知領域アイコンまたはポップアップの右クリックメニュー
- `Ctrl + Alt + X` が他アプリに取られている場合は
  登録に失敗するため、ソース冒頭の定数 (`ResidentContext` 内) でキー・表示間隔 (10 分)・
  自動消滅時間 (8 秒) を変更可能にする
- 対象はプライマリモニタのみ (マルチモニタ対応は将来課題)

## 8. 将来拡張 (今回は実装しない)

- Claude 側の利用量 API 取得 (現状はリンクのみ)
- 残量が閾値を下回った際の強調表示 (バーの色変え) やトースト通知
- フルスクリーン検出時に定期表示を抑制するオプション
- タスクスケジューラ登録による自動起動のセットアップスクリプト
