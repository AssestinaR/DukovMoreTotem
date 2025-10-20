# Duckov Modding 示例 (Duckov Modding Example)

_This is an example project for modding Escape From Duckov._

## 工作原理概述 / Overview

《逃离鸭科夫》的 Mod 模块会扫描并读取 Duckov_Data/Mods 文件夹中的各个子文件夹，以及 Steam 创意工坊已订阅物品的各个文件夹。通过文件夹中包含的 dll 文件，info.ini 和 preview.png 在游戏中展示、加载 mod。

The modding system of Escape From Duckov scans and reads the subfolders in the Duckov_Data/Mods folder, as well as the folders of subscribed items in the Steam Workshop. Mods are displayed and loaded in the game through the `dll` files, `info.ini`, and `preview.png` contained in these folders.

《逃离鸭科夫》会读取 info.ini 中的 name 参数，并以此作为 namespace 尝试加载名为 ModBehaviour 的类。例如，info.ini 中如果记载`name=MyMod`,则会加载`MyMod.dll`文件中的`MyMod.ModBehaviour`。

Escape From Duckov reads the name parameter in info.ini and uses it as a namespace to load a class named ModBehaviour. For example, if info.ini contains `name=MyMod`, it will load `MyMod.ModBehaviour` from the `MyMod.dll` file.

ModBehaviour 应继承自 Duckov.Modding.ModBehaviour。Duckov.Modding.ModBehaviour 是一个继承自 MonoBehaivour 的类。其中还包含了一些 mod 系统中需要使用的额外功能。在加载 mod 时,《逃离鸭科夫》会创建一个该 mod 的 GameObject 并通过调用 GameObject.AddComponent(Type) 的方式创建一个 ModBehaviour 的实例。Mod 作者可以通过编写 ModBehaviour 的 Start\Update 等 Unity 事件实现功能，也可以通过注册《逃离鸭科夫》中的其他事件实现功能。

ModBehaviour should inherit from `Duckov.Modding.ModBehaviour`. `Duckov.Modding.ModBehaviour` is a class that inherits from MonoBehaviour and includes some additional features needed in the mod system. When loading a mod, Escape From Duckov creates a GameObject for that mod and creates an instance of ModBehaviour by calling GameObject.AddComponent(Type). Mod authors can implement functionality by writing Unity events such as Start\Update in ModBehaviour, or by registering other events in Escape From Duckov.

## Mod 文件结构 / File Structure

将准备好的 Mod 文件夹放到《逃离鸭科夫》本体的 Duckov_Data/Mods 中，即可在游戏主界面的 Mods 界面加载该 Mod。
假设 Mod 的名字为"MyMod"。发布的文件结构应该如下：

Place the prepared Mod folder in `Duckov_Data/Mods` within the Escape From Duckov installation directory, and the Mod can be loaded in the Mods interface on the game's main menu.
Assuming the Mod's name is "MyMod", the published file structure should be as follows:

- MyMod (文件夹 / Folder)
  - MyMod.dll
  - info.ini
  - preview.png (正方形的预览图，建议使用 256*256 分辨率 / Square preview image, recommended resolution 256*256)

[Mod 文件夹示例 / Mod Folder Example](DisplayItemValue/ReleaseExample/DisplayItemValue/)

### info.ini

info.ini 应包含以下参数:

- name (mod 名称，主要用于加载 dll 文件)
- displayName (显示的名称)
- description（显示的描述）

info.ini should contain the following parameters:

- name (mod name, primarily used for loading the dll file)
- displayName (display name)
- description (display description)

info.ini 还可能包含以下参数:

- publishedFileId （记录本 Mod 在 steam 创意工坊的 id）

info.ini may also contain the following parameters:

