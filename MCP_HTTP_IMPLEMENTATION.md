# MCP HTTP/SSE Transport Implementation

## 版本
v0.1.5 (2026-04-18)

## 問題
用戶回報 MCP HTTP 連接失敗，錯誤訊息：
```
連接失敗：由於未提供憑證名稱，所以無法啟動該區。
```

## 根本原因
`MCPClient` 原本只實作了 stdio 傳輸模式（透過 Process 啟動子程序），UI 雖然有 HTTP/SSE 設定欄位，但後端並未實作 HTTP 傳輸功能。

## 解決方案

### 1. 新增 HttpClient 支援
在 `MCPClient` 類別中新增 `_httpClient` 欄位：

```csharp
private HttpClient _httpClient;
```

### 2. 分離連接邏輯
將 `ConnectAsync` 重構為：
- `ConnectStdioAsync()` - 原本的 stdio 邏輯
- `ConnectHttpAsync()` - 新的 HTTP 傳輸邏輯

```csharp
public async Task ConnectAsync(CancellationToken cancellationToken)
{
    if (Config.Type == McpServerType.Http || Config.Type == McpServerType.Sse)
    {
        await ConnectHttpAsync(cancellationToken);
    }
    else
    {
        await ConnectStdioAsync(cancellationToken);
    }
}
```

### 3. SSL 憑證處理
在 `ConnectHttpAsync` 中建立 `HttpClientHandler`，並設定 `ServerCertificateCustomValidationCallback` 來接受自簽憑證：

```csharp
var handler = new HttpClientHandler();
handler.ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true;

_httpClient = new HttpClient(handler);
_httpClient.Timeout = TimeSpan.FromSeconds(30);
```

### 4. 分離請求發送邏輯
將 `SendRequestAsync` 重構為：
- `SendStdioRequestAsync()` - 透過 stdin/stdout 發送 JSON-RPC
- `SendHttpRequestAsync()` - 透過 HTTP POST 發送 JSON-RPC

```csharp
private async Task<MCPResponse> SendRequestAsync(string method, JObject @params, CancellationToken cancellationToken)
{
    if (_httpClient != null)
    {
        return await SendHttpRequestAsync(method, @params, cancellationToken);
    }
    else
    {
        return await SendStdioRequestAsync(method, @params, cancellationToken);
    }
}
```

### 5. HTTP 請求實作
```csharp
private async Task<MCPResponse> SendHttpRequestAsync(string method, JObject @params, CancellationToken cancellationToken)
{
    var id = _nextId++;
    var request = new MCPRequest { Method = method, Id = id, Params = @params };
    var json = JsonConvert.SerializeObject(request);
    var content = new StringContent(json, Encoding.UTF8, "application/json");

    var url = Config.SseUrl; // For HTTP, use SseUrl as the endpoint
    var response = await _httpClient.PostAsync(url, content, cancellationToken);
    response.EnsureSuccessStatusCode();

    var responseText = await response.Content.ReadAsStringAsync();
    return JsonConvert.DeserializeObject<MCPResponse>(responseText);
}
```

### 6. 更新 Dispose 邏輯
在 `Disconnect()` 和 `Dispose()` 中處理 `HttpClient` 釋放：

```csharp
public void Disconnect()
{
    _cts?.Cancel();
    IsConnected = false;

    if (_httpClient != null)
    {
        try { _httpClient.Dispose(); } catch { }
        _httpClient = null;
    }
    else
    {
        try { _stdin?.Close(); } catch { }
        try { if (_process != null && !_process.HasExited) _process.Kill(); } catch { }
    }
}
```

## 檔案變更

### ClaudeCodeWPF/Services/MCP/MCPClient.cs
- 新增 `_httpClient` 欄位
- 重構 `ConnectAsync` 分支為 stdio/HTTP
- 新增 `ConnectHttpAsync()` 方法
- 重構 `SendRequestAsync` 分支為 stdio/HTTP
- 新增 `SendHttpRequestAsync()` 方法
- 更新 `Disconnect()` 處理 HttpClient

### README.md & README.en.md
- 更新版本號至 0.1.5
- 新增 v0.1.5 changelog

### ClaudeCodeWPF/Properties/AssemblyInfo.cs
- 版本號從 0.1.4.0 更新至 0.1.5.0

## 使用方式

在 MCP 設定中：
1. 選擇類型為 "Http" 或 "Sse"
2. 在 URL 欄位填入 MCP 伺服器端點，例如：
   ```
   https://localhost:8000/mcp
   ```
3. 儲存並連接

## 技術細節

### MCP 傳輸協定
- **Stdio**: JSON-RPC over stdin/stdout (透過子程序)
- **HTTP**: JSON-RPC over HTTP POST (同步請求/回應)
- **SSE**: Server-Sent Events (長連接串流，本次實作視為 HTTP)

### SSL 憑證驗證
目前實作設定為接受所有憑證（包含自簽憑證），這對於本地開發環境是合理的。若需要正式環境的安全性，應：
1. 新增配置選項 `AllowSelfSignedCerts`
2. 只在該選項啟用時才繞過憑證驗證
3. 預設應驗證完整憑證鏈

## 測試建議

1. **Stdio 連接**（確保未破壞原有功能）
   - 使用任何 stdio MCP 伺服器測試（如 filesystem, github 等）
   - 確認工具列表正常載入
   - 確認工具呼叫正常執行

2. **HTTP 連接**（新功能）
   - 啟動 HTTP MCP 伺服器（可用 Python mcp 實作測試伺服器）
   - 使用 HTTPS 端點測試
   - 使用自簽憑證端點測試
   - 確認 initialize 請求成功
   - 確認 tools/list 請求成功
   - 確認 tools/call 請求成功

3. **錯誤處理**
   - 無效的 URL
   - 伺服器無回應
   - 憑證驗證失敗（若實作嚴格模式）
   - 網路超時（30秒）

## 已知限制

- 目前將 SSE 與 HTTP 視為相同處理（HTTP POST），真正的 SSE 長連接串流尚未實作
- 無 HTTP 請求的背景讀取迴圈（不像 stdio 有 ReadLoopAsync）
- 所有憑證都被接受（安全性考量）

## 未來改進

1. 實作真正的 SSE 串流連接
2. 新增配置選項控制憑證驗證策略
3. HTTP 連接支援 Keep-Alive
4. 支援 WebSocket 傳輸（若 MCP 規範支援）
5. 新增連接重試機制
6. 新增連接健康檢查（heartbeat）
