# DSH 便携安装包（一台新电脑从零到跑起来，只需双击一次）

## 本文件夹内容
| 文件 | 作用 |
| --- | --- |
| `install-dsh.cmd` | **首次部署双击的文件**：全自动装 Node/pnpm/DSH 并建桌面快捷方式 |
| `setup.ps1` | 安装主逻辑（被上面调用，也可手动执行；pwsh 7 / 5.1 均可） |
| `DSH.exe` | **托盘 + 状态面板控制壳**（日常使用）：可见状态窗 + 系统托盘；X=进托盘，托盘「退出」才停服务 |
| `DSH.cs` / `build-dsh-exe.cmd` | DSH.exe 源码 + 一键重新编译（GUI 版，用本机自带 csc，图标嵌 app.ico） |
| `app.ico` + `tray_*.ico` | 图标集：EXE/窗体/快捷方式 = `app.ico`；托盘按状态切 `tray_running/starting/error/offline`（由 `gen-icons.ps1` 从 `icon-src` 生成；源图母版在 `D:\DSH\资源图`） |
| `whale_base.png` | Header Logo 源图（保留 PNG，供后续界面 Header 使用） |
| `gen-icons.ps1` / `png-to-ico.ps1` | 图标批量生成器 / 单图转 ICO 工具（pwsh 7 优先，5.1 亦可） |
| `migrate-to-dsh-app.cmd` + `migrate-dsh-app.ps1` | 目录分离迁移（薄壳 cmd 优先 pwsh 7、回退 5.1，安全重链） |
| `pack-for-other-pc.cmd` + `pack-for-other-pc.ps1` | **发布打包**：白名单方式，只含运行所需（工程记录/开发工具/构建产物不打包），并把工作区根的 Node 安装包放进 `redist\` 供离线安装（cmd 优先 pwsh 7） |
| `..\docs\` | **工程文档在「工作区根」，不属于本包**：架构 / 构建验证 / 排障 / 已知坑 / 变更记录 / 台账 / 接口 / ADR（入口见工作区根 `AGENTS.md`） |
| `start-dsh.cmd` | 备用启动器（控制台版，仅当无法编译 DSH.exe 时救急） |
| `make-shortcut.ps1` | 桌面快捷方式生成脚本（自动优先指向 DSH.exe） |
| `package.json` | 锁定 `@deepseek-ai/dsh@0.1.1-rc.2`（勿删） |
| `pnpm-workspace.yaml` | pnpm≥10.32/11 配置：`allowBuilds` 原生模块构建白名单（勿删） |
| `.npmrc` | 下载源：npmmirror 镜像 + 重试容错 |
| `pnpm-lock.yaml` | 安装后生成；连同复制可保证依赖版本一致 |
| `..\docs\ledger.md` | 资产台账：清单/当前状态/更新策略/待办 |
| `..\docs\changelog.md` | 变更记录（Keep a Changelog，按 BuildId） |
| `.tools\` | 安装后自动生成：便携 Node.js + 本地 pnpm（免安装、免管理员）；Node 优先来自包内 `redist\`，无需联网 |
| `logs\` | 运行日志（DSH.exe 启动后自动生成 `dsh-web.log`） |

## 新电脑要求
- Windows 10/11（64 位）
- 能联网（**只有 pnpm 依赖需要联网**；Node.js 已随包放在 `redist\`，离线解包安装）
- **不需要**预装 Node.js / pnpm，也不需要管理员权限
- **不需要 Visual Studio Build Tools / Python**：所有原生模块（`node-pty`、`koffi`、`sharp`、`node-addon-require-builtin`）都随包提供 **win32-x64 预编译二进制**（N-API），`pnpm install` 只下载、不编译

### 关于 Node 安装包（`redist
ode-*.msi`）
- **默认（推荐）**：`install-dsh.cmd` 自动用 `msiexec /a` 把它**便携解包**到 `.tools
ode` —— 不装进系统、不写 PATH、不需要管理员。
- **备选**：你也可以自己双击这个 MSI 正常安装 Node（会进系统并加入 PATH），装完再跑 `install-dsh.cmd`，脚本会自动优先使用 PATH 里的 Node（≥ v20 即可）。
- ⚠️ **两种方式都不要勾选 "Tools for Native Modules"**：它会额外下载 Chocolatey + Python + Visual Studio Build Tools（数 GB、要管理员），而本项目原生模块全是预编译二进制，装了也没用（依据见 `..\docs\known-issues.md` KI-10）。

## 使用方法
1. 把整个文件夹复制到新电脑**任意目录**（`node_modules`、`.tools`、`logs` 不用带，会按需重建/生成）。
2. 双击 `install-dsh.cmd`，等它跑完（首次约几分钟）：
   - 检查/下载便携 Node.js（最新 LTS）到 `.tools
