[简体中文](README.md) | [English](README-EN.md) | **繁體中文**

<div align="center">

<img src="Plain Craft Launcher 2/Images/icon.png" alt="Logo" width="80" height="80">

</div>

# PCL XE

> **PCL XE（Plain Craft Launcher XE，eXtended Edition）** 是基於 [PCL Symbio Edition](https://github.com/Hydrogen-Peroxide/PCL-Symbio-Edition) 的實驗性分支，新增「網易版」「問問 AI」兩個頂層分頁；模組管理仍保留在「版本設定 → 模組管理」。

## PCL XE 的改動

| 功能 | 說明 |
| --- | --- |
| 網易版 | 自動辨識帶中國版特徵的「我的世界中國版」`.minecraft`（MCLDownload / netease 檔案 / 版本 JSON 中的 netease 欄位），也可手動選擇；把該目錄註冊為遊戲資料夾後，**直接用 Java 版啟動流程啟動遊戲**，不再喚起官方啟動器。國際版 `.minecraft` 不會被誤判。 |
| 模組管理 | 回到原位置：實例 → 版本設定 → 模組管理，沿用上游的啟用 / 停用、`.disabled` 後綴與中繼資料解析邏輯。 |
| 問問 AI | 用對話操作啟動器：填寫任意 OpenAI 相容的 `/chat/completions` 介面後，AI 回傳 JSON 指令，程式依白名單自動執行。 |
| 圖示與品牌 | 圖示、視窗標題與介面文字統一為 XE，不再出現 SE 字樣。 |

### 網易版啟動的邊界

- 只讀取本機既有的中國版 `.minecraft`，**不下載、不破解、不讀取網易帳號**；帳號驗證、版本下載與元件安裝仍由官方客戶端負責。
- 啟動沿用 PCL 的 Java 版流程（離線帳號、自動選擇 Java、資源與函式庫指向該 `.minecraft`），因此版本 JSON 必須完整。
- 首次啟動會把該 `.minecraft` 寫進遊戲資料夾清單，方便之後在啟動頁直接切換。
- 自動掃描只收錄能確認是中國版的目錄；國際版結構完全相同，因此不會被列出，手動選擇時頁面會給出警告。

## 功能概覽

### Java 版

- 官方正式版、快照與歷史版本一鍵安裝，自動補全依賴並選擇合適的 Java 執行環境
- 8 種安裝選項：Forge、NeoForge、Fabric、Legacy Fabric、OptiFine、LiteLoader、Cleanroom、LabyMod
- 支援整合包安裝與模組載入器切換

### 基岩版

- 讀取系統中已安裝的 Minecraft 基岩版，區分 UWP 舊版與 GDK 新版
- 支援匯入安裝包並解包到獨立目錄，不影響其他版本
- 內建 Xbox 帳號登入，登入後可直接啟動
- GDK 可攜版可按版本選擇載入器：原版、LeviLamina（依遊戲版本自動安裝）或 BLoader（隨啟動器分發）

### 資源下載

- CurseForge 與 Modrinth 雙源檢索
- 涵蓋模組、整合包、光影、資源包、資料包與世界存檔，下載後自動放入對應目錄

### 實例管理

- 每個實例擁有獨立的模組、存檔、截圖與設定
- 單一模組可隨時停用，便於排查衝突
- 存檔與截圖集中瀏覽、備份，並可一鍵開啟所在目錄
- 支援整體打包匯出

### 使用體驗

- 簡體中文與 English (US) 提供完整介面資源；其餘語言目前僅有佔位資源，介面文字回退為簡體中文
- 主題配色與背景可自訂，頁面切換與清單捲動帶有過渡動畫
- 自動掃描本機 Java，減少手動設定
- 啟動失敗時對日誌進行分析，給出可讀的原因與建議
- 閒置或切到背景時自動歸還執行記憶體，長時間開著也不會越用越吃記憶體

> PCL XE 不包含多人連線大廳功能，相關模組已從程式庫中移除。

## 支援平台

| 作業系統 | 支援情況 | 環境需求 |
|---|---|---|
| Windows 10 1809 (17763) 或更高 | 完整支援 | [.NET 10 Desktop Runtime](https://get.dot.net/10) |
| Windows 8 - Windows 10 1809 (17763) | 理論可執行，酌情提供支援 | [.NET 10 Desktop Runtime](https://get.dot.net/10) |
| Windows 7 或更低版本 | 不支援 | / |
| macOS / Linux / 其他作業系統 | 僅支援交叉編譯開發，無法直接執行 | [.NET 10 SDK](https://get.dot.net/10) |

**完整支援**：盡可能提供一切相關支援，但必須確保啟動器為最新版本。

**理論可執行，酌情提供支援**：PCL XE 應該可以在這些平台上執行，但不保證功能完全可用。

**不支援**：PCL XE 在這些平台的可用性較低，甚至無法啟動。請升級作業系統以使用 PCL XE。

**僅支援交叉編譯開發**：原始碼可以在 macOS 與 Linux 上編譯，但無法直接執行。

## 下載與使用

1. 從 [Releases](https://github.com/8888888b/PCL-X-Edition/releases) 下載最新版本的發布包
2. 解壓到任意目錄（啟動器為綠色可攜版，不會寫入系統目錄）
3. 執行 `Plain Craft Launcher XE.exe`，選擇 Java 版、基岩版，或進入「網易版」「問問 AI」；模組管理在 實例 → 版本設定 → 模組管理

> PCL XE 使用獨立版本號，更新源指向本儲存庫的 Release，不會被上游 PCL Symbio Edition 的版本覆蓋。

官方發布包為框架依賴版本，需要預先安裝 [.NET 10 Desktop Runtime](https://get.dot.net/10)；自行以自包含方式建置則無需額外安裝執行環境。

## 從原始碼建置

需要 [.NET 10 SDK](https://get.dot.net/10)。請在儲存庫根目錄執行：

```powershell
dotnet publish "Plain Craft Launcher 2/Plain Craft Launcher 2.csproj" `
  -c Release -p:Platform=x64 -p:PublishSingleFile=true `
  -p:DeleteExistingFiles=true -o ./artifact --no-self-contained
```

將 `-p:Platform=x64` 換成 `-p:Platform=ARM64` 即可建置 ARM64 版本。

可用設定（`-c`）：`Release`、`Beta`、`Debug`、`CI`；可用平台（`-p:Platform`）：`x64`、`ARM64`、`AnyCPU`。

## 專案結構

| 目錄 | 說明 |
|---|---|
| `Plain Craft Launcher 2/` | WPF 主程式：介面、頁面與 Minecraft 啟動邏輯 |
| `PCL.Core/` | 核心函式庫：設定、網路與下載、Java、存檔、動畫、在地化、日誌等 |
| `PCL.Core.SourceGenerators/` | 設定與相依性注入相關的來源產生器 |
| `PCL.Core.Test/` | PCL.Core 的單元測試 |
| `.github/` | CI 建置與發布工作流程 |

## 與上游的差異

- 新增「網易版」分頁：直接定位並啟動「我的世界中國版」的版本，不再喚起官方啟動器
- 新增「問問 AI」分頁：接入任意 OpenAI 相容介面，用對話讓啟動器執行白名單內的操作
- 模組管理回到「版本設定 → 模組管理」，不再佔用獨立分頁
- 自動更新使用本分支自己的更新源，版本號獨立（目前 `2.14.04`）
- 移除多人連線大廳及配套模組

## 聲明

> 以下聲明同時會在啟動器更新後的「版本說明」彈窗中展示。

- **專案性質**：PCL XE（Plain Craft Launcher XE）是基於 PCL Symbio Edition 的第三方實驗分支，由個人開發者維護，與 LTCatt（龍騰貓躍）維護的 PCL2 官方專案、PCL-Community 維護的 PCL CE 官方專案均無隸屬關係。
- **支援範圍**：「網易版」分頁只使用本機已安裝的中國版客戶端目錄，不提供破解或私有資料存取；Java 版與基岩版能力沿用上游。
- **API 使用**：僅透過經 Mojang Studios 批准的 API 存取 Minecraft 國際版的相關服務。
- **程式碼來源**：本專案不包含任何 Minecraft（含「我的世界中國版」）的程式碼，也不包含 Mojang Studios 及其中國代理商的任何程式碼。
- **名稱使用**：文件中出現的「Minecraft」字樣僅用於描述本專案所支援的遊戲，屬於合理使用。
- **關係說明**：與 PCL2、PCL CE、PCL Symbio Edition、Mojang Studios 及其中國代理商之間不存在任何隸屬、合作、維護或授權關係。
- **正版倡導**：鼓勵透過官方管道購買並遊玩正版 Minecraft。

## 授權條款

- `Plain Craft Launcher 2/` 使用[自訂授權條款](https://github.com/8888888b/PCL-X-Edition/blob/main/Plain%20Craft%20Launcher%202/LICENCE)
- 其餘所有目錄使用 [Apache License 2.0](https://github.com/8888888b/PCL-X-Edition/blob/main/LICENSE)
- 本專案中，**網易版適配、問問 AI、自動更新等新增獨立程式碼**：採用 GNU GPLv3，見 [LICENSE-GPL-3.0](./LICENSE-GPL-3.0)

## 致謝

- [PCL](https://github.com/Meloong-Git/PCL) — 原作者 龍騰貓躍
- [PCL Symbio Edition](https://github.com/Hydrogen-Peroxide/PCL-Symbio-Edition) — 本專案的直接上游，基岩版能力由此而來
- [BedrockBoot](https://github.com/Round-Studio/BedrockBoot) 與 [BedrockLauncher.Core](https://github.com/Round-Studio/BedrockLauncher.Core) — Round Studio 開發的基岩版專案（GPL-3.0），本專案基岩版安裝、解密與啟動方案的移植參考
- [BLoader](https://github.com/Chlna6666/BLoader) — Chlna6666 開發的基岩版模組載入器（GPL-3.0），隨啟動器分發並注入
- [LeviLamina](https://github.com/LiteLDev/LeviLamina) 與 [lip](https://github.com/LiteLDev/lip) — 依遊戲版本透過官方 lip 安裝的 LeviLamina 客戶端（LGPL-3.0 / GPL-3.0），不隨本專案分發
- 以及 Dapper、Downloader、Markdig.Wpf、NAudio、Microsoft.Data.Sqlite、protobuf-net、Lucide 等開源專案，完整的第三方元件與授權資訊見 [metadata.json](Plain%20Craft%20Launcher%202/metadata.json) 與啟動器「關於」頁面
