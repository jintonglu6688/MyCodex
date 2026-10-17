# MyCodex

MyCodex 是 [Codex App Server](https://developers.openai.com/codex/) 的 Windows 桌面客户端，使用 .NET Framework 4.8 WinForms 开发，重视键盘操作和读屏软件支持。

MyCodex 是独立的第三方项目，与 OpenAI 没有隶属或背书关系。

## 功能

- **多种执行目标**：本机 Windows（内置、系统或自定义 Codex 运行时）、WSL、通过 SSH 连接的远程 macOS 和 Linux。每个目标独立运行，配置和会话互不影响。
- **编码与 Office 两种模式**：同一窗口里分别处理代码项目和文档类工作。
- **认证中心**：管理 ChatGPT 账号和第三方 API 提供商，支持把多个提供商聚合成一个模型列表。由 [MyCodex-auth-host](https://github.com/jintonglu6688/MyCodex-auth-host)（基于 CC Switch）提供。
- **插件、技能与 MCP**：浏览和安装插件市场中的扩展。
- **Git 与 GitHub**：查看改动、提交、推送和处理拉取请求。
- **手机远程**：通过 [MyCodex Relay](https://github.com/jintonglu6688/MyCodex-relay) 和 [Android 客户端](https://github.com/jintonglu6688/MyCodex-android) 在手机上继续对话。
- **无障碍**：支持 NVDA、争渡、保益悦听等读屏软件朗读，所有功能都可以用键盘完成。
- **在线更新**：程序和内置运行时可以自动更新。

使用说明见 [`help/用户手册.html`](help/用户手册.html)（英文版 [`help/UserManual.en.html`](help/UserManual.en.html)）。

## 相关仓库

| 仓库 | 说明 |
|---|---|
| [MyCodex](https://github.com/jintonglu6688/MyCodex) | Windows 桌面客户端（本仓库） |
| [MyCodex-auth-host](https://github.com/jintonglu6688/MyCodex-auth-host) | 认证后端，CC Switch 分支 |
| [MyCodex-relay](https://github.com/jintonglu6688/MyCodex-relay) | 手机远程连接用的中继服务 |
| [MyCodex-android](https://github.com/jintonglu6688/MyCodex-android) | Android 远程客户端 |

## 构建

需要 Windows、Git、Windows PowerShell，以及能构建 .NET Framework 4.8 WinForms 项目的 .NET SDK / MSBuild（含 net48 目标包）。

首次检出后还原一次测试项目，它引用了全部应用项目：

```bat
dotnet restore tests\MyCodex.Application.Tests\MyCodex.Application.Tests.csproj -p:Platform=x64
```

日常构建和测试：

```bat
dotnet build src\MyCodex.WinForms\MyCodex.WinForms.csproj -c Debug -p:Platform=x64 -p:MyCodexChannel=Dev --no-restore -v:minimal
dotnet test tests\MyCodex.Application.Tests\MyCodex.Application.Tests.csproj -c Debug -p:Platform=x64 --no-restore -v:minimal
```

直接构建不会准备认证后端和内置 Codex 运行时。完整开发版用 `build-dev.bat`，它会下载并校验固定版本的认证后端、Codex 运行时和 Relay。安装包用 `package.bat`，需要 Inno Setup 6。

更多约定见 [`AGENTS.md`](AGENTS.md) 和 [`CONTRIBUTING.md`](CONTRIBUTING.md)。

## 许可

MyCodex 按 [MIT 许可证](LICENSE) 发布。仓库中包含或构建时获取的第三方组件按各自许可使用，见 [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md)。

## 安全问题

请不要在公开 Issue 中报告安全漏洞，处理方式见 [`SECURITY.md`](SECURITY.md)。