- publishedFileId (records this Mod's ID in the Steam Workshop)

**注意：在上传 Steam Workshop 的时候，会复写 info.ini。info.ini 中原有的信息可能会因此丢失。所以不建议在 info.ini 中存储除以上项目之外的其他信息。**

**Note: When uploading to Steam Workshop, info.ini will be overwritten. Original information in info.ini may be lost as a result. Therefore, it is not recommended to store any information other than the above items in info.ini.**

## 配置 C# 工程 / Configuring C# Project

1. 在电脑上准备好《逃离鸭科夫》本体。
2. 创建一个 .Net Class Library 工程。
3. 配置工程参数。
   1. Target Framework
      - **TargetFramework 建议设置为 netstandard2.1。**
      - 注意删除 TargetFramework 不支持的功能，比如`<ImplicitUsings>`
   2. Reference Include
      - 将《逃离鸭科夫》的`\Duckov_Data\Managed\*.dll`添加到引用中。
      - 例：

      ```
        <ItemGroup>
          <Reference Include="$(DuckovPath)\Duckov_Data\Managed\TeamSoda.*" />
          <Reference Include="$(DuckovPath)\Duckov_Data\Managed\ItemStatsSystem.dll" />
          <Reference Include="$(DuckovPath)\Duckov_Data\Managed\Unity*" />
        </ItemGroup> 
      ```

4. 完成！现在在你 Mod 的 Namespace 中编写一个 ModBehaivour 的类。构建工程，即可得到你的 mod 的主要 dll。

### English Translation: 

1. Have Escape From Duckov installed on your computer.
2. Create a .NET Class Library project.
3. Configure project parameters.
   1. Target Framework
      - **It is recommended to set TargetFramework to netstandard2.1.**
      - Note: Remove features not supported by TargetFramework, such as `<ImplicitUsings>`
   2. Reference Include
      - Add `\Duckov_Data\Managed\*.dll` from Escape From Duckov to the references.
      - Example:

      ```
      <ItemGroup>
        <Reference Include="$(DuckovPath)\Duckov_Data\Managed\TeamSoda.*" />
        <Reference Include="$(DuckovPath)\Duckov_Data\Managed\ItemStatsSystem.dll" />
        <Reference Include="$(DuckovPath)\Duckov_Data\Managed\Unity*" />
      </ItemGroup> 
      ```

4. Done! Now write a ModBehaviour class in your Mod's Namespace. Build the project to get your mod's main dll.

csproj 文件示例 / csproj File Example: [DisplayItemValue.csproj](DisplayItemValue/DisplayItemValue.csproj)

## 其他 / Other

### Unity Package

使用 Unity 进行开发时，可以参考本仓库附带的 [manifest.json 文件](UnityFiles/manifest.json) 来选择 package。

When developing with Unity, you can refer to the [manifest.json file](UnityFiles/manifest.json) included in this repository to select packages.

### 自定义游戏物品 / Custom Game Items

- 可调用 `ItemStatsSystem.ItemAssetsCollection.AddDynamicEntry(Item prefab)` 添加自定义物品
- 可调用`ItemStatsSystem.ItemAssetsCollection.RemoveDynamicEntry(Item prefab)`将该 mod 物品移除
- 自定义物品的 prefab 上需要配置好 TypeID。避免与游戏本体和其他 MOD 冲突。
- 进入游戏时如果未加载对应 MOD，存档中的自定义物品会直接消失。

- Call `ItemStatsSystem.ItemAssetsCollection.AddDynamicEntry(Item prefab)` to add custom items
- Call `ItemStatsSystem.ItemAssetsCollection.RemoveDynamicEntry(Item prefab)` to remove the mod item
- Custom item prefabs need to have TypeID configured properly. Avoid conflicts with the base game and other MODs.
- If the corresponding MOD is not loaded when entering the game, custom items in the save file will disappear directly.

### 本地化 / Localization

- 可调用 `SodaCraft.Localizations.LocalizationManager.SetOverrideText(string key, string value)` 来覆盖显示本地化文本。
- 可借助 `SodaCraft.Localizations.LocalizationManager.OnSetLanguage:System.Action<SystemLanguage>` 事件来处理语言切换时的逻辑

- Call `SodaCraft.Localizations.LocalizationManager.SetOverrideText(string key, string value)` to override displayed localization text.
- Use the `SodaCraft.Localizations.LocalizationManager.OnSetLanguage:System.Action<SystemLanguage>` event to handle logic when switching languages

---

## 已验证的 API、类名与补丁点 / Verified APIs, Types and Patch Points

以下内容为在开发 MoreTotem 与 MoreGemSlot 过程中验证可用的类名、方法名与常见反射访问点，供后续协作者直接复用，避免重复试错。

- Harmony 入口
  - 在 `Awake()` 中执行：`new Harmony("<your-id>").PatchAll()`
  - 优先使用“精准补丁”而非全量扫描，必要时可从运行时堆栈定位真实入口。

- 角色与装备相关（用于扩展 Totem 槽，前缀 Prefix）
  - `CharacterMainControl.SetItem`（前缀）
  - `CharacterEquipmentController.SetItem`（前缀）
  - 提前扩容以避免反序列化后第3槽掉档。

- 关卡/人物创建（用于确保在装备/反序列化之前扩容）
  - `LevelManager.InitLevel`（前缀）
  - `CharacterCreator.CreateCharacter`（前缀）
  - 注：以上方法名来自运行时堆栈：`LevelManager.InitLevel → CharacterCreator.CreateCharacter → CharacterMainControl.SetItem`。

- 运行时调试：一次性堆栈打印
  - `MoreTotem.ModBehaviour.TraceOnceFrom(string where)` 可在补丁中调用，打印触发调用链，便于确认真实入口（不会刷屏）。
  - 日志示例前缀：`[MoreTotem] Call stack from <where>`。

- 槽位系统（ItemStatsSystem）常用成员
  - 类型：`ItemStatsSystem.SlotCollection`
    - 成员：`list`（`IList<Slot>`）、`Add(Slot)`, `Remove(Slot)`, `GetSlot(string key)`
  - 类型：`ItemStatsSystem.Slot`
    - 重要字段/属性：`Key`, `DisplayName`, `SlotIcon`, `requireTags`, `excludeTags`
    - 方法：`Initialize(SlotCollection)`, `Unplug()`, `ForceInvokeSlotContentChangedEvent()`
    - 事件字段：`onSlotContentChanged`（可 += 订阅）
    - 反射私有字段：`forbidItemsWithSameID: bool`
  - 槽位识别策略
    - Totem 槽：`Key/DisplayName/requireTags[0].name` 包含 `totem`（忽略大小写）
    - Gem 槽：`Key/DisplayName/requireTags[0].name` 包含 `gem`（忽略大小写）
  - 生成新槽位 Key 的规则
    - 使用 `NextIncrementalKey(existingKeys, templateKey)`：以模板 Key 为前缀，读取尾随数字，生成下一个递增 Key。

- MoreTotem 关键配置与行为
  - 常量：`BaseTotemSlots = 2`，`MaxExtraSlots = 20`（总槽位 = 2 + Extra，F6 增，F7 减）
  - 入口方法：`MoreTotem.ModBehaviour.EnsureTotemSlotsEarly()`（在上述补丁前缀中调用）
  - 节流：基于“目标总槽位 + 场景索引 + 时间戳”的去重，避免频繁扫描 `Resources.FindObjectsOfTypeAll<SlotCollection>()`
  - 持久化：依赖游戏原生读档流程；关键是“在读档/装备前扩容”，无需额外 PlayerPrefs 物品备份。

- MoreGemSlot（为当前查看武器调节宝石槽，F8/F9）
  - 按键：F8 增加，F9 减少（限制 0..20，可调整）
  - 获取“当前查看物品”的面板类型（单例实例 + 当前 Item）
    - 类型候选：`ItemInspectPanel`、`ItemInfoPanel`、`UI_ItemInspectPanel`
    - 单例字段/属性：`Instance`（静态）
    - 当前 Item 成员名候选：`CurrentItem`、`TargetItem`、`item`
  - 武器判定：`item.GetType().Name/FullName` 包含 `weapon`
  - 宝石槽数量的常见成员名（命中其一即可）
    - 属性：`GemSlots:int`、`GemSlotCount:int`、`SocketCount:int`
    - 字段：`gemSlots:int`、`gemSlotCount:int`、`socketCount:int`
  - 若无数量字段：回退为编辑该 `Item` 的 `SlotCollection` 中的 Gem 槽（按首个 Gem 槽模板复制/删除）
  - 刷新 UI：调用 `OnStatsChanged()`/`OnChanged()` 或面板的 `Refresh()`/`UpdateUI()`

- Harmony/引用
  - Harmony 来自 `0Harmony.dll`（通常位于 `Duckov_Data/Managed/0Harmony.dll` 或 Mod 文件夹中）。
  - 参考 `DisplayItemValue.csproj` 与 `MoreGemSlot.csproj` 的引用示例；在工程中设置 `DuckovPath` 以指向你的游戏安装目录。

## 扩展建议 / Extension Tips

- 若其他模块需要“读档前确保状态”，优先从运行时堆栈定位真实入口，再只对该入口加前缀补丁，避免性能问题。
- 扩充槽位时尽量复制模板槽的 `SlotIcon` 与 `requireTags/excludeTags`，并调用 `Initialize()` 后再 `Add()`；必要时触发 `ForceInvokeSlotContentChangedEvent()` 更新状态。
- 若 UI 或布局因槽位过多而异常，需要配合 UI 侧做额外适配。
