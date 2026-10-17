# 参与贡献

欢迎提交 Issue 和拉取请求。

## 提交前

- 较大的改动请先开 Issue 说明想法，避免方向不一致。
- 按 [`.github`](.github) 中的模板填写 Issue 和拉取请求。
- 项目结构、分层规则、代码风格、本地化和无障碍要求见 [`AGENTS.md`](AGENTS.md)。

## 开发与测试

构建步骤见 [`README.md`](README.md)。提交前至少运行：

```bat
dotnet build src\MyCodex.WinForms\MyCodex.WinForms.csproj -c Debug -p:Platform=x64 -p:MyCodexChannel=Dev --no-restore -v:minimal
dotnet test tests\MyCodex.Application.Tests\MyCodex.Application.Tests.csproj -c Debug -p:Platform=x64 --no-restore -v:minimal
```

新功能和问题修复请附带测试。界面改动需要保持键盘操作和读屏朗读可用，并同时更新 `Strings.resx` 和 `Strings.zh-CN.resx`。

## 注意

- 不要提交 API 密钥、令牌、密码或个人路径。
- 不要提交构建输出（`debug`、`Release`、`artifacts`）和生成的运行时（`runtime/codex`、`runtime/auth-core`）。
- 提交的代码按本仓库的 [MIT 许可证](LICENSE) 授权。
