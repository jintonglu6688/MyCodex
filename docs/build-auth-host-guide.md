# 编译包含 Gemini thought_signature 修复的 auth-host

## 前置条件：安装 Rust 环境

### 方法一：使用 rustup（推荐）

1. 下载安装程序：https://rustup.rs/
2. 运行安装程序，选择默认选项
3. 安装完成后，重启 PowerShell
4. 验证安装：
   - rustc --version
   - cargo --version

### 方法二：使用 Scoop
scoop install rust

### 方法三：使用 Chocolatey
choco install rust

## 编译步骤

### 1. 确保源码已更新
cd MyCodex-auth-host
git fetch origin
git log --oneline -1

### 2. 编译 Windows 版本
cd MyCodex-auth-host
cargo build --release --bin mycodex-auth-host

编译成功后，可执行文件在：
target\release\mycodex-auth-host.exe

### 3. 验证编译结果
.\target\release\mycodex-auth-host.exe --version-json

### 4. 替换 MyCodex 的 auth-host

找到 MyCodex 安装的 auth-host 位置：
- %LOCALAPPDATA%\MyCodex\auth-core\bin\mycodex-auth-host.exe
- 或 %APPDATA%\MyCodex\auth-core\bin\mycodex-auth-host.exe

备份原文件后替换。

## 注意事项

1. 版本检查：编译出的版本仍然显示 0.3.10，但包含了修复
2. 兼容性：需要确保 MyCodex 客户端版本 >= 8.0.3
3. 如果编译错误，尝试 cargo clean 后重新编译

## 如果遇到问题

- 确保安装了 MSVC 构建工具
- 或运行：winget install Microsoft.VisualStudioBuildTools
