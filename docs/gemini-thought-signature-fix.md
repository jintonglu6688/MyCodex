# Gemini thought_signature 修复方案

## 问题诊断

**错误信息：**
`
Function call is missing a thought_signature in functionCall parts
`

**根本原因：**

MyCodex 客户端 8.0.3 绑定的认证后端 uth-core-v0.3.10 **不包含** Gemini thought_signature 的修复。

- 修复提交：2906db6 (2026-10-16)
- auth-core-v0.3.10 发布：2026-10-19
- **问题：** f2906db6 未被包含在 auth-core-v0.3.10 版本中

## 修复内容

提交 2906db6 修复了以下内容：

1. **codex_chat_common.rs**: 新增 copy_tool_call_extra_content() 和 	ool_call_extra_content() 函数
2. **codex_chat_history.rs**: 在历史存储中保留 extra_content 字段
3. **streaming_codex_chat.rs**: 流式响应中传递 extra_content
4. **transform_codex_chat.rs**: 转换时将 extra_content 复制回 tool call

## 解决方案

### 方案一：等待官方更新（推荐）

联系 MyCodex 作者，请求发布包含修复的 auth-host 新版本。

### 方案二：手动编译 auth-host

如果你有 Rust 开发环境，可以从源码编译包含修复的 auth-host：

`ash
cd MyCodex-auth-host
git checkout main
# 确保 f2906db6 已在当前分支
cargo build --release
`

### 方案三：使用上游 CC Switch v4.x

CC Switch v4.0.20 已包含完整修复，但需要修改 MyCodex 以使用不同的后端。

## 临时 workaround

如果暂时无法更新 auth-host，可以尝试：

1. **避免使用工具调用** - 不使用 exec_command 等需要思考签名的工具
2. **使用非流式请求** - 某些情况下非流式请求可能绕过此问题
3. **检查代理配置** - 确保没有使用 Cloudflare Worker 等中间代理

## 相关文件

- 修复提交: https://github.com/jintonglu6688/MyCodex-auth-host/commit/f2906db6
- MyCodex 仓库: https://github.com/jintonglu6688/MyCodex
- auth-host 仓库: https://github.com/jintonglu6688/MyCodex-auth-host

## 状态

- [ ] 向 MyCodex 作者报告此问题
- [ ] 等待官方发布包含修复的新版本
- [ ] 测试修复后确认问题已解决
