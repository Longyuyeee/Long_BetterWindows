# 插件人工验收回执

本目录只保存人工验收回执，不保存截图、视频、剪贴板内容、ETL 或其他原始质量产物。

回执必须由根目录 `approve-plugin-manual-evidence.ps1` 在审阅人完成真实操作后生成。脚本要求：

- 跟踪工作区干净；
- 明确的插件与人工检查 ID；
- `-ConfirmPassed`；
- 审阅人和结论说明；
- 至少一个位于 `artifacts/quality/` 的本地原始证据文件；
- 当前 Release 宿主、插件 Manifest 和 Git 提交。

回执记录原始证据的仓库相对位置、大小和 SHA-256，但原始文件继续保持本地、不提交 GitHub。`verify-plugin-positive-matrix.ps1` 会验证回执、Manifest、命令集合和产品源码是否自审批提交后发生变化；任何不一致都会让该人工项保持未通过。

不要手工伪造或复制其他提交的回执。重新验收时使用 `-Replace`，并重新检查全部原始证据。
