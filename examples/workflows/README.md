# 组合动作样例

这些 Schema v3 文件可在 Tool Center 的组合动作工作台中导入。导入文件始终按外部来源审查；采用后需要保存，并在执行前重新批准当前插件版本和能力计划。

## Base64 编码与解码往返

`base64-roundtrip.workflow.json` 演示声明驱动的 Text 输出绑定：

1. `base64.encode` 编码固定文本并返回 `result`。
2. `base64.decode` 将前一步 `result` 绑定到自身文本输入。

固定文本属于持久化输入，首次保存时 Tool Center 会要求确认。执行报告不会保存运行时输出值。

## JSON 与 URL 四步往返

`json-url-roundtrip.workflow.json` 演示跨插件 Text 输出链：

1. `json.minify` 压缩固定 JSON。
2. `url.encode` 绑定并编码压缩结果。
3. `url.decode` 绑定并还原 URL 编码结果。
4. `json.format` 绑定并重新格式化 JSON。

每一步只引用已经成功的前序步骤，任一步失败都会停止后续执行。