ode`
   - 在 `.tools\pnpm-home` 装一份本地 pnpm（不动系统）
   - 用镜像源 `pnpm install` 装 DSH 依赖并编译原生模块
   - 自动在桌面创建 "DSH" 快捷方式（指向 DSH.exe）
3. 日常使用（托盘模式）：
   - 双击桌面 "DSH" → 弹出**状态窗**（服务状态/前端地址/PID/运行时长/BuildId）
   - 点 **「打开前端」** → 浏览器打开网页界面（服务刚启动时，dsh 也可能自行开一次页）
   - 点状态窗 **×** → 只是最小化到系统托盘，**服务继续运行**
   - 托盘右键 **「退出（停止服务）」** → 停止后端并退出程序
   - 需要看后端日志：托盘菜单「打开日志文件 / 打开日志目录」

## 发布与更新其他电脑

**打包（本机）**：双击 `pack-for-other-pc.cmd` → 生成发布包 `D:\DSH-Publish\DSH-App`。
**白名单打包**：只放运行所需（源码 + 构建/安装脚本 + 图标 + 依赖清单 + `DSH.exe` + `README.md`）；
**工程记录（`docs\`、`AGENTS.md`）、开发工具（探针/图标生成/迁移脚本）、构建产物（`DSH-old.exe`、`*.bak-*`）一律不进包**。
同时把工作区根的 `node-v*-x64.msi` 复制进 `redist\` → 目标机**离线装 Node**（`setup.ps1` 用 `msiexec /a` 解包，无需管理员）。

| 场景 | 在新电脑做什么 | 联网 |
|---|---|---|
| **首次安装** | 拷 `D:\DSH-Publish` 过去 → 进 `DSH-App` 双击 `install-dsh.cmd`（装便携 Node/pnpm → 镜像 `pnpm install` 重链依赖 → 建桌面快捷方式） | ✅ 一次 |
| **日常更新**（只改了 `.cs`/脚本/文档/图标） | 覆盖改动过的文件 → 双击 `build-dsh-exe.cmd` 本地重建 | ❌ 不需要（用系统自带 csc） |
| **依赖有变**（`package.json` / `pnpm-lock.yaml`） | 覆盖后重跑 `install-dsh.cmd`（或 `setup.ps1`） | ✅ |

补充：
- 发布包**已带 `DSH.exe`**，首次安装后可直接用；本地重建只是为了消除“未知发布者”提示（本机生成的文件没有 MOTW 标记）。
- 若提示“来自其他计算机”：右键 → 属性 → 勾选“解除锁定”。
- 工作区默认 = `DSH-App` 的上级目录（保持文件夹名 `DSH-App` 即生效）；要指定别的，在 `DSH-App` 里放一行 `workdir.txt`。
- 想沿用本机账号/会话/API 配置：把 `C:\Users\<用户名>\.dsh` 复制到新机同路径。
- 核对版本：状态窗 BuildId 与 `docs\ledger.md`/本文档一致即同版。

## DSH.exe（托盘壳）做了什么
- 运行时不出现 cmd / powershell / 控制台窗口；后端 node 隐藏运行，输出写入 `logs\dsh-web.log`；
- **真实用量与成本（BuildId -21 / 累计口径 -23）**：直接读 DSH 后端的明文投影缓存 `~/.dsh/storages/session_projcache.json`（`tokenUsage.totals`：未缓存输入 / 输出 / 缓存读 / 缓存写），按 `%LOCALAPPDATA%\DSH\pricing.json` 的**峰谷价**算钱（DeepSeek 自 2026-08-17 起峰谷计费：高峰 2×、周末低谷；默认值=低谷价，可自行编辑），每分钟把增量落 `usage-history.jsonl`。余额卡显示「今日消费 / 近 1 小时 / **累计 · N 天** / 预计可用」（累计总量与起始日期存 `%LOCALAPPDATA%\DSH\usage-state.json`，首启按已有会话总量播种），用量卡显示「近 1/3/6 小时成本 + 输入/输出/缓存读 tokens」。**不需要改后端、不需要 zstd**；
- **桌面宠物（BuildId -21，可调项 -23）**：把 `assets\deepseek-whale-standalone.html` 的鲸鱼喷水动画**矢量移植**到 WPF（SVG 几何 1:1、动画公式照搬），透明置顶小窗漂浮在桌面；拖动可移动（位置记忆）、双击开控制台、右键＝托盘同款菜单、服务停止时闭眼"睡觉"；任务/计划变化与"可能等待审批"会以**气泡**弹出并引导回网页确认；设置页可关闭，并可调**宠物速度**（0.2–3.0×，快慢完全由你定）与**工作时水花**（0–60%，任务运行时的水花大小；任务完成时喷到 100% 持续 10 秒、其它提醒 80%、**异常结束/中断只提醒不喷（水花平滑退回无任务档，约 2 秒）**；**审批提醒会一直挂着直到你处理**，空闲时回到 12% 以下；「工作中」由 projcache 的 `openStep` 判定，3 秒一采样）；
- **备份与迁移（BuildId -26，保留策略 -27）**：设置页「备份与迁移」→「立即备份」把 `~/.dsh`（配置 / 凭据 / 会话 / 附件）打成 `logs\backups\dsh-backup-<时间戳>.zip`（**排除**可重建的插件依赖与投影缓存；**含会话记录（全部对话）、配置、附件、模型设置，以及壳自身的用量/成本/通知/偏好数据**；**API 凭据默认不进包**，需要时在「备份」页开「备份包含 API 凭据」），带 `manifest.json`；「从备份恢复」先自动备份现状，再**只补缺失文件、绝不覆盖**，非 DSH 归档会被拒绝；服务运行时自动停 → 恢复 → 重启。侧栏「备份」页列出全部归档（类型/时间/文件数/体积）可单独恢复或删除，设置页「备份保留」控制只留最近 1–20 份（默认 5，超出自动进回收站）；
- **壳内审批（BuildId -31）**：总览页「审批」卡列出后端正在等待批准的工具调用（工具名 / 原因 / 时间），可直接「拒绝」或「允许一次」（一键生效，「拒绝」已实机验证通过）；读的是后端事件下行 `ws://127.0.0.1:3080/api/events.mux`（只读，同一条连接也用于判断任务开始/完成），决策走 `POST /api/respond`；**只有挂着 6 秒还没人答的审批才会提醒**（应答方秒答的不打扰），处理完（壳内或网页任一处）自动清掉它自己的通知与托盘红点。连不上时自动回退为"通知 + 引导回网页"，不会静默失败；
- **通知历史 + 托盘红点（BuildId -29）**：任务变化、预算告警等通知会落 `%LOCALAPPDATA%\DSH
otifications.jsonl`（最多 500 条），侧栏「通知」页按时间倒序查看、未读带蓝点，可「全部已读 / 清空」，点任意一行即标记该条已读并跳回对应会话；**回到某个会话发言时，该会话的通知自动已读**；有未读时托盘图标**右上角**加小红点（右下角留给服务状态点：绿/黄/红/灰；图标按托盘真实尺寸 16/24/32px 合成、取原生帧，PNG-in-ICO 帧自解码见 KI-13）、tooltip 追加「N 条未读」；
- **预算与定价（BuildId -28）**：侧栏「预算」页可直接编辑每日/每月预算与告警阈值（进度条正常蓝 / 接近上限黄 / 超限红，0 = 不启用），越过阈值或超限时宠物气泡 + 事件流提醒（同一天同一级别一次）；定价区可改汇率、低谷时段、周末全低谷与每个模型的四项单价和峰值倍数，保存即写回 `pricing.json` 并重算，可一键恢复默认（保留汇率）；
- **会话库（BuildId -25）**：侧栏「会话」页合并 projcache 与 `~/.dsh/sessions` 磁盘文件，列出「时间 · 轮次/步数 · tokens · 成本 · 体积」，支持按标题/id 实时搜索；**导出**为 zip（保留 `sessions/<工作区>/<会话id>/` 原始结构，**导入可原样还原**）；**删除**移入回收站（可还原），并在服务停止时清掉缓存行、随后自动重启服务让网页列表同步——删除正在进行的对话会二次提醒；
- **一键诊断包（BuildId -24）**：设置页「诊断 → 生成诊断包」或托盘菜单「生成诊断包」→ 在 `logs\` 生成 `diagnostics-<时间戳>.zip`，内含人可读 `summary.txt` + 环境 / 服务与端口属主 / 进程 / 依赖完整性 / 用量摘要 / 最近事件 / 日志尾部；**已脱敏**（不含 API Key、凭据文件与会话正文，用户名替换为 `<user>`），单文件默认远小于 5MB；
- **UI（BuildId -9）**：按 `DSH_UI_Redesign_Spec.md`（工作区根）重构为 Windows 11 Fluent 单页卡片布局：Header（鲸鱼 Logo + DSH/DeepSeek Harness）→ 服务状态（状态点）→ Service 卡片（Endpoint/PID·Build/打开 DSH/重新启动）→ Balance 卡片（DeepSeek 大字号余额 + ↻ 刷新 + 明细/更新时间）→ 最近消费卡片（1/3/6h 估算）→ 底部一行托盘提示；移除传统 TabControl；
- **深色 UI（与 DSH 网页前端同源）**：色板 / 层级背景 / 描边 / 圆角 / 字号全部取自前端设计 token（`@deepseek-ai/dsh-client-ui-theme` 的 `--dsw-*` 暗色值：`#151517` 底、`#232324` 卡片、`#ffffff1f` 描边、`#679efe` 品牌蓝、12px 圆角、11/12/13 字号阶）；Win11 22H2+ 自动启用系统 **Mica** 背景（DWM 硬件合成，窗口不做透明层，文字依旧清晰）；Win10 / 旧版自动回退不透明深色；滚动条为深色细条（10px 轨道 / 6px 胶囊，运行时 XAML 模板，失败自动回退系统默认）；
- 托盘（**原生 Win32，零 WinForms 依赖**）：`Shell_NotifyIcon` + HwndSource 隐藏消息窗路由鼠标事件；**左键单击/双击＝打开控制台**；右键＝**WPF 自绘圆角菜单**（打开 DSH / 打开控制台 / 刷新余额 / 重新启动服务 / **停止·启动服务** / 日志 / **通知** / **会话库** / **备份历史** / **预算与定价** / **生成诊断包** / 设置 / 退出；**开机启动已移到设置页**）；图标按状态切 `tray_running/starting/error/offline`，Tooltip 运行态含余额；服务停止时出系统气泡提示；
- **服务控制**：Hero 卡片「停止服务 / 启动服务」按钮与托盘菜单项同步；**外部实例占用 3080 时也能停止**（先弹确认框，显示 PID、进程路径、命令行、父进程是否存活，确认后用 taskkill /T /F 结束该进程树）；手动停止后状态显示「服务已手动停止」而非"异常"；
- **不再产生孤儿后端（JobKill）**：后端 node 是子进程，Windows 不会因父进程死亡而回收它——DSH.exe 崩溃 / 任务管理器结束 / 被强制 kill 时，`node bin.js web` 会残留并一直占着 3080，导致之后每次启动都显示"由其他实例运行"。现在启动后立即把子进程加入 **Job 对象（`JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`）**，本进程无论怎么退出（含崩溃）内核都会一并结束它；启动时还会用 WMI 读出 3080 属主的 PID/命令行/父进程状态并显示在「最近事件」里，停止确认框也一并显示；
- **外观（浅色 / 深色）**：设置页可切换，配色为 DSH 网页前端同源 token，切换即时生效（brush 原地改色，不重建窗口），偏好存 `%LOCALAPPDATA%\DSH\ui-settings.txt`。**浅色为天蓝系**：底 `#EFF6FF`（不透明，避免 Mica 把浅色拖灰）、卡片纯白、侧栏 `#E9F2FF`、hero `#E1EDFF`、描边 `#DCE7F7`；**Mica 只在深色启用**；
- **滚动顶部渐隐**：滚动区顶部叠 26px 渐变遮罩（内容淡出而非被硬切），遮罩颜色随主题/卡片背景自动跟随；日志卡内滚动区同样处理；
- **文字渲染**：小字（11–13px）用 `Display` 格式化 + **ClearType**（像素对齐、笔画实心，解决"毛刺/扭曲"）；大标题（≥18px）用 `Ideal` + Grayscale（柔和、无彩色描边）。**关键：ClearType 需要不透明背景**——卡片与侧栏已改为完全不透明（`#232324` / `#1B1B1C`），否则 WPF 会自动降级为灰度抗锯齿（这就是之前"到处都毛刺"的原因）；Mica 仍从窗口底层的半透明层透出；标题类用 Segoe UI Variable Display + SemiBold、侧栏选中态 Medium、深色主文字 `#edeff3`；
- 窗口关闭（X）= 隐藏到托盘继续服务；只有托盘「退出」才真正停止服务；
- 余额刷新失败**不清零**：保留上次成功值并标注"更新失败"；
- 停止时用 `taskkill /T /F` 结束整棵进程树，不残留 node 子进程。

