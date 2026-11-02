# Unity 官方 MCP 安装与连接记录

日期：2026-11-01（Asia/Shanghai）。目标工程：E:/YatoUIFramework，Unity 6000.5.6f1。

## 已安装

- Unity 官方 CLI 1.0.0-beta.14：C:/Users/xyc/AppData/Local/Unity/bin/unity.exe。来自 unity.com/install.ps1，按官方版本清单校验 SHA-256；安装器已加入用户 PATH，新终端生效。
- Unity 官方 Pipeline 0.8.0-exp.1：由 CLI 安装到当前工程的 Packages/manifest.json，UPM 从 https://packages.unity.com 解析。
- 官方 MCP 使用 CLI 的 stdio 模式，由 Codex 启动；Unity Editor 本地 Pipeline 服务监听 127.0.0.1:7800。无需另启 Python/Node 中转服务。

## Codex 配置

C:/Users/xyc/.codex/config.toml 已添加：

```toml
[mcp_servers.unity]
command = 'C:\Users\xyc\AppData\Local\Unity\bin\unity.exe'
args = ["mcp", "--project-path", "E:/YatoUIFramework"]
startup_timeout_sec = 60
tool_timeout_sec = 120
```

已有 unityMCP（http://127.0.0.1:8088/mcp）保留，设置 enabled=false。安装前 Codex 配置和原包清单副本保存在 C:/Users/xyc/.codex/backups/unity-mcp-20261101。

保持 Unity 工程打开，重启 Codex 后加载新增工具。MCP 名称为 unity。配置固定当前工程；其他工程需要对应的 Pipeline 包与项目路径。

## 实测与版本配套

- CLI 最新别名当时返回旧 0.1.0-beta.3，缺少 pipeline/mcp 命令；因此按官方发行说明明确安装 1.0.0-beta.14。
- 首次安装 Pipeline 0.9.1-exp.1 时，MCP tools/list 返回重复的 get/create/status 等工具名，console 调用因命令歧义失败。没有修改 Unity 官方包源码；改用官方 0.8.0-exp.1。
- 最终 MCP initialize、tools/list 成功；160 个工具名称全部唯一。
- 实际调用 editor_status、get_scene_hierarchy、console_status、console 均成功；编辑器 ready、compiling=false，compilationFailed=false。读取到当前未保存场景的 Main Camera。
- 控制台保留了版本配套排查时的两个命令路由错误；它们不是 C# 编译错误，也没有把早先失败计为成功。
- 验证 JSON：Logs/Unity-Official-MCP-Verification.json（本地忽略文件）。不包含登录凭据。

可使用绝对路径执行：

```powershell
& C:/Users/xyc/AppData/Local/Unity/bin/unity.exe pipeline list --json
& C:/Users/xyc/AppData/Local/Unity/bin/unity.exe command editor_status --project-path E:/YatoUIFramework --json
codex mcp get unity
```

## 官方参考

- https://docs.unity.com/en-us/unity-cli/replace-mcp-server-unity-cli
- https://docs.unity.com/en-us/unity-cli/unity-pipeline/unity-pipeline-package
- https://docs.unity.com/en-us/unity-cli/use-unity-cli
- https://learn.chatgpt.com/docs/extend/mcp?surface=cli

本次仅安装开发连接工具，没有添加 Yato Survivor 内容。
