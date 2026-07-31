<p align="center">
  <a href="README.md"><kbd>简体中文</kbd></a>
  &nbsp;
  <a href="README_EN.md"><kbd>English</kbd></a>
</p>

# CS2-HanZipLine

video https://www.bilibili.com/video/BV1AoGN6XEX4/

面向 CS2 服务器的双向滑索插件。玩家可以在地图中放置滑索并按 `F` 乘坐；管理员可以将所有玩家共同布置的滑索保存为地图路线，在以后每次加载该地图时自动恢复。

当前版本：`v0.4.0`

## 功能

- 玩家自行创建、删除自己的滑索。
- 靠近任一端点按 `F` 乘坐；再次按 `F` 可主动下索。
- 管理员可创建 CT、T 和全局滑索，删除任意滑索，并保存或重载当前地图路线。
- 普通玩家创建的滑索自动归属其创建瞬间所在的 CT 或 T 队伍；普通玩家不能创建全局滑索。
- 可选择允许所有人使用全部滑索，或限制为仅本队与全局滑索。
- CT、T、全局滑索分别使用独立颜色；该颜色同时用于激光绳与管理员轮廓高亮。
- 可选 Bot 自动接近并使用允许乘坐的滑索。
- 支持单人数量限制、全局数量限制、冷却、距离限制、使用次数、存在时间和每回合清理。
- 地图路线按地图名称保存，支持管理员组织玩家共同布置后一次性保存。

## 前置条件

- 已安装并正常运行 SwiftlyS2 的 CS2 专用服务器。
- 使用发布包时无需自行编译；自行编译需要 .NET 10 SDK。
- 端点模型与音效事件必须是服务器可用资源。默认配置使用 CS2 自带资源。

## 安装

1. 下载 Release，或执行：

   ```powershell
   dotnet build CS2-HanZipLine.csproj
   ```

2. 将发布目录 `build/CS2-HanZipLine/` 内的全部内容复制到服务器的 SwiftlyS2 插件目录，并保留目录结构。

   ```text
   CS2-HanZipLine.dll
   CS2-HanZipLine.jsonc
   resources/
   ```

3. 重启服务器或按你的 SwiftlyS2 管理方式重新加载插件。
4. 修改 `CS2-HanZipLine.jsonc` 后，插件会自动应用有效的新配置；首次使用前建议先设置管理员权限。

> 不要只复制 DLL。`resources/translations/` 包含菜单和聊天提示翻译，缺失后玩家会看到翻译键或英文回退内容。

## 快速开始

### 玩家创建与乘坐

1. 站在可用地面附近，准星瞄准希望作为另一端端点的实体表面。
2. 输入 `!zipline`，在菜单中选择“创建滑索”；或直接输入 `!zipline create`。
3. 起点会放在你的附近地面，终点放在准星命中的表面。创建成功后靠近任一端点。
4. 按 `F` 前往另一端；乘坐中再次按 `F` 可主动离开。

创建失败时，聊天栏会说明原因，例如目标不是有效表面、附近没有可用地面、距离不符合限制、端点过近、冷却中或数量达到上限。

### 管理员保存玩家共创路线

这是推荐的地图布置流程：

1. 将 `MaxPerPlayer` 临时设为 `1`，让每位玩家各自放置一根适合路线的滑索。
2. 管理员检查完成后输入 `!zipline admin`。
3. 选择“保存当前地图滑索”。
4. 当前全部**已完成创建**的滑索都会保存，包括普通玩家放置的 CT/T 滑索和管理员放置的滑索。
5. 以后该地图加载时，保存的路线会自动生成。

地图文件保存在：

```text
<PluginDataDirectory>/maps/<地图名称>.json
```

## 命令

| 命令 | 谁能使用 | 作用 |
| --- | --- | --- |
| `!zipline` | 存活玩家 | 打开玩家菜单。 |
| `!zipline create` / `!zipline_create` | 存活玩家 | 按当前准星和站位创建一根本队滑索。 |
| `!zipline remove` / `!zipline_remove` | 存活玩家 | 删除准星指向的自己创建的滑索。 |
| `!zipline admin` / `!zipline_admin` | 管理员 | 打开管理员菜单。 |
| `!zipline_clear` | 管理员 | 立即清除当前全部活动滑索。 |
| `F` | 靠近端点的存活玩家 | 乘坐滑索；乘坐中再次按下可下索。 |

菜单内执行动作后不会自动关闭，可连续创建、删除或管理；使用菜单自身的 Tab/返回操作关闭或离开菜单。

## 管理员菜单

管理员权限由 `AdminPermissions` 控制。拥有其中任意一个权限标记的玩家可使用管理员菜单和清除命令。