## 杀毒软件提示怎么办（背景说明）
告警通常来自三个"可疑特征"：`powershell -ExecutionPolicy Bypass` 字符串、脚本自动联网下载并执行、以及文件带"来自其他计算机"（MOTW）标记。托盘版日常启动已完全避开脚本执行。若仍有个别提示：
1. **解除锁定**：右键 DSH.exe / .cmd → 属性 → 勾选"解除锁定"（新电脑复制过来必做）；
2. **本地重新编译**：在新电脑上双击一次 `build-dsh-exe.cmd`，用本机自带编译器生成 DSH.exe——本地生成的文件没有 MOTW 标记，最干净；
3. **加信任白名单**（可选，仅在信任本文件夹时）：Windows 安全中心 → 病毒和威胁防护 → 排除项，加入 `D:\DSH`（及数据目录 `C:\Users\<用户名>\.dsh`）；
4. **彻底消除"未知发布者"**：需要代码签名证书（商业证书或自签证书并导入本机信任），DSH.exe 本身没有签名。

## 实现要点（为什么不用管理员/不污染系统）
- 便携 Node：下载官方 zip 解压到项目内，不写注册表、不进 Program Files。
- 本地 pnpm：用 node 自带的 npm 装到 `.tools\pnpm-home`，不用 `-g`。
- 启动器自动定位：优先 `.tools
ode
ode.exe`，其次 PATH——安装和运行永远用同一个 Node，避免原生模块 ABI 不匹配。
- 下载全部走 npmmirror（官方源在国内经常 ECONNRESET），node 二进制也从 npmmirror 拉，失败自动回落 nodejs.org。

