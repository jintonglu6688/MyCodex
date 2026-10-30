# 更新分发渠道调研

核实日期：2026-10-29。配额与政策可能调整。以下区分官方规则、当前文件验证与尚未证明的性能归因；没有修改现有发布地址或使用令牌上传测试附件。

## 当前结论

- GitCode 的账号级限速、平台上传瓶颈和本地上传实现之间，目前没有充分证据作出唯一归因。没有查到 Release 上传带宽付费升级的公开价目或提速承诺。
- 运行时可以优先评估直接使用国内 npmmirror 中已有的官方 npm 原包，省去再次上传大文件。当前稳定版、Windows x64 包以及 alpha 标签都已追平官方；稳定版原包已通过完整哈希验证。
- MyCodex 安装包的独立免费分发候选以 CNB Release 最值得试点。公开文档和匿名下载模式符合需求，但本轮没有测过该账号的上传速度。

## GitCode 上传路径与归因边界

`tools/gitcode/GitCodePublishing.ps1` 的 `Send-GitCodeReleaseFile` 先调用 `GET .../releases/:tag/upload_url`，再将文件以 PUT 和平台返回的请求头上传到签名存储地址，使用 `Invoke-WebRequest -InFile`，超时 900 秒。脚本没有主动带宽限速，也没有分别记录申请地址、文件传输、等待服务端回调和附件可见所耗时间。因此，发布总耗时不能直接等同于上传带宽。[官方上传接口](https://docs.gitcode.com/docs/apis/get-api-v-5-repos-owner-repo-releases-tag-upload-url/)。

本轮访问官方 Release 文档、帮助中心、关于页面及公开 sitemap，没有找到针对 Release 上传带宽的账号档位或付费提速规则。这表示公开证据不足，不能推断平台不存在私下报价，也不能认定账户受免费档限速。官方的模型 Token/核时计量规则属于另一类服务，不能当成附件提速套餐。[Release 操作](https://docs.gitcode.com/docs/help/home/org_project/project_manage/release_management/release_operations/)、[模型计量说明](https://docs.gitcode.com/docs/help/home/ai-community/models/billing/)。如向官方询问，应明确问 Release 签名 PUT 的限速、账号/仓库/IP 配额、并发和付费 SLA；官方关于页面列出工作日技术咨询渠道。[官方联系方式](https://docs.gitcode.com/docs/help/home/aboutus/)。

当前检查进程是 Windows PowerShell 5.1，进度偏好为 Continue，且系统有 curl.exe；项目上传函数未禁用进度。微软仓库记录过 Windows PowerShell 进度显示影响 Web 请求性能，但这不能证明本次 PUT 上传也由该问题导致。可在下一次已授权发布或专用测试仓库中，保持文件、网络及账号一致，比较现有脚本与 curl 的传输时间，不应先归因或购买套餐。[微软项目记录](https://github.com/microsoft/PowerShellForGitHub/issues/227)。

公开只读检查结果：GitCode 在线稳定清单返回 HTTP 200，应用版本 8.0.5、运行时 0.165.0；两种包分别请求前 1 MiB，均返回 HTTP 206、正确的 Content-Range，最终域名为 `file-cdn.gitcode.com`，本机分别耗时约 0.200 和 0.262 秒。它们只证明下载入口及范围请求正常，不能用于判断上传速度、账号限速或全国访问质量。

要区分原因，需记录一次授权上传的连接耗时、真实上传字节/速度、上传结束至响应返回的等待，以及附件可下载的时间。相同账号换客户端可查实现问题，保持客户端后换网络可查链路；只有在受控条件下比较另一账号，或得到平台明确配额说明，才足以判断账号差异。本轮没有进行这些远端写入测试。

## npmmirror 当前版本、同步与完整性

北京时间 2026-10-29 21:52 左右，对官方与镜像同时请求 `@openai/codex` 元数据，得到：

| 标签或包 | npm 官方 | npmmirror |
|---|---|---|
| latest | 0.165.0 | 0.165.0 |
| win32-x64 | 0.165.0-win32-x64 | 0.165.0-win32-x64 |
| alpha | 0.166.0-alpha.6 | 0.166.0-alpha.6 |
| alpha-win32-x64 | 0.166.0-alpha.6-win32-x64 | 0.166.0-alpha.6-win32-x64 |

可重复查询的来源：[官方 latest](https://registry.npmjs.org/@openai%2Fcodex/latest)、[镜像 latest](https://registry.npmmirror.com/@openai%2Fcodex/latest)、[官方完整元数据](https://registry.npmjs.org/@openai%2Fcodex)、[镜像完整元数据](https://registry.npmmirror.com/@openai%2Fcodex)。版本及标签均以查询时点为准。

官方 0.165.0 发布于北京时间 10 月 28 日 04:14:25，Windows x64 包于 04:18:27 发布；alpha.6 主包于 10 月 29 日 09:55:29、Windows x64 包于 10:00:33 发布。镜像也已有这些版本。不过，镜像的 `time`、`publish_time` 和 `_cnpmcore_publish_time` 保留上游发布时间，不能用时间相等推断同步只花零秒，也不能据此算出平均同步延迟。

镜像主页只承诺尽量实时同步，没有找到发布后固定多少秒/分钟同步完成的 SLA。底层 cnpmcore 支持上游 changes stream，但开源实现的能力与默认配置不证明生产服务具体延迟。故当前结论是“此刻已追平，包括当日发布的预发布版本”，不是“保证每次若干分钟同步”。[镜像主页](https://npmmirror.com/)、[同步机制文档](https://github.com/cnpm/cnpmcore/blob/master/docs/sync-setup.md)。镜像 latest 响应还声明 `Cache-Control: public, max-age=300`，短暂标签差异也可能包含缓存影响。

对 `https://registry.npmmirror.com/@openai/codex/-/codex-0.165.0-win32-x64.tgz` 完整流式下载并计算哈希，未保存到磁盘：

- HTTP 200，173,677,955 字节，本机当次耗时约 5.204 秒；这是下载测量，不是同步延迟或速度保证。
- SHA-256 为 `53ebec84d5a380bbb654a796e695576a4dbd2c228ec9bc02982f2013f261969d`，与当前 GitCode 在线清单和本地清单一致。
- SHA-512 与官方 npm `dist.integrity` 一致。
- 小范围下载也返回 HTTP 206，最终文件域名为 `cdn.npmmirror.com`。

镜像由 cnpmcore 驱动，阿里云赞助；异常高流量自动化访问可被限流。它适合这里已经在 npm 正式发布的 Codex 包，不应作为任意 MyCodex 安装包的上传空间。[运营说明及使用须知](https://github.com/cnpm/cnpmcore#npmmirrorcom)。

后续方案应由官方 npm 确定目标版本及 integrity，再核实镜像对应平台包已可下载、完整性正确，才发布版本固定的镜像 URL。镜像尚未追平时保留已验证版本，或使用官方/既有渠道；不应只信镜像 latest，也不把预发布 alpha 当稳定更新。现有发布流程本就校验原始 npm 包，改分发地址无需改变其格式和 SHA-256。

## 国内候选渠道比较

本地 `updates/stable.json` 中，安装包为 199,881,903 字节（约 190.62 MiB），Windows x64 runtime 为 173,677,955 字节（约 165.63 MiB）。二者合计约 373.56 MB；1,000 次各下载一份约消耗 373.56 GB 流量，未计重试。候选渠道必须支持无登录 HTTPS 文件下载，不能只有人工网盘下载页。现有清单允许 `asset.url` 使用绝对外部地址，仍须保留大小与 SHA-256 校验。

| 渠道 | 已确认的免费条件及限制 | 对当前更新包的判断 |
|---|---|---|
| 腾讯 CNB Release | 微信注册并实名后创建顶级组织；每顶级组织对象存储免费 100 GiB，制品、LFS、图片和附件共享；官方附件插件支持单文件 64 GB；可设永久保留 | 最值得先做试点。大小与自动发布能力符合，免费流量/公平使用上限仍需确认 |
| Gitee Release | 普通项目单附件 100M、仓库附件总量 1G；推荐项目总量 5G；GVP 项目单附件 200M、总量 20G | 普通免费项目两个包均超限。取得 GVP 后可能容纳当前包，但安装包接近上限，不能作为默认可用方案 |
| AtomGit | 官方已宣布与 GitCode 全面融合、账号和数据互通 | 不构成独立平台替代，不能用来排除 GitCode 同一链路的问题 |
| CODING DevOps | 免费标准版已下线，高级/旗舰版已停售，官方计划于 2028-09-30 全面停止服务 | 不作为新分发渠道；官方建议免费用户迁往 CNB |
| 又拍云联盟 | 官方帮助仍列联盟一年期 67 元代金券活动；旧联盟入口已返回 404 | 仅作申请询价候选，不能按旧文章承诺长期免费额度 |
| 七牛常规免费额度 | 10 GB 标准存储和 10 GB CDN 回源；CDN 免费外网流量明确是 HTTP | 现有更新器要求 HTTPS，不能把 HTTP 免费流量直接当成免费更新下载流量 |
| 阿里云 OSS、腾讯云 COS | 新用户限期试用，外网流量与存储分项计费 | 可作付费备用，不能称为中国大陆永久免费分发 |

CNB 的官方定价页面未单列下载流量收费项，但这不足以证明无限流量或无限速度。100 GiB 是组织共享容量，不是每月累积赠送；当前文档未列新用户试用到期日，但服务协议 3.1.1 明确保留取消或调整免费配额的可能，不能承诺永久免费。创建顶级组织需先微信注册及实名认证。其附件插件支持 OpenAPI 令牌及流水线发布，上传参数 `ttl` 默认为 0，表示永久保留；这仍以仓库和账号正常存在为前提。公开仓库与公开 Release 允许匿名访问。[CNB 定价](https://docs.cnb.cool/zh/pricing.html)、[服务协议](https://docs.cnb.cool/zh/saas/terms.html)、[官方附件插件](https://cnb.cool/cnb/plugins/cnbcool/attachments)、[角色权限](https://docs.cnb.cool/zh/guide/role-permissions.html)。

本轮对 CNB 官方 runner 的现有附件做了匿名、只读小范围请求：`https://cnb.cool/cnb/cool/orange-runner-doc/-/releases/download/v1.0.1/orange-runner-1.0.1-linux-amd64.tar.gz`，GET `Range: bytes=0-1023` 返回 HTTP 206、1,024 字节，经过一次 HTTPS 跳转，无账号、Cookie 或令牌。该结果证明这个公开样例可供普通 HTTP 客户端下载并支持范围请求；约 1.65 秒包含建连和跳转，不是吞吐量测速。应在清单中保存稳定的 Release 入口 URL，不保存跳转后的临时签名地址。[官方 runner 下载说明](https://cnb.cool/cnb/cool/orange-runner-doc)。

若试点 CNB，建议新建仅用于分发的公开仓库，保持 GitHub 源码仓库及现有 GitCode 清单入口。先验证同一约 200 MB 安装包的上传时间、匿名完整下载、SHA-256、重定向、旧客户端更新，再考虑让 GitCode 上的小清单指向 CNB 大文件。不要从文档可用直接推断上传更快，也不要直接替换旧客户端的固定清单入口。

Gitee 官方明确允许通过 Release 分发软件包和附件，并公布上述大小和容量限制；本轮未对其匿名直链、API 限流和下载流量做实测。即使申请 GVP 成功，也需复核 API 发布及未来包体增长。[创建 Release](https://help.gitee.com/repository/release/create)。AtomGit 官方说明全面融合，不能把换域名视为迁移到另一家服务商。[关于我们](https://docs.gitcode.com/docs/help/home/aboutus/)。

CODING 官方已公告：免费标准版 2025-09-01 下线，高级/旗舰版 2025-09-30 停售，计划 2028-09-30 全面停止服务，并明确建议免费用户转向 CNB。不能再使用历史“五人以下免费”、Generic 通用制品功能等旧资料推荐新建 CODING 分发。[CODING DevOps 系列产品公告](https://cloud.tencent.com.cn/document/product/1726/116235)。

七牛的免费标准存储、免费回源与面向最终用户的免费 HTTPS 流量是不同计费项；当前官方免费说明只列 HTTP 外网下载。没有核实到仍开放且覆盖 MyCodex 规模的开源专项赞助。又拍云官方帮助列一年期代金券，HTTPS 请求另行计费；旧文章中的“10 GB 存储 + 15 GB HTTP/HTTPS 流量，可续申”不能作为当前有效权益。申请前需确认可执行文件分发、额度、HTTPS 请求、域名要求和续期条件。[七牛免费额度](https://developer.qiniu.com/af/kb/1574/free-credit-information?category=kb)、[又拍云账号与费用](https://help.upyun.com/knowledge-base/%E8%B4%A6%E5%8F%B7%E5%8F%8A%E8%B4%B9%E7%94%A8%E7%B1%BB/)、[又拍云计费说明](https://www.upyun.com/price_instruction)。

阿里云 OSS 新用户额度为 20 GB 标准存储、2 GB 外网流量和 20 万次请求，期限三个月；中国香港及部分境外地区另有固定免费额度，不能混同为中国大陆额度。腾讯云 COS 官方当前列新用户 50 GB 标准存储、180 天，不能抵扣请求和流量。它们具备文件存储用途，但本轮不按永久免费候选推荐。[OSS 新用户免费试用](https://help.aliyun.com/zh/oss/free-quota-for-new-users/)、[COS 免费额度](https://cloud.tencent.com/document/product/436/6240)。

ModelScope 官方工具入口面向模型、数据集、Studio、MCP 和技能；未核实其对普通 Windows 安装包及第三方 CLI 镜像的公开分发授权、长期配额和服务保证。因此不把安装包放进“数据集仓库”的技术可行性当成正式分发方案。[ModelScope 官方平台技能](https://www.modelscope.cn/skills/modelscope/ms-hub)。GitHub、Cloudflare R2 等境外平台与国内第三方代理，也不属于已确认的中国大陆免费分发渠道。

## 与现有客户端衔接

`UpdateManifestClient.ResolveAssetUri` 支持绝对 HTTPS 包地址；`UpdatePackageDownloader` 下载后核对大小和 SHA-256。因此可保留现有 GitCode 稳定清单入口，仅将运行时包指向已验证的 npmmirror 固定版本，将安装包指向验证后的 CNB Release。这样旧客户端仍能通过原入口获取新地址。发布脚本目前仍按 GitCode 构造地址，下一步实施需要同步调整发布侧，并验证旧客户端。

当前 `UpdateAssetReference` 每包只有一个 URL，客户端没有自动多镜像回退能力；“同时发布多个镜像”不能被描述成已经实现自动容错。现有 `.gitcode/workflows/sync-standard-runtime.yml` 每天 UTC 19:00 触发，约北京时间次日 03:00；镜像同步速度与项目自身的更新发现频率是两件事，迁移下载源不会自动提高轮询频率。

本轮未注册账号、上传测试文件或修改发布参数。CNB 的实际上传速度、免费外网公平使用规则，以及各运营商完整包下载仍未验证。

## 后续实现（2026-10-29）

已按“直接跟随官方稳定版”修改客户端运行时链路：官方元数据优先，官方不可达时有界回退到镜像元数据；固定平台包和完整性快照；镜像下载失败或损坏时回退同版官方包。安装仍使用既有共享资源预留、暂存发布和在用版本保护。应用升级继续使用 GitCode，小运行时清单只用于兼容旧客户端，发布侧不再上传运行时 tarball。有关当前行为以源代码和 `docs/gitcode-update-repo.md` 为准；本报告前面的调研观察仍是当时的测量记录。
