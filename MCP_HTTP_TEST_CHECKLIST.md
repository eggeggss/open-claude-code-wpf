# MCP HTTP 連接測試檢查清單

## 編譯檢查
- [ ] 專案在 Windows 上成功編譯（需要在 Windows 環境測試）
- [ ] 無編譯警告或錯誤
- [ ] AssemblyInfo 版本號正確為 0.1.5.0

## Stdio 連接測試（回歸測試）
- [ ] 建立 stdio 類型的 MCP 伺服器配置
- [ ] 成功連接 stdio MCP 伺服器
- [ ] 工具列表正確載入
- [ ] 工具呼叫正常執行
- [ ] 斷開連接正常

## HTTP 連接測試（新功能）
- [ ] 建立 HTTP 類型的 MCP 伺服器配置
- [ ] 輸入 HTTP URL（例如：`http://localhost:8000/mcp`）
- [ ] 成功連接 HTTP MCP 伺服器
- [ ] 工具列表正確載入
- [ ] 工具呼叫正常執行
- [ ] 斷開連接正常

## HTTPS 連接測試（SSL 憑證處理）
- [ ] 建立 HTTPS 類型的 MCP 伺服器配置
- [ ] 輸入 HTTPS URL（例如：`https://localhost:8443/mcp`）
- [ ] 自簽憑證可正常連接（不報憑證錯誤）
- [ ] 工具列表正確載入
- [ ] 工具呼叫正常執行

## SSE 連接測試
- [ ] 建立 SSE 類型的 MCP 伺服器配置
- [ ] 輸入 SSE URL
- [ ] 成功連接（目前實作等同 HTTP）
- [ ] 工具列表正確載入

## 錯誤處理測試
- [ ] 無效 URL 格式顯示適當錯誤訊息
- [ ] 伺服器無回應時顯示超時錯誤（30秒）
- [ ] 連接失敗顯示清楚的錯誤訊息
- [ ] 錯誤不導致應用程式崩潰

## UI 測試
- [ ] MCP 設定頁面正常顯示
- [ ] HTTP/SSE URL 欄位可正常輸入
- [ ] 類型切換（Stdio/HTTP/SSE）正常
- [ ] 儲存設定後再載入，配置正確保存

## 驗證原報錯已修復
- [ ] 原錯誤「由於未提供憑證名稱，所以無法啟動該區」不再出現
- [ ] HTTP/SSE 連接成功建立
- [ ] 可正常使用 MCP 工具

## 效能測試
- [ ] HTTP 請求回應時間合理（< 5 秒）
- [ ] 無記憶體洩漏（多次連接/斷開後記憶體穩定）
- [ ] HttpClient 正確釋放

## 文件檢查
- [ ] README.md 更新至 v0.1.5
- [ ] README.en.md 更新至 v0.1.5
- [ ] Changelog 正確描述變更
- [ ] MCP_HTTP_IMPLEMENTATION.md 技術文件完整

## 建議測試環境

### 最小測試（必要）
1. Windows 10/11 環境
2. 至少一個 HTTP MCP 伺服器（可用 Python 快速搭建）

### 完整測試（建議）
1. stdio MCP 伺服器（例如：filesystem, github）
2. HTTP MCP 伺服器（可信任憑證）
3. HTTPS MCP 伺服器（自簽憑證）
4. 測試連接失敗情境（錯誤 URL、超時等）

## 簡易 Python HTTP MCP 伺服器範例

```python
from flask import Flask, request, jsonify

app = Flask(__name__)

@app.route('/mcp', methods=['POST'])
def mcp_handler():
    req = request.json
    method = req.get('method')
    
    if method == 'initialize':
        return jsonify({
            'jsonrpc': '2.0',
            'id': req['id'],
            'result': {
                'protocolVersion': '2024-11-05',
                'capabilities': {'tools': {}},
                'serverInfo': {'name': 'test-server', 'version': '1.0'}
            }
        })
    elif method == 'tools/list':
        return jsonify({
            'jsonrpc': '2.0',
            'id': req['id'],
            'result': {
                'tools': [
                    {
                        'name': 'echo',
                        'description': 'Echo test tool',
                        'inputSchema': {
                            'type': 'object',
                            'properties': {
                                'message': {'type': 'string'}
                            }
                        }
                    }
                ]
            }
        })
    elif method == 'tools/call':
        return jsonify({
            'jsonrpc': '2.0',
            'id': req['id'],
            'result': {
                'content': [
                    {'type': 'text', 'text': f"Echo: {req['params']['arguments'].get('message', '')}"}
                ]
            }
        })
    
    return jsonify({'jsonrpc': '2.0', 'id': req['id'], 'error': {'code': -32601, 'message': 'Method not found'}})

if __name__ == '__main__':
    app.run(port=8000, debug=True)
```

執行：
```bash
pip install flask
python test_mcp_server.py
```

然後在 WPF 應用程式中配置：
- 類型：Http
- URL：`http://localhost:8000/mcp`
