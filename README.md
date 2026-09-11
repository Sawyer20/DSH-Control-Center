# DSH 控制中心

![Windows 10/11](https://img.shields.io/badge/Windows-10%20%2F%2011-0078D4?logo=windows&logoColor=white)
![.NET Framework 4.8](https://img.shields.io/badge/.NET%20Framework-4.8-512BD4?logo=dotnet&logoColor=white)
![build csc](https://img.shields.io/badge/build-csc%20%C2%B7%20no%20MSBuild-2ea44f)
![License MIT](https://img.shields.io/badge/license-MIT-blue)

> **EN** — A portable Windows tray console for a local **DSH** backend (`dsh web`, default `http://127.0.0.1:3080`):
> start/stop the service, see **real token usage and cost**, answer approvals and questions without leaving the
> desktop, browse sessions, back up and restore, and get one-click diagnostics. No installer, no admin rights.

DSH 控制中心的托盘小程序，把本机的 DSH 后端管起来：**服务启停、真实用量与成本、审批与提问提醒、
会话库、备份恢复、诊断包**，桌面上还有一只会喷水的鲸鱼。便携，免安装、免管理员。
界面为中文；后端本体需自备（见[环境要求](#环境要求)）。

<img src="screenshots/overview.png" width="660" alt="总览">

## 主要功能

- **服务控制** — 启停/重启本机 `dsh web`，后端隐藏运行、日志进 `logs\dsh-web.log`；外部实例占用 3080 时也能停止（先确认）。后端进程加入 Windows Job 对象，壳崩溃也不会留下孤儿进程。
- **不管工作区** — 工作区是**按会话**由网页决定的（`session.create` 传 `workspaceId`，网页空状态就是「选择一个工作区开始」）。壳不参与管理，只保证后端有个**干净的兜底目录**：安装时自动建 `<安装目录>\default-workspace\`，网页没指定工作区的会话就落在这里（绝不会落进程序目录）。想确认"哪个会话在哪个工作区"，看**会话库**每行的标签。
- **用量与成本** — 直接读后端落盘的 `session_projcache.json`（不改后端）：今日 / 近 1 小时 / 累计·N 天 / 预计可用，近 1·3·6 小时成本与输入、输出、缓存读 tokens。
- **模型与成本** — 一页改「用哪个模型 + 它花多少钱」：改前端模型 ID（写进后端配置，**热加载**，新会话生效）、一键声明该模型是否支持图片输入、编辑高峰时段与各列单价、按现价重算历史成本。
- **通知与托盘** — 任务完成/中断、预算告警、审批、提问都落成通知历史（未读蓝点、点击跳回会话）；托盘图标按服务状态变色，有未读时右上角加红点。
- **审批与提问提醒** — 后端等待审批或模型向你提问时，壳内可直接允许/拒绝；**宠物会举着气泡等你回复**，处理完自动消红点。
- **会话库（按工作区区分）** — 列出全部会话（时间 · 轮次 · tokens · 成本 · 体积），每行带**工作区（项目）标签**，可按工作区筛选、也能按工作区搜索；支持导出 zip、原样导入还原、删除进回收站并同步网页列表。工作区本身由**网页**管理（`session.create` 传 `workspaceId`），壳不插手——它只告诉你"哪个会话在哪个工作区"。
- **备份与诊断** — `~/.dsh` 打包备份（**API 凭据默认不进包**），恢复只补缺失、绝不覆盖；一键生成脱敏诊断包便于排障。
- **桌面宠物** — 矢量移植的鲸鱼喷水动画：速度 0.2–3.0× 可调、任务完成喷到 100%、异常结束只提醒不喷、空闲回到低水花；可拖动、可关闭。

<img src="screenshots/model-and-cost.png" width="660" alt="模型与成本">

<img src="screenshots/notifications.png" width="660" alt="通知">

<img src="screenshots/pet.png" width="240" alt="桌面宠物">

## 环境要求

- **Windows 10 / 11（64 位）**（系统自带 .NET Framework 4.8）。
- 本机已能跑 DSH 后端：仓库**不含**后端本体，`install-dsh.cmd` 会按 `package.json` 用 pnpm 安装 `@deepseek-ai/dsh`（能否拉到取决于你的 registry）。
- 安装时需联网；**不需要**管理员权限、**不需要**预装 Node.js / Python / VS Build Tools。

## 安装

> **放在哪**（有两个实际影响）：
> - 建议放在一个**空目录**、路径尽量短，例如 `D:\DSH\`（解压后是 `D:\DSH\DSH-App\`）。
> - 不放 `Program Files` 这类需要管理员权限的位置；也别放进 OneDrive / 网盘同步目录：装完 `node_modules` 有约 196 MB、近 3 万个文件，实时同步很拖。
> - **路径别太深**：依赖里最长的一条路径已接近 250 字符，放到 `C:\Users\<名字>\Downloads\...` 这种长路径可能让 `pnpm install` 因超过 260 字符上限而失败。
> - 想换掉兜底的启动目录：在应用目录放一行 `workdir.txt`，内容写目标目录（如 `D:\我的项目`）。

1. 下载本仓库（`Code → Download ZIP`，或 `git clone https://github.com/Sawyer20/DSH-Control-Center.git`），解压到上面的位置。
2. 双击 **`install-dsh.cmd`**，等它跑完（首次几分钟）：下载便携 Node 到 `.tools\node` → 装本地 pnpm → `pnpm install` 装依赖 → 建桌面快捷方式 → 建好兜底工作区 `default-workspace\`。
3. 双击桌面 **DSH** 开始用。第一次打开网页会让你**选一个工作区**（或新建一个）——那才是你干活的地方；`default-workspace\` 只是"你还没选"时的落点。

> 若目录里放了 `redist\node-*.msi`，脚本会用它离线解包安装；没有就从网上下。
> 装 Node 时**不要勾选 "Tools for Native Modules"**（会拉 Chocolatey + Python + VS Build Tools，本项目用不到）。
> 只改了 `.cs` / 脚本时不必重装：覆盖文件后双击 `build-dsh-exe.cmd` 重建即可，也不需要联网。

## 日常使用

- 双击桌面 **DSH** → 状态窗（服务状态 / 前端地址 / PID / BuildId）→ **「打开 DSH」** 进网页界面。
- 窗口 **×** 只是最小化到托盘，**服务继续跑**；托盘菜单 **「退出（停止服务）」** 才真正停止后端。
- 托盘右键菜单：打开 DSH / 刷新余额 / 停止·启动服务 / 日志 / 通知 / 会话库 / 备份历史 / 模型与成本 / 生成诊断包 / 设置。

## 数据与隐私

| 位置 | 内容 |
|---|---|
| `~/.dsh` | 后端自己的配置、凭据、会话、附件（壳只读；写配置仅在你点「保存并生效」时发生） |
| `%LOCALAPPDATA%\DSH` | 壳自己的数据：价目表、用量历史、通知历史、主题与偏好 |
| `<安装目录>\logs` | 后端日志、备份 zip、诊断包 |

无遥测、无云端上报，所有连接只针对 `127.0.0.1`；备份与诊断包默认不含 API 凭据。

## 常见问题

- **杀毒提示**：右键 `DSH.exe` → 属性 → **解除锁定**；或在本机跑一次 `build-dsh-exe.cmd` 重新生成（本地文件没有"来自其他计算机"标记）。
- **`ECONNRESET` / 下载失败**：网络抖动，重跑 `install-dsh.cmd`（可续传）。镜像 404 就把 `.npmrc` 第一行改成 `registry=https://registry.npmjs.org/`。
- **换电脑**：复制整个文件夹（跳过 `node_modules`、`.tools`、`logs`）；要沿用会话与凭据，另外复制 `~/.dsh`。
- **网页让我「选择一个工作区开始」，是正常的吗？** 是。工作区（项目目录）由 DSH 网页按会话管理——第一次打开需要你添加或选择一个目录，之后每个会话都记着自己在哪个工作区。壳不参与这件事：它只保证后端有个干净的兜底启动目录 `<安装目录>\default-workspace\`，所以你**不选也不会把文件写进程序目录**。
- **`default-workspace\` 是什么？** 上面那个兜底目录，安装时自动建、空目录。正常用不到它；除非有入口绕过工作区选择直接建了会话，那种会话才会落在里面，并在网页的工作区列表里显示成同名工作区。
- **壳会动我的工作区吗？** 不会。壳只读网页的工作区注册表用于"会话库按工作区标注"，从不创建/删除/切换工作区，也不写 `workspace.json`。
- **遇到问题想反馈？请附上「诊断包」**：托盘右键或「设置 → 诊断 → 生成诊断包」，会在 `logs\` 生成 `diagnostics-<时间戳>.zip`。里面是环境 / 服务与端口属主 / 进程 / 依赖完整性 / 用量摘要 / 最近事件 / 日志尾部，**已脱敏**（不含 API Key、凭据文件与会话正文，用户名替换为 `<user>`），可以安全贴到 issue 里。
- **想拿一台机器上跑起来，用哪个包？** GitHub **Releases** 里的 zip（含 `DSH.exe` + 源码 + `install-dsh.cmd`，解压后进 `DSH-App` 双击安装脚本即可）；仓库的 `Code → Download ZIP` 是同一份内容，只是没有打包好的 Release 说明。

## 自己编译

代码级 WPF：无 XAML、无 MSBuild、无第三方库，用系统自带 `csc` 直接编译（C# 5）。源码即真源，`DSH.exe` 只是产物。

| 命令 | 作用 |
|---|---|
| `build-dsh-exe.cmd` | 编译 `DSH.exe`（运行中也安全：旧文件改名 `DSH-old.exe`，下次启动生效） |

改源码的约定：递增 `DSH.cs` 里的 `BuildId` → 重新编译 → 同步本 README 的版本行。

**当前 BuildId：`2026-09-02-59`**

## 许可证

[MIT](LICENSE) © 2026 Sawyer20

## 免责声明

第三方工具，与 DeepSeek 官方无隶属或背书关系；"DSH / DeepSeek" 等名称仅作指称性使用（说明本工具配套哪个后端）。
**MIT 覆盖本壳自有代码**；界面里的鲸鱼形象、图标与动画是**作者本人用 AI 绘图工具生成**的（非第三方素材，但 AI 生成内容的可版权性因法域而异）——逐项说明见 [`THIRD-PARTY.md`](THIRD-PARTY.md)。
后端本体不在本仓库内，由使用者自行安装，其许可与使用条款由提供方决定。请在符合当地法律与服务条款的前提下使用。