| 菜单项 | 作用 |
| --- | --- |
| 创建 CT 滑索 | 无论管理员当前在哪个队伍，都创建一根固定归属 CT 的滑索。 |
| 创建 T 滑索 | 无论管理员当前在哪个队伍，都创建一根固定归属 T 的滑索。 |
| 创建全局滑索 | 创建所有队伍都可使用的滑索。仅管理员可创建。 |
| 删除准星所指的滑索 | 删除任意队伍、任意创建者的滑索。 |
| 保存当前地图滑索 | 保存当前所有已完成创建的活动滑索。 |
| 重载地图保存的滑索 | 清除当前滑索，并从已保存地图路线恢复。 |
| 清除所有当前滑索 | 清除当前地图中的所有活动滑索，不删除已保存地图文件。 |
| 显示/隐藏滑索架轮廓 | 显示或隐藏端点外发光。CT、T、全局滑索分别使用自己的颜色。 |

## 队伍、权限与颜色

每根滑索有一个固定归属：`CT`、`T` 或 `Global`。

| `AllowAllTeamsUseZiplines` | CT 玩家可用 | T 玩家可用 | Bot 行为 |
| --- | --- | --- | --- |
| `true` | 所有滑索 | 所有滑索 | 可使用任意滑索。 |
| `false` | CT 与 Global 滑索 | T 与 Global 滑索 | 遵循与玩家完全相同的限制。 |

- 普通玩家创建时，归属取创建瞬间所在队伍；之后换队不会改变该滑索归属。
- 管理员可通过管理员菜单明确选择三种归属。
- 保存后，滑索归属会一同写入地图文件；旧版地图文件没有队伍字段时按 `Global` 加载。
- `CTZiplineColor`、`TZiplineColor`、`GlobalZiplineColor` 支持 `R G B` 或 `R G B A` 格式，例如 `"80 180 255 255"`。
- 想要不区分颜色时，将三组颜色填写为同一个值即可。

## Bot 使用

默认关闭。开启前请先确认地图路线对 Bot 可到达。

```jsonc
"BotAllowUse": true,
"BotUseRange": 300.0,
"BotUseCooldownSeconds": 20.0,
"BotTargetTimeoutSeconds": 5.0,
"BotApproachSpeed": 450.0
```

| 配置 | 默认值 | 作用 |
| --- | ---: | --- |
| `BotAllowUse` | `false` | 是否允许 Bot 自动使用滑索。 |
| `BotUseRange` | `300` | Bot 开始选择附近滑索端点的范围。 |
| `BotUseCooldownSeconds` | `20` | Bot 结束或放弃一次尝试后的等待时间。 |
| `BotTargetTimeoutSeconds` | `5` | Bot 未能到达目标端点时放弃该次尝试的时间。`0` 为不使用超时。 |
| `BotApproachSpeed` | `450` | Bot 接近端点时的移动速度。 |

Bot 不会模拟按键，也不会改变视角；它只会接近允许使用的端点并自动挂索。

## 配置说明

主配置文件为 `CS2-HanZipLine.jsonc`，根节点为 `CS2HanZipLine`。数值为 `0` 时，以下字段表示不限：`MaxUses`、`LifetimeSeconds`。

### 基础限制

| 配置 | 默认值 | 说明 |
| --- | ---: | --- |
| `Enable` | `true` | 总开关。关闭后插件清除当前活动滑索。 |
| `AdminOnlyCreate` | `false` | `true` 时仅管理员可创建滑索。 |
| `AdminPermissions` | `hanzipline.admin.manage` | 管理员权限标记；多个标记用英文逗号分隔，命中任意一个即可。留空会禁用管理员操作。 |
| `MaxActivePairs` | `64` | 服务器同时存在的滑索上限。 |
| `MaxPerPlayer` | `10` | 每名普通玩家可同时拥有的滑索上限。 |
| `CreateCooldownSeconds` | `8` | 同一玩家两次创建之间的等待秒数。 |
| `MinDistance` | `128` | 两端端点允许的最短距离。 |
| `MaxDistance` | `5000` | 两端端点允许的最长距离。 |
| `AnchorSeparation` | `96` | 新端点与已有端点的最小间距。 |
| `UseRadius` | `96` | 玩家按 `F` 或 Bot 自动挂索时，距端点的最大距离。 |

### 队伍与视觉

| 配置 | 默认值 | 说明 |
| --- | --- | --- |
| `AllowAllTeamsUseZiplines` | `true` | `true` 时所有人可用全部滑索；`false` 时仅本队与 Global 滑索可用。 |
| `CTZiplineColor` | `80 180 255 255` | CT 激光绳和 CT 轮廓高亮颜色。 |
| `TZiplineColor` | `255 80 80 255` | T 激光绳和 T 轮廓高亮颜色。 |
| `GlobalZiplineColor` | `255 255 255 255` | Global 激光绳和 Global 轮廓高亮颜色。 |
| `AdminVisionGlowRange` | `5000` | 管理员显示轮廓时的外发光距离。`0` 可关闭范围显示。 |
| `BeamWidth` | `0.5` | 激光绳起始宽度。 |
| `BeamEndWidth` | `0.5` | 激光绳末端宽度。 |
| `BeamHaloScale` | `3` | 激光绳光晕大小。 |