## 常见问题
- **ECONNRESET / 下载失败**：多为网络抖动，直接重跑 `install-dsh.cmd`（可续传）。
- **pnpm 报 node-pty/koffi 编译失败（node-gyp）**：正常不会发生——本项目的原生模块全部是**预编译二进制**（见上文与 `..\docs\known-issues.md` KI-10）。只有在目标平台没有对应 prebuild 时才会回退编译，那时才需要装 "Visual Studio Build Tools"（含 C++ 桌面开发）与 Python。
- **镜像 404（版本未同步）**：临时改 `.npmrc` 第一行为 `registry=https://registry.npmjs.org/` 再重跑。
- **DSH.exe 编译报"文件被占用"**：说明 DSH 还在运行；托盘「退出」或关闭旧窗口后再试。
- **提示"文件来自其他计算机"**：右键文件 → 属性 → 勾选"解除锁定"。
- **想看原生模块编译产物**：它们不放在顶层，而在 `node_modules\.pnpm\<包名>@版本
ode_modules\` 里（node-pty 的 Windows 二进制是 `prebuilds\win32-x64\conpty.node`，预编译版，无需本地编译工具）。

## 注意
- **目录布局（工作区与软件分离，布局 A）**：`D:\DSH` 是工作区根（放你的工作文件、会话挂这里）；软件本体放进子文件夹 `D:\DSH\DSH-App\`（整个文件夹便携，可复制到其他电脑）。启动器给 DSH 后端指定的工作目录按此规则解析：优先读软件文件夹里的 `workdir.txt`（内容为一行目录路径）；否则当文件夹名是 `DSH-App` 时取它上一级；否则用自身——保证工作区始终是 `D:\DSH`。迁移请用 `migrate-to-dsh-app.cmd`（需先托盘「退出」DSH）。
- 桌面上的 `DSH.lnk` 不用复制：目标路径写死，安装脚本会按新位置自动重建。
- DSH 的会话 / 模型配置在 `C:\Users\<用户名>\.dsh`，**不随本文件夹迁移**；需要的话单独复制该目录。

## 版本与更新（保持各电脑一致）
**当前启动器 BuildId：`2026-09-02-47`**（见 DSH 状态窗 / `DSH.cs` / 本行；资产状态见 `..\docs\ledger.md`，逐条历史见 `..\docs\changelog.md`）

更新流程约定：
1. 源码改动都在本文件夹完成（主要是 `DSH.cs`、各 `.cmd`/`.ps1`、`README.md`、`package.json`）；
2. 每次改动 `DSH.cs` 必须同时**递增 BuildId** 并**重新编译** `DSH.exe`（`build-dsh-exe.cmd`）；
   - **不需要先退出 DSH**：脚本先编译到 `DSH.build.exe`，再自动换位——若 `DSH.exe` 正被占用，就把运行中的旧文件改名成 `DSH-old.exe`（Windows 允许重命名运行中的 exe），新构建落到 `DSH.exe`；运行中的实例不受影响，下次启动即新版；
3. 同步到其他电脑时，复制**整个文件夹**覆盖即可（跳过 `node_modules`、`.tools`、`logs`，可选跳过 `pnpm-lock.yaml`）；
4. 到新电脑后验证是否最新：状态窗显示 BuildId 是否与本文档一致；
   - 若不一致：在该电脑双击 `build-dsh-exe.cmd` 本地重建（也顺带消除"未知发布者"提示），重建后即一致。
5. 原则：**永远以 `DSH.cs` + 文档为真源**，`DSH.exe` 只是它的编译产物——任何电脑上"跑 build 脚本"都能回到与源码一致的状态。

## 开发与验收（给代理 / 维护者）

调度入口是工作区根 **`D:\DSH\AGENTS.md`**（精简，指向各分册）；细节见 `docs\`：`architecture.md`（架构与硬规矩）、`build-and-verify.md`（构建 + 三步验证）、`runbook.md`（排障）、`known-issues.md`（已知坑）、`changelog.md`、`ledger.md`、`interfaces.md`、`decisions\`（ADR）。常用三条命令：

| 命令 | 作用 |
|---|---|
| `build-dsh-exe.cmd` | 编译 `DSH.exe`（**运行中也安全**，自动换位：旧文件改名 `DSH-old.exe`） |
| `run-smoke-test.cmd` | 无界面冒烟测试：模板 seal / 按钮状态 / 主题切换 / 滚动条 / 端口属主 / JobKill |
| `render-preview.cmd` | 离屏渲染浅色 + 深色界面到 `logs\preview-*.png`（不弹窗，可用于验收 UI） |
