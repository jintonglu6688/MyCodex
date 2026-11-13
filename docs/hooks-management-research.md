# Hooks 管理调研与初步实现方案

调研日期：2026-11-13。范围：MyCodex 当前源码、官方 App Server 协议和 CLI 实现；未修改业务代码、运行 hooks 或改变用户信任。

官方最新稳定版本查询结果为 [rust-v0.167.2](https://github.com/openai/codex/releases/tag/rust-v0.167.2)。本次直接核对该版本的列表 schema 和 CLI 信任写法；更细的服务端实现固定到 main 提交 `e1340f2ea8d4b6a8577e734fe940fdcf46710514`；旧运行时兼容性另对照了 `rust-v0.167.0`（当前本地打包的运行时版本）、`rust-v0.160.0`、`rust-v0.150.0`。这不代表用户所有 Windows、WSL、SSH 运行时已经支持这些能力。下文明确区分协议事实、MyCodex 建议和待验证项。

## 结论

需要增加一套目标范围内的 Hooks 管理与审阅流程。仅接收运行通知无法解决新建或变更 hook 的信任问题：当前协议没有专用的 `hooks/changed` 或 `hooks/requestApproval`；官方 CLI 自行调用 `hooks/list` 发现需要审阅的条目。信任通过已有的 `config/batchWrite` 写入，运行通知则是单数 `hook/started`、`hook/completed`。[协议注册](https://github.com/openai/codex/blob/e1340f2ea8d4b6a8577e734fe940fdcf46710514/codex-rs/app-server-protocol/src/protocol/common.rs)、[CLI 启动审阅](https://github.com/openai/codex/blob/e1340f2ea8d4b6a8577e734fe940fdcf46710514/codex-rs/tui/src/startup_hooks_review.rs)

完整交付范围应包括：发现与状态解释、变更审阅、信任与撤销、独立启停、用户源配置维护、运行结果展示，以及各执行目标和旧运行时的处理。发现、指纹计算、信任判定和执行继续由 App Server 负责。

## 当前项目的接入点与缺口

| 当前源码 | 发现及影响 |
| --- | --- |
| [IAppServerExtensionClient](../src/MyCodex.Application/Interfaces/IAppServerExtensionClient.cs) | 仅组合 MCP、Skills、Plugins 接口，没有 hooks 清单和管理契约。 |
| [AppServerNotificationDispatcher](../src/MyCodex.Infrastructure/AppServer/AppServerNotificationDispatcher.cs) | `hook/started`、`hook/completed` 明确归入 Ignore；对应测试也要求忽略。 |
| [AppServerCanonicalActivityMapper](../src/MyCodex.Infrastructure/AppServer/AppServerCanonicalActivityMapper.cs) | `hookPrompt` 返回 null；不能把它当信任审批，更不能从名称猜测其交互契约。 |
| [AppServerNotificationFactory](../src/MyCodex.Infrastructure/AppServer/AppServerNotificationFactory.cs) | `configWarning` 调通用消息提取器，未读官方 `summary`，也未保留 `path/range`；只有 summary 的告警可能退化成泛化文本。 |
| [MainForm.SessionSettings](../src/MyCodex.WinForms/Forms/Main/MainForm.SessionSettings.cs) | 有 MCP、技能、插件管理入口，可以按同一布局接入 Hooks。导入外部配置后当前只刷新通用配置与会话。 |
| [AppServerClientOperations](../src/MyCodex.Infrastructure/AppServer/AppServerClientOperations.cs) | 已有 `config/read`、`config/batchWrite`、`expectedVersion`、`reloadUserConfig` 和文件操作；配置写方法当前丢弃响应，丢掉了 `ConfigWriteResponse` 的 `status/version/overriddenMetadata`，需要返回这些字段并回读验证有效状态。 |
| [AppServerConfigWriteGuard](../src/MyCodex.Application/Services/AppServerConfigWriteGuard.cs) | 可以复用用户配置层版本检查，不应写第二套盲目覆盖配置的逻辑。 |
| [AppServerThreadStartParameterBuilder](../src/MyCodex.Infrastructure/AppServer/AppServerThreadStartParameterBuilder.cs) | 创建线程时通过线程 `config` 覆盖注入 `projects.<path>.trust_level=trusted`；项目层信任与 hook 定义信任必须保持独立。该覆盖只作用于线程，`hooks/list` 的 cwd 模式看不到它（见下文）。 |
| [BrowserUsePluginSetup](../src/MyCodex.Infrastructure/BrowserUse/BrowserUsePluginSetup.cs)、[插件清单](../src/MyCodex.Infrastructure/BrowserUse/PluginAssets/plugins/browser-use/.codex-plugin/plugin.json) | 浏览器插件 `browser-use@mycodex-browser-use` 有 Stop、Interrupt、SubagentStop 的 `cua_repl.turn_ended` `mcp_tool` hooks；安装启用流程没有独立 hooks 审阅。该插件 ID 不在官方内置清理 hook 白名单内（见下文），按源码推断这 3 个 hook 在用户信任前为 `untrusted`、不会执行。尚未用实际运行时的 `hooks/list` 确认。 |

项目层次适合这项扩展：Application 放 DTO、状态和操作规划，Infrastructure 负责协议与目标路径，WinForms 负责界面。MainForm 只连接入口、生命周期和提示，不承载管理实现。

## 已核实的协议

### 清单和状态

`hooks/list` 接收 `cwds` 或 `threadId`，非空 `cwds` 与 `threadId` 互斥。空目录列表采用 App Server 自身 cwd；`threadId` 只接受已加载线程。响应为 `data[]`，每项有 `cwd`、`hooks`、`warnings`、`errors`，错误带 `path/message`；`errors` 只在目录模式的配置加载失败时填写，线程模式始终为空，线程侧问题只出现在 `warnings`。没有 `forceReload` 参数。目录模式重新发现目录配置，线程模式反映线程配置及其执行环境，不能把两者混用。[稳定版参数](https://github.com/openai/codex/blob/rust-v0.167.2/codex-rs/app-server-protocol/schema/typescript/v2/HooksListParams.ts)、[列表实现](https://github.com/openai/codex/blob/e1340f2ea8d4b6a8577e734fe940fdcf46710514/codex-rs/app-server/src/request_processors/catalog_processor.rs#L652)

目录模式以 `load_for_cwd(None, ConfigOverrides::default(), cwd)` 加载配置，不带任何请求级或线程级覆盖。MyCodex 在线程 `config` 中注入的项目信任因此不会进入目录模式：用户配置未信任的项目，其项目层 hooks 在新建线程前的目录检查中不可见，只能在线程创建后用 `threadId` 模式看到。这是源码确定的行为，不是偶发差异。

`threadId` 模式是较新加入的：`rust-v0.160.0` 与 `rust-v0.150.0` 的 `HooksListParams` 只有 `cwds`，`HookMetadata` 也没有 `environmentId`（`currentHash/trustStatus` 已存在）。参数结构未拒绝未知字段，旧运行时收到 `threadId` 不会报错，而是按空 `cwds` 返回 App Server 自身 cwd 的清单。`rust-v0.167.0` 的 `HookMetadata` 与 0.167.2 一致。

`HookMetadata` 包括 `key`、`currentHash`、`trustStatus`、`enabled`、`isManaged`、事件、matcher、来源、源路径、插件/执行环境标识、排序、超时、状态文字和处理器摘要。`trustStatus` 为 `managed/untrusted/trusted/modified`；handler 有 `command/mcpTool/prompt/agent`。metadata 不是完整定义，例如没有 MCP `input`，审阅必须允许查看原始来源。[稳定版 metadata](https://github.com/openai/codex/blob/rust-v0.167.2/codex-rs/app-server-protocol/schema/typescript/v2/HookMetadata.ts)

官方文档说明用户、项目、插件与受管来源可同时参与发现；项目层还受项目信任控制。受管 hooks 由策略信任，不提供用户禁用操作。启用、信任、项目是否加载、功能总开关和处理器支持情况需要分别解释。[官方 Hooks 文档](https://learn.chatgpt.com/docs/hooks)

官方文档未提到的一点：服务端有一份内置清理 hook 白名单（`codex-rs/plugin/src/bundled_hooks.rs`），列出若干 `*@openai-bundled` 插件（以及 `browser@openai-curated-remote`）在 Stop、Interrupt、SubagentStop 上调用指定 MCP 工具的 hook。命中白名单的条目 `trustStatus` 恒为 `trusted`、`enabled` 恒为 true，写 `hooks.state` 也改变不了；管理界面应显示为“内置”，不提供信任、撤销或禁用操作。白名单按插件 ID、事件和目标工具精确匹配，MyCodex 自带的 `browser-use@mycodex-browser-use` 不在其中。[内置白名单](https://github.com/openai/codex/blob/e1340f2ea8d4b6a8577e734fe940fdcf46710514/codex-rs/plugin/src/bundled_hooks.rs)、[状态判定](https://github.com/openai/codex/blob/e1340f2ea8d4b6a8577e734fe940fdcf46710514/codex-rs/hooks/src/engine/discovery.rs#L1000)

### 信任、启用与撤销

沿用官方 CLI 的信任写法，并在 MyCodex 增加已观测用户配置层的 `expectedVersion`：

```json
{
  "method": "config/batchWrite",
  "params": {
    "edits": [{
      "keyPath": "hooks.state",
      "value": {
        "<hooks/list 返回的 key>": {
          "trusted_hash": "<用户本次审阅的 currentHash>"
        }
      },
      "mergeStrategy": "upsert"
    }],
    "expectedVersion": "<用户配置层版本>",
    "reloadUserConfig": true
  }
}
```

`key/currentHash` 必须原样使用。对象合并保留其他 hooks 和当前 hook 的 `enabled`；启停使用相同对象合并，只写 `enabled`。不能替换整张 `hooks.state` 表，也不能把任意 key 直接拼接成 dotted keyPath。[稳定版 CLI 信任实现](https://github.com/openai/codex/blob/rust-v0.167.2/codex-rs/tui/src/hooks_rpc.rs)、[HookStateToml](https://github.com/openai/codex/blob/e1340f2ea8d4b6a8577e734fe940fdcf46710514/codex-rs/config/src/hook_config.rs)

插件 `currentHash` 可能是复合值，服务端写配置时拆成 `trusted_hash/trusted_content_hash`，客户端不要自行拆解或重算。普通 hook 的定义指纹不能被解释为所有外部脚本内容的完整性保证；插件另有内容指纹和执行验证。[发现与散列](https://github.com/openai/codex/blob/e1340f2ea8d4b6a8577e734fe940fdcf46710514/codex-rs/hooks/src/engine/discovery.rs#L831)、[插件信任验证](https://github.com/openai/codex/blob/e1340f2ea8d4b6a8577e734fe940fdcf46710514/codex-rs/hooks/src/engine/plugin_trust.rs)

撤销没有专用 RPC。通用配置 writer 对独立 edit 的 `value:null` 执行 `clear_path` 删除该路径并持久化，因此方案是一次 batch 删除该 key 的 `trusted_hash` 和 `trusted_content_hash`，保留 `enabled`；使用 `mergeStrategy:replace`。hook key 形如 `{来源}:{事件}:{组序号}:{处理器序号}`，用户来源包含文件路径，必然带 `.`，因此 key 段必须写成引号段，并对 `"` 与 `\` 加反斜杠转义（`parse_key_path` 支持这种写法），例如 `hooks.state."<转义后的 key>".trusted_hash`。这是依据 writer 源码提出的实现，尚未实际调用验证，需要契约测试；不要在 upsert 对象里嵌套 null 来猜测删除效果。[配置写入、删除与路径解析](https://github.com/openai/codex/blob/e1340f2ea8d4b6a8577e734fe940fdcf46710514/codex-rs/app-server/src/config_manager_service.rs)

信任状态来自 user/sessionFlags，而非项目或插件自授信任。配置写入只允许当前用户配置；高层覆盖可能让写入结果不等于最终状态。`config/batchWrite` 的响应 `ConfigWriteResponse` 已给出 `status`（`ok`/`okOverridden`）、`overriddenMetadata` 和写后的用户层 `version`：先用它们解释覆盖并更新已观测版本，再重新读取列表确认有效状态或失败。[状态合并规则](https://github.com/openai/codex/blob/e1340f2ea8d4b6a8577e734fe940fdcf46710514/codex-rs/hooks/src/config_rules.rs)

### 生效与运行通知

`reloadUserConfig:true` 会请求刷新已加载线程，内部包含 `refresh_hooks`，不需要默认重启整个 App Server。它不代表撤回已启动的 hook，也不保证所有正在执行的回合都在相同时间切换配置；UI 应区分“已保存”“当前配置已确认”“既有执行可能仍使用旧配置”。[配置刷新](https://github.com/openai/codex/blob/e1340f2ea8d4b6a8577e734fe940fdcf46710514/codex-rs/app-server/src/request_processors/config_processor.rs#L393)、[线程 hooks 刷新](https://github.com/openai/codex/blob/e1340f2ea8d4b6a8577e734fe940fdcf46710514/codex-rs/core/src/session/config_refresh.rs#L218)

当前公开功能键为 `features.hooks`，内部枚举名才是 `CodexHooks`；当前注册为 Stable、默认开启。`hooks/list` 和两种运行通知没有 experimental 标记，但旧运行时仍可能不认识它们。不要用 MyCodex 现有的“modern protocol”笼统推定支持。[feature 注册](https://github.com/openai/codex/blob/e1340f2ea8d4b6a8577e734fe940fdcf46710514/codex-rs/features/src/lib.rs)、[协议注册](https://github.com/openai/codex/blob/e1340f2ea8d4b6a8577e734fe940fdcf46710514/codex-rs/app-server-protocol/src/protocol/common.rs)

运行通知形状为 `{threadId, turnId?, run}`。run 包含运行 id、来源、事件、执行模式、状态、时间和 entries；状态包括 running/completed/failed/blocked/stopped。异步 hooks 不发这两个通知；没有专用全量运行历史查询接口。[运行摘要 schema](https://github.com/openai/codex/blob/e1340f2ea8d4b6a8577e734fe940fdcf46710514/codex-rs/app-server-protocol/schema/typescript/v2/HookRunSummary.ts)、[官方 App Server 文档](https://learn.chatgpt.com/docs/app-server)

## 建议的用户流程

### 发现变更并引导审阅

1. 当前目标连接或重连、打开工作区、新建/恢复会话前、插件变更或外部配置导入完成后，做一次合并去重的清单检查。新线程前用明确 cwd，已加载线程补充 threadId 视角。
2. 发现启用但 `untrusted/modified` 的项，显示持久状态：“有 N 个 Hooks 需要审阅，审阅前不会执行”，提供“审阅 Hooks”。首次发现当前指纹集合时可提示“现在审阅／稍后”；稍后不改变信任，入口一直保留。
3. 不靠解析英文 warning 判断安全状态；`configWarning` 用来完整显示错误、触发一次合并刷新，真实状态以 `hooks/list` 为准。补齐 `summary/details/path/range` 映射。
4. 去重键包含目标 ID、配置/线程上下文、hook key、currentHash；迟到响应绑定连接代次，切换或关闭后不得更新另一个上下文。后台目标提示按 `InteractionRequestPromptPlanner` 处理，避免抢焦点或连续弹窗。
5. 运行期间外部文件变更可以复用 `fs/watch` 触发去抖后的目录检查；同时保留显式刷新和发送前的轻量复核，不能只依赖 watcher。新建源文件须监听已知配置目录，插件变更使用安装/更新完成信号。旧 runtime 不支持 watch 时明确降级为这些检查点。

当前项目临时注入的项目 trust override 会让两种视角确定性地不一致：新线程前的 cwd 发现不含该覆盖，看不到用户配置未信任项目的项目层 hooks。新线程前的提示只能覆盖用户、插件、受管及已受信项目来源，不能声称“已检查全部 hooks”；创建/恢复后必须用 `threadId` 视角补核对。不要为让列表一致而偷偷永久信任项目。已错过的 SessionStart 不自动重放，不承诺信任后补执行过去的生命周期事件。

### Hooks 管理窗口

设置菜单增加“Hooks 管理…”，与 MCP、技能、插件管理并列。窗口固定所属执行目标，明确当前工作区/会话上下文；Coding 与 Office 均可进入。

| 区域/动作 | 行为 |
| --- | --- |
| 清单与筛选 | 原生 ListView 展示事件、处理器、来源、启用状态、信任状态；筛选待审阅、已信任、禁用、受管。错误单独显示，不把读取失败当空列表。 |
| 详情与查看来源 | 显示 matcher、超时、命令或 MCP 工具、来源路径、插件/环境、指纹；可只读打开完整定义。来源不足以完成审阅时不给出误导性的“已完整检查”提示。 |
| 信任当前版本 | 用户明确点击后，只提交本次审阅的 key/hash；审阅期间再次变化则要求重新审阅，不自动取最新指纹批准。批量操作只包含明确选中的项。 |
| 启用/禁用 | 与信任独立。启用未信任项时说明仍不会执行并提供审阅；信任已禁用项不自动启用。 |
| 撤销信任 | 删除用户信任记录，保留配置和 enabled；回读若仍被上层覆盖，解释原因。受管项不可操作。 |
| 全局功能状态 | 展示实际 `features.hooks` 及策略来源；允许写的用户设置才提供总开关，不能把它描述成可绕过组织策略。 |
| 刷新与应用状态 | 展示加载警告、保存冲突和有效状态；刷新保持选中项和焦点，不因选中条目发请求或执行代码。 |

采用现有 `UiControlFactory`、主题控件、传统菜单和中英文资源。每个操作有短 AccessibleName、明确 TabIndex、唯一助记键，窗口可调整大小。状态用可读文本，不能只用颜色或图标。审阅源码始终作为文本打开，复用已有防止脚本通过系统默认动作被执行的策略。

### 用户源配置的新建、编辑、删除

这部分属于文件维护，不能虚构 `hooks/create/update/delete` RPC，也不能让 `config/batchWrite` 写项目 hooks.json。

- 用户和项目 `hooks.json`：提供新建 command/MCP hook、编辑和删除定义；围绕事件、matcher、处理器、超时等做小型表单，保留高级原文编辑入口。保留 matcher 分组和 handler 等嵌套层的未知字段、其他 matcher 分组及原有编码/换行；未知未来 handler 只读展示。`hooks.json` 顶层是 `deny_unknown_fields`，只允许 `description` 与 `hooks`，顶层出现其他字段时整个文件会解析失败，保存前校验应按此报错，而不是当作可保留字段。
- 用户 config.toml 内联定义：优先利用 App Server 允许的配置编辑能力，版本保护下只修改相关键；项目 TOML 走目标文件编辑，不把整份 TOML 转写成新格式。
- 插件来源：提供来源查看和插件管理入口，日常禁用/信任写用户状态，不直接改已安装缓存包。编辑插件由其正常源码与发布流程承担。
- system/MDM/cloud/requirements 来源：只读并显示管理者来源。配置解析失败时保留原文件，展示路径和错误，允许用户打开可写来源修复。
- 删除的是选定定义，不顺带删脚本、整个 hooks.json 或其他 hook。有备份/恢复能力；保存之前验证格式，保存之后重新发现，内容变化重新审阅。

复用 `AppServerFileWindowManager`、`AppServerFilePreviewForm`、`IAppServerFileSystemClient` 和现有路径映射。当前编辑器依赖外部变更通知、保存采用 UTF-8 字节，不能直接宣称具有原编码保留或原子的版本比较写入；新增 hooks 编辑需要补相关保护。通用 `fs/writeFile` 不能假装有配置 API 的 `expectedVersion`。如果强原子冲突保护需要桥接实现，Windows 与 Unix 各自使用目标文件操作，WSL/Remote Unix 共享 Unix core，并保留路径、链接、允许根和错误处理边界。

### 运行可见性

移除对两种运行通知的忽略，按 target/thread/run.id 更新已有活动时间线，显示正在执行、耗时、完成、失败、阻止或停止。`turnId` 为空仍须可见，历史回放不重新播提示；重连后没有终态的运行标为未知/连接中断，不能一直显示正在运行。

不把所有 entries/context 原文自动写入日志或聊天，可能包含敏感命令和上下文。常规显示必要摘要，用户主动打开详情；沿用脱敏规则。异步 hooks 或连接前发生的运行没有事件证据时显示观测限制，不能以“没有通知”判断没有执行。`hookPrompt` 的展示另按确认过的 item schema 决定，不接入普通命令审批。

## 实现落点

| 层 | 建议变更 |
| --- | --- |
| Application | 新增小型 `IAppServerHookClient`、清单/详情/运行 DTO；增加 `HookManagementService` 与审阅去重、配置 edit 规划。复用 `IAppServerConfigClient`、`AppServerConfigWriteGuard`，不存第二份信任数据库。 |
| Infrastructure/AppServer | 增加 `AppServerHookMapper` 与 hooks 操作类，读 JSON 用现有 reader；AppServerClient 仅薄转发，保持 transport、请求 ID、超时和断连契约。补运行通知与配置警告映射，必要的事件 DTO 扩展保持既有消费者兼容。 |
| WinForms/Forms/Extensions | Hooks 管理、详情/审阅、源编辑入口，以及专门的窗口协调器。MainForm 只接菜单和生命周期；共享审批提示策略但不冒充服务端审批请求。 |
| 配置与文件 | 信任/启停复用用户配置写入，安全编辑复用目标文件服务。CLI 与 MyCodex 使用同一目标用户配置时自然共享信任；不同目标/不同 CODEX_HOME 不跨写。 |
| 兼容性 | 按连接探测 `hooks/list` 与必要字段；method-not-found 显示不支持并停止重复调用。`threadId` 模式不能靠 method-not-found 判断：旧运行时会静默忽略它并返回 App Server cwd 的清单，必须核对响应 `cwd` 与线程 cwd 一致，不一致即视为不支持线程视角。缺 key/hash、未知 trust 状态不能当已信任；连接更换重新探测。 |
| 自带插件 | `browser-use@mycodex-browser-use` 的清理 hooks 不在官方白名单内，安装/更新后应纳入待审阅流程；是否在安装时经用户确认直接信任 MyCodex 自带 hook 的当前 hash，需要单独决定，不能静默写入信任。 |

信任操作完整顺序为：读取列表和用户配置层版本 → 展示具体定义 → 用户选择 → 复核 key/hash → 只写用户已审阅的 hash，携带 expectedVersion → reload → 回读有效状态。配置冲突时保留用户选择并要求刷新复核，不自动覆盖；中途断连且写入结果未知，重连后先读状态再决定是否重试。列表与写入不是一个原子操作，hash 绑定与写后复核共同防止意外批准新定义；不要声称消除了所有文件并发风险。

## 实施顺序与验收

1. 先完成可交付的信任闭环：协议探测、清单、完整警告、待审阅提示、详情、信任/撤销、启停、配置冲突保护和多目标隔离。直接解决本次报告的问题。
2. 补齐来源维护与变更发现：可写用户/项目源的创建编辑删除、插件导入联动、目录观察、保存验证与恢复。完成日常管理。
3. 补齐运行可见性：同步执行摘要、错误与阻止原因、无 turnId 场景、重连与历史去重、异步观测限制。

以上三步是完整目标的实施顺序，第一步完成不等于已经交付完整管理。暂不建设独立 hook 执行器、任意“试运行”按钮、全量日志数据库或自有信任算法。

必要测试：

- Mapper/契约：两种列表上下文、空值和未知枚举、command/MCP、来源错误、完整 `configWarning`、运行无 turnId、重复和乱序通知、`ConfigWriteResponse` 的 `okOverridden`、旧运行时忽略 `threadId` 后返回错误 cwd。
- 信任安全：首次/修改后信任、用户已看 hash 与当前 hash 不同、插件复合 hash、启停保留信任、信任保留禁用、撤销两个信任字段、特殊字符 key（含 `.`、`"`、`\`、`:` 和 Windows 路径）、受管拒绝、内置白名单项拒绝、expectedVersion 冲突、上层覆盖和写后失联。
- 发现流程：首次连接、重连、工作区切换、已加载/未加载会话、项目 trust override、插件更新、外部源变更、新建配置文件、导入后审阅；重复检查不重复弹窗。
- 源维护：格式错误不覆盖原文件、保留未知字段/编码/换行、删除只作用于目标定义、外部并发修改、符号链接/路径越界、只读受管和插件源。
- 目标矩阵：Windows、WSL、Remote Mac SSH、Remote Unix SSH；Coding/Office、前台/后台、目标关闭与迟到响应、旧 runtime 降级。
- UI：键盘全流程、焦点恢复、屏幕阅读器、可调整尺寸、高 DPI、助记键和中英文资源。

更新现有 `AppServerNotificationDispatcherTests`、`AppServerClientMessageTests`、`AppServerNotificationFactoryTests` 及相关活动时间线/源守卫测试；新增重点服务和 planner 行为测试，fakes 同步窄接口。涉及 MainForm 功能代码时按仓库规则先做实际责任提取；只在用户授权提交时拆分提交，不因本研究自行提交。涉及源健康指标下降则降低 ratchet。

## 尚需实际验证

- 对最终选定运行时生成 schema，并在隔离测试配置下验证撤销删除语义、插件复合指纹、刷新失败和状态覆盖；本次源码调研不能替代实际 RPC 验收。
- 不同生命周期事件在正在进行回合中的配置快照时点；不要先承诺“立刻全部生效”或“必定下回合才生效”。
- 线程专属环境下 cwd 与 threadId 列表的其余差异，以及用户编辑源文件后的显式 reload 路径。项目信任覆盖造成的差异已由源码确认，不再列为待验证。
- 在实际运行时用 `hooks/list` 确认 `browser-use@mycodex-browser-use` 的 3 个清理 hooks 当前为 `untrusted`，以及它们未执行时对浏览器会话清理的实际影响。
- 用户当前每个执行目标的版本、有效 hooks 配置和实际屏幕阅读器体验，本轮均未读取或操作。

本轮验证限于当前源码、官方文档和官方协议/CLI 源码交叉核对、研究文档审阅；复核时补充了内置白名单、cwd 模式无覆盖、旧运行时参数差异、写入响应字段和 `hooks.json` 顶层字段限制。没有运行构建和测试，也没有发布或提交。