### 乘坐与存在时间

| 配置 | 默认值 | 说明 |
| --- | ---: | --- |
| `RideSpeed` | `700` | 沿滑索移动的速度。 |
| `ArrivalDistance` | `48` | 接近终点到该距离时结束乘坐。 |
| `AlignmentSpeed` | `240` | 乘坐中向绳索中心修正的位置速度。 |
| `RideFlyDurationSeconds` | `1` | 上索后短暂飞行状态持续时间。`0` 为不启用。 |
| `StallTimeoutSeconds` | `1.5` | 骑乘没有前进时自动安全下索的时间。 |
| `MaxUses` | `0` | 每根滑索允许被乘坐的次数；`0` 为不限。 |
| `LifetimeSeconds` | `0` | 玩家或管理员临时创建的滑索存在秒数；`0` 为不限。已保存地图滑索不受此项影响。 |
| `ClearEachRound` | `true` | 回合结束清除当前滑索，并在下一回合开始恢复已保存路线。 |

### 模型与建造效果

| 配置 | 默认值 | 说明 |
| --- | --- | --- |
| `AnchorModel` | `models/props/cs_italy/it_streetlampleg.vmdl` | 两端滑索架模型。 |
| `AnchorModelScale` | `0.65` | 滑索架模型缩放。 |
| `CableAttachmentHeightFallback` | `144` | 无法读取模型边界时，缆绳挂点的备用高度。 |
| `RealisticBuild` | `false` | 是否显示端点模型飞向目标位置的建造效果。 |
| `BuildFlightModel` | `weapons/models/grenade/decoy/weapon_decoy.vmdl` | 建造飞行效果使用的模型。 |
| `BuildFlightModelScale` | `1` | 飞行模型缩放。 |
| `BuildFlightSpeed` | `1800` | 飞行模型速度。 |
| `BuildFlightGravity` | `800` | 飞行模型下落力度。 |
| `SurfaceOffset` | `2` | 端点离命中表面的视觉偏移。 |
| `GroundTraceDistance` | `256` | 创建时向下寻找起点地面的最大距离。 |

### 声音

| 配置 | 默认值 | 说明 |
| --- | --- | --- |
| `PrecacheSoundEvent` | `soundevents/game_sounds_ui.vsndevts` | 需要预加载的声音事件文件；留空仅禁用预加载。 |
| `CreateSound` | `Music.Match.LastRoundHalf` | 滑索创建完成音效。留空可禁用。 |
| `BuildSound` | `UI.ContractSeal` | 开启真实建造效果时的音效。留空可禁用。 |
| `RideStartSound` | `UIPanorama.container_weapon_ticker` | 开始乘坐音效。留空可禁用。 |
| `RideLoopSound` | `UI.StickerScratch` | 乘坐循环音效。留空可禁用。 |
| `RideLoopInterval` | `0.5` | 循环音效间隔秒数。 |
| `RideEndSound` | `UI.CrateOpen` | 下索或到达终点音效。留空可禁用。 |
| `SoundVolume` | `1` | 所有滑索音效音量。 |
| `SoundPitch` | `1` | 所有滑索音效音调。 |

## 常见用法

### 双方都能快速前往中路

1. 管理员在 CT 家创建一根 CT 滑索，在 T 家创建一根 T 滑索，在中路创建一根全局滑索。
2. 将 `AllowAllTeamsUseZiplines` 设为 `false`。
3. CT 只能使用 CT 家滑索和中路全局滑索；T 同理。

### 所有人自由使用全部路线

将 `AllowAllTeamsUseZiplines` 设为 `true`。滑索原本的 CT、T、Global 颜色仍会保留，便于管理员识别路线归属，但使用不受队伍限制。

### 玩家共创地图路线

设置 `MaxPerPlayer: 1`，让玩家逐一布置。管理员确认后在管理员菜单选择“保存当前地图滑索”。保存完成后可将 `MaxPerPlayer` 改回正常值。

## 排查

| 情况 | 检查方法 |
| --- | --- |
| 无法创建 | 确认角色存活、准星命中实体表面、脚下附近有地面，并检查距离、冷却和数量限制。 |
| 无法使用某根滑索 | 检查 `AllowAllTeamsUseZiplines`；关闭时只能使用本队或 Global 滑索。 |
| Bot 不使用滑索 | 确认 `BotAllowUse` 已开启、端点在 `BotUseRange` 内、Bot 能走到端点，且队伍权限允许。 |
| 保存后没有自动恢复 | 确认保存的是当前地图，且服务器对 `<PluginDataDirectory>/maps/` 有写入权限。可在管理员菜单使用“重载地图保存的滑索”验证。 |
| 没有菜单或管理员项 | 确认 `Enable` 为 `true`，并检查玩家是否拥有 `AdminPermissions` 中任一权限。 |
| 缺少中文聊天提示 | 确认发布时连同 `resources/translations/` 一起复制。 |

## 开源协议

本项目使用 [GPL-3.0](LICENSE) 协议。
