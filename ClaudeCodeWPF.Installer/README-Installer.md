# ClaudeCodeWPF.Installer — 安裝專案說明

## 先決條件

在 Visual Studio 中建置 MSI 之前，必須先安裝：

### 1. WiX Toolset v3
前往 [https://wixtoolset.org/releases/](https://wixtoolset.org/releases/) 下載並安裝 WiX Toolset v3.11+

### 2. Visual Studio WiX 延伸模組 (選用但建議)
在 Visual Studio → Extensions → Manage Extensions 搜尋並安裝：
- **WiX Toolset Visual Studio Extension**

這可讓你在 VS 中開啟 `.wixproj` 並獲得語法提示。

---

## 建置步驟

### 方法 A: Visual Studio

1. 開啟 `ClaudeCodeWPF.sln`
2. 在 Solution Explorer 中選擇 `ClaudeCodeWPF.Installer`
3. 設定為 **Release | x86**
4. 對 WPF 主專案 `OpenClaudeCodeWPF` 按右鍵 → **Build**（先建置主專案）
5. 對 `ClaudeCodeWPF.Installer` 按右鍵 → **Build**
6. MSI 輸出位置：`ClaudeCodeWPF.Installer\bin\Release\OpenClaudeCodeWPF-Setup.msi`

### 方法 B: 命令列 (MSBuild)

```bat
cd /d X:\myclaudecode\claude-code-wpf

rem 先建置主專案 (Release)
msbuild ClaudeCodeWPF\ClaudeCodeWPF.csproj /p:Configuration=Release /p:Platform="Any CPU"

rem 再建置安裝專案
msbuild ClaudeCodeWPF.Installer\ClaudeCodeWPF.Installer.wixproj /p:Configuration=Release /p:Platform=x86
```

---

## 輸出檔案

```
ClaudeCodeWPF.Installer\bin\Release\
└── OpenClaudeCodeWPF-Setup.msi   ← 發布給使用者的安裝檔
```

---

## 安裝內容

| 項目 | 位置 |
|------|------|
| ClaudeCodeWPF.exe | C:\Program Files\Open Claude Code\Open Claude Code WPF\ |
| ClaudeCodeWPF.exe.config | 同上 |
| Newtonsoft.Json.dll | 同上 |
| 開始功能表捷徑 | 開始 → Open Claude Code WPF\ |
| 桌面捷徑 | 桌面 |
| 解除安裝捷徑 | 開始 → Open Claude Code WPF\Uninstall |

---

## 升級支援

`Product.wxs` 已設定 `MajorUpgrade`，建置新版本時：

1. 更新 `ClaudeCodeWPF/Properties/AssemblyInfo.cs` 中的 `AssemblyVersion`
2. 更新 `Product.wxs` 中 `<Product Version="X.Y.Z.0" ...>`
3. 重新建置 MSI，舊版本會自動被移除再安裝新版

> ⚠️ **不要** 修改 `UpgradeCode`（`{E5F6A7B8-C9D0-1234-EF01-23456789ABCD}`），
> 這個 GUID 是辨識同一產品所有版本的唯一識別碼。

---

## 自訂授權條款

替換 `ClaudeCodeWPF.Installer\License.rtf` 為你的授權 RTF 文件，
安裝精靈的授權頁面會顯示該內容。
