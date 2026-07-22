# 组合动作样例

这些 Schema v3 文件可在 Tool Center 的组合动作工作台中导入。导入文件始终按外部来源审查；采用后需要保存，并在执行前重新批准当前插件版本和能力计划。

## Base64 编码与解码往返

`base64-roundtrip.workflow.json` 演示声明驱动的 Text 输出绑定：

1. `base64.encode` 编码固定文本并返回 `result`。
2. `base64.decode` 将前一步 `result` 绑定到自身文本输入。

固定文本属于持久化输入，首次保存时 Tool Center 会要求确认。执行报告不会保存运行时输出值。
