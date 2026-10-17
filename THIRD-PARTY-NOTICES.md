# 第三方组件声明

MyCodex 自身代码按 [MIT 许可证](LICENSE) 发布。下列第三方组件不属于 MIT 授权范围，版权归各自所有者，按其各自条款使用和分发。

## 仓库中包含的文件

| 组件 | 位置 | 版权与许可 |
|---|---|---|
| PuTTY Plink | `runtime/putty/plink.exe` | Copyright 1997-2026 Simon Tatham 等，MIT 风格许可，全文见 [`runtime/putty/LICENCE.txt`](runtime/putty/LICENCE.txt) |
| NVDA Controller Client | `tts/nvdaControllerClient/nvdaControllerClient.dll` | Copyright NV Access Limited，GNU LGPL 2.1，见 <https://www.gnu.org/licenses/old-licenses/lgpl-2.1.html> |
| 争渡读屏接口 ZDSRAPI | `tts/ZDSRAPI/ZDSRAPI_x64.dll` | 争渡读屏公开发布的读屏控制接口，版权归其开发者所有 |
| 保益悦听控制接口 | `tts/byctrl/byctrl-x64.dll` | 保益悦听公开发布的读屏控制接口，版权归其开发者所有 |

读屏接口只用于把朗读文本交给用户已安装的读屏软件，MyCodex 不修改这些文件。

## NuGet 依赖

构建时由 NuGet 还原，不随仓库提交。

| 包 | 许可 |
|---|---|
| Markdig | BSD-2-Clause |
| Scintilla5.NET（含 Scintilla 与 Lexilla） | MIT；Scintilla/Lexilla 使用其自带的 Scintilla 许可 |
| Microsoft.Web.WebView2 | Copyright Microsoft Corporation，BSD 风格许可（见包内 `LICENSE.txt`） |

测试项目另使用 NUnit 等测试依赖，许可见各包。

## 构建或运行时获取的组件

以下组件不在仓库中，由构建脚本下载并校验哈希后放入安装包，或在运行时下载：

| 组件 | 来源 | 许可 |
|---|---|---|
| OpenAI Codex CLI 运行时 | npm `@openai/codex`，版本由 `tools/runtime` 固定 | Apache-2.0 |
| Git for Windows（MinGit） | git-for-windows 官方 Release，版本见 `tools/runtime/git-runtime.json` | GPL-2.0，源码见 <https://github.com/git-for-windows/git> |
| MyCodex 认证后端 | [MyCodex-auth-host](https://github.com/jintonglu6688/MyCodex-auth-host) 固定 Release（基于 [CC Switch](https://github.com/farion1231/cc-switch)） | MIT |
| MyCodex Relay | [MyCodex-relay](https://github.com/jintonglu6688/MyCodex-relay) 固定 Release | MIT |
| Computer Use 组件 | 构建时从本机已安装的官方 Codex 客户端复制 | 归 OpenAI 所有，按其条款使用 |

## 其他资源

- `sound/` 下的提示音和 `help/` 下的用户手册随 MyCodex 一起分发。
- “Codex”“ChatGPT”“OpenAI” 是 OpenAI 的商标。MyCodex 是独立的第三方客户端，与 OpenAI 没有隶属或背书关系。
