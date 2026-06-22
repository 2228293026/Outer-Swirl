# Outer Swirl

Outer Swirl 是一个用于《A Dance of Fire and Ice》（ADOFAI）的 Unity Mod Manager 模组。它会在关卡编辑器中新增一个自定义事件，用来在谱面指定楼层开启或关闭“外圈星球”效果。

## 功能

- 在编辑器的 **Gameplay** 分类中注册自定义事件 **Outer Swirl / 星球外圈**。
- 事件可放置在第一格，并在 `OnPrebar` 时机缓存事件数据。
- 在星球移动到对应楼层时应用事件状态，动态切换外圈效果。
- 支持英文、简体中文、韩文和日文的事件名称与属性标签本地化。
- 使用 Harmony 补丁扩展游戏对 `scrPlanet.foolSwirl` 的判断，让外圈状态可以由自定义事件控制。

## 事件说明

| 项目 | 值 |
| --- | --- |
| 事件名称 | `Outer Swirl` / `星球外圈` |
| 事件分类 | `Gameplay` |
| 执行时机 | `OnPrebar` |
| 是否允许第一格 | 是 |
| 自定义事件 ID | `100000` |

### 属性

| 属性 | 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `Enable` | `Bool` | `true` | 开启或关闭外圈效果。 |

## 安装

1. 确保已为 ADOFAI 安装 Unity Mod Manager。
2. 从 Release 或自行编译获取模组文件。
3. 将以下文件放入 Unity Mod Manager 的 `Mods/OuterSwirl` 目录：
   - `Outer Swirl.dll`
   - `Info.json`
   - `Localization.json`
4. 在 Unity Mod Manager 中启用 **Outer Swirl**。

## 从源码构建

### 环境要求

- Windows
- Visual Studio 或 MSBuild
- .NET Framework 4.8.1 Developer Pack
- 已安装 ADOFAI 与 Unity Mod Manager

### 依赖配置

项目文件中的游戏依赖默认指向：

```text
D:\Program Files\Steam\steamapps\common\A Dance of Fire and Ice\A Dance of Fire and Ice_Data\Managed\
```

如果你的游戏安装路径不同，请在 `Outer Swirl.csproj` 中更新相关 `<HintPath>`。

### 构建命令

```powershell
msbuild "Outer Swirl.sln" /p:Configuration=Release
```

构建产物默认输出到：

```text
bin\Release\Outer Swirl.dll
```

## 项目结构

```text
.
├── Events/SetOuterSwirlEvent.cs   # 自定义 Outer Swirl 事件定义
├── Patch/FoolSwirlPatch.cs        # 扩展 foolSwirl 判断的 Harmony 补丁
├── EventSystem.cs                 # 自定义事件注册、执行与本地化挂钩
├── PatchManager.cs                # Harmony 补丁注册与生命周期管理
├── OuterSwirlLocalization.cs      # 多语言文本加载与查询
├── Info.json                      # Unity Mod Manager 元数据
├── Localization.json              # 本地化文本
└── Outer Swirl.csproj             # C# 项目文件
```

## 开发提示

- 新增补丁后，请在 `PatchManager.RegisterAll()` 中注册补丁类型。
- 新增事件属性时，请在事件成员上添加 `[EventProperty]`，并按需添加 `[PropertyLabel]` 等属性元数据。
- 新增本地化文本时，请同步更新 `Localization.json`。

## 许可证

本项目采用 MIT 协议开源，详情请参见 [LICENSE](LICENSE)。
