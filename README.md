This is English language version of DukovMoreTotem. The version of MoreTotem is old as well the date of 2025 Oct 20

Original instruction below:
________________________________________________________________________________________

# Duckov Modding 指南（基于 MoreTotem 实战）

本仓库记录了在“逃离鸭科夫（Escape From Duckov）”缺少官方 API 情况下，如何高效完成一个功能型 MOD 的方法论与可复用代码位点。目标：后续新建 MOD 时，仅阅读本 README 即可快速确定切入点、规避坑点、减少盲试时间。

English summary at bottom.

---

## 一、Mod 装载模型（已验证）
- Mod 目录：游戏根目录 `Duckov_Data/Mods/<YourMod>`（或创意工坊目录）。
- info.ini：`name` 用作命名空间，游戏按 `<name>.ModBehaviour` 添加到场景 (`GameObject.AddComponent`)。
- `ModBehaviour` 必须继承 `Duckov.Modding.ModBehaviour`（它本质是 `MonoBehaviour`，并带 Mod 系统扩展）。
- 工程建议：TargetFramework 设为 `netstandard2.1`；引用 `Duckov_Data/Managed/*.dll` 与 `0Harmony.dll`。

---

## 二、运行期 API 发现与定位（高效套路）
缺少官方 API 时，优先“精准绑定 + 事件驱动”，仅在兜底时扫描。

1) Harmony 打补丁
- 在 `Awake()`：`new Harmony("mod.moretotem").PatchAll()`。
- 用“候选名数组 + 反射匹配 + Prepare() 过滤”的方式找目标，避免无目标时报错（见 `Patches/*`）。

2) 快速定位调用链
- 工具：`ModBehaviour.TraceOnceFrom(string where)`（只打印一次，避免刷屏）。
- 用法：在补丁 Prefix 里调用，日志里看真实入口链，例如：
  - LevelManager.InitLevel → CharacterCreator.CreateCharacter → CharacterMainControl.SetItem

3) 精准 UI 绑定（面板直连优先）
- 已验证的详情面板/路径：
  - 类型优先：`Duckov.UI.ItemDetailsDisplay`（及同命名空间下的其它 ItemDetails/Tooltips 类）。
  - 路径后备（部分常见 UI）：
    - `/GameplayUICanvas/Tabs/ViewArea/InventoryView/.../ItemDetails/.../ItemSlotsDisplay/GridLayout`
    - `/GameplayUICanvas/ItemDetails/.../ItemSlotsDisplay/GridLayout`
    - Loot/Shop/Repair/Decompose/Customize/Formula 等常见变体（详见 `Core/ModBehaviour.PanelBinding.cs` 的 suffix 列表）。
- 交互热键：
  - F9 锁定当前 ItemDetails 的目标（严格模式）
  - F11 光标射线在 UI 上拾取附近 `SlotCollection`
  - F3 Dump 所有 GridLayoutGroup
  - F5 对 `ItemSlotsDisplay` 做两行高度与 ScrollRect 修正

---

## 三、已验证类型/成员与常用反射点
- ItemStatsSystem（槽位系统）
  - `ItemStatsSystem.SlotCollection`
    - 成员：`list: IList<Slot>`、`Add(Slot)`、`Remove(Slot)`、`GetSlot(string key)`
  - `ItemStatsSystem.Slot`
    - 关键：`Key`、`DisplayName`、`SlotIcon`、`requireTags`、`excludeTags`
    - 方法：`Initialize(SlotCollection)`、`Unplug()`、`ForceInvokeSlotContentChangedEvent()`
    - 事件字段：`onSlotContentChanged += Action<Slot>`（可直接 +=）
    - 私有字段（反射）：`forbidItemsWithSameID: bool`
- 槽位识别策略（统一封装于 `Core/ModBehaviour.ItemHelpers.cs`）
  - Totem 槽：`Key/DisplayName/requireTags[0].name` 包含 `totem`（无视大小写）
  - Gem 槽：`Key/DisplayName/requireTags[0].name` 命中 GemKeywords；排除 NonGemKeywords（中文“显卡”等误判词已排除）
- 生成新槽 Key（稳定命名）
  - `NextIncrementalKey(existing, prefix)`：以固定前缀与尾随编号生成（`gem, gem1, gem2...` 或保存下来的前缀 `Socket, Socket1...`）

---

## 四、关键补丁点（按时机）
- 关卡/角色创建：尽早扩槽
  - `LevelManager.InitLevel` Prefix
  - `CharacterCreator.CreateCharacter` Prefix
- 装备/设置物品：对“正在被设置的实例”扩槽
  - `CharacterMainControl.SetItem(Item)` Prefix
  - `CharacterEquipmentController.SetItem(Item)` Prefix
- 仓库/存储类：对“被放入/设置的 Item”扩槽（兜底）
  - 名称含 `Stash/Warehouse/Storage/Container/Inventory/Bank/Chest/Depot/Locker`，方法名为 `Add/Insert/Put/Place/SetItem/SetSlot/Set` 等（见 `Patches/StorageItemPatches.cs`）
- 反序列化/启用（极早期兜底，防止读档时内容找不到槽）
  - 对所有 `Item` 的 `OnAfterDeserialize/OnEnable/Awake/Start` 做 Postfix（仅在目标存在时生效，见 `Patches/ItemLifecyclePatches.cs`）

---

## 五、持久化与一致性策略（本 Mod 落地做法）
- Gem 槽数量偏好（每“物品类型”）
  - PlayerPrefs 键：`MoreTotem.Gems.<SanitizedKey>`（构建自 `Item` 类型 + `TypeID/ID/UniqueId/Guid` 等常见唯一字段）
  - 只增不减：加载/切场景时仅扩充到“已保存目标数”，从不自动缩减，以防读档阶段把已有内容抛弃。
  - 前缀持久化：另存 `MoreTotem.GemKeyPrefix.<...>`，优先用“观察到的前缀”（如 `Socket`）创建新槽，避免跨会话键名不一致导致内容匹配失败。
  - 宿主字段同步：常见字段 `GemSlots/GemSlotCount/SocketCount/GemSlotCapacity` 会在变更与退出前被同步到“当前 gem 槽数”，配合 `ForceInvokeSlotContentChangedEvent()` 触发存档。
- Totem 槽（全局 Extra）
  - 总数 = 基础 2 + Extra（PlayerPrefs：`MoreTotem.Extra`）
  - 同步宿主计数：尝试匹配 `TotemSlots/TotemSlotCount/TotemSlotCapacity/RuneSlots/...` 等字段，保证扩槽被序列化体系认可。
- 退出时兜底
  - `OnApplicationQuit/OnQuitting` 扫描所有 `SlotCollection`，同步 gem/totem 计数并触发一次变更事件；日志输出 `GemSummary(...)` 便于核对 ownerCount 与键名列表。
- 动态同步器
  - `GemOwnerSyncer` 组件自动跟随槽内容变动同步宿主计数字段，避免 UI 操作后未刷新导致不保存。

---

## 六、性能与触发时机（避免卡顿）
- 非每帧扫描：所有重操作绑定到“场景加载后一次/两次短流程”与“补丁触发时刻/按键触发”。
- 缓存：`Resources.FindObjectsOfTypeAll<SlotCollection/GridLayoutGroup>` 结果按场景缓存。
- 分批/预算：应用已保存 Gem 槽数时按预算（512/96/48）分次运行，可在代码中调整。

---

## 七、操作与热键（默认不影响自定义）
- 打开窗口：
  - 自定义热键（默认 `LeftCtrl+LeftAlt+F8`）
  - `LeftCtrl+F6`
  - 新增永久备用：`Ctrl+N`（不影响自定义绑定）
- 精准选择：`F9` 锁定当前 ItemDetails；`F11` 鼠标指向拾取；`F3` Dump Grid；`F5` UI 两行高度修正。

---

## 八、启动一个新 Duckov Mod 的最小步骤（Cookbook）
1. 建 `netstandard2.1` 类库工程，引用 `Duckov_Data/Managed/*.dll` 与 `0Harmony.dll`。
2. 新建 `YourNs.ModBehaviour : Duckov.Modding.ModBehaviour`，在 `Awake()` 调用 `Harmony.PatchAll()`。
3. 用本仓库的补丁与工具类“按需复用”：
   - 入口/时机：拷贝 `Patches/*` 并删去与你无关的那类扫描条件
   - UI/面板绑定：参考 `Core/ModBehaviour.PanelBinding.cs` 的“严格绑定 + 路径后备”套路
   - 槽位操作：参考 `Core/ModBehaviour.ItemHelpers.cs` 与 `Services/GemService.cs`
   - 持久化：参考 `Services/GemPrefs.cs` 的 Key 构造与保存
4. 若需要自定义 UI 适配，参考 `Core/ModBehaviour.UIEnsure.cs`（两行高度 + ScrollRect）。

---

## 九、需要移除/避免的“落后做法”（本仓库已替换）
- 仅依赖早期面板类型名（例如 `ItemInspectPanel/ItemInfoPanel/UI_ItemInspectPanel`）去取“当前 Item”
  - 现状：统一改为 `Duckov.UI.ItemDetailsDisplay` 等 `Duckov.UI.*` 系列优先 + 路径直连后备；提高命中率。
- 每帧全局扫描 `Resources.FindObjectsOfTypeAll`/全局 UI 射线
  - 现状：只在场景加载后 1-2 次短流程，平时事件驱动（SetItem/存储等）或按键触发。
- Gem 槽“按模板 Key 前缀”随缘命名
  - 现状：统一前缀并持久化（`gem/gem1...` 或保存观察到的 `Socket...`），避免跨会话不一致。
- 加载时“缩减到保存值”
  - 现状：永不自动缩减，只扩不减，避免把已存在内容挤掉。
- 仅编辑 `SlotCollection` 而不同步宿主“计数字段/容量”
  - 现状：同步 `GemSlots/GemSlotCount/SocketCount/GemSlotCapacity` 与 `TotemSlots/...` 等，确保读档/存档逻辑认可变更。
- 退出前不触发变更事件、不打诊断日志
  - 现状：退出时触发一次 `ForceInvokeSlotContentChangedEvent()` 并输出 `GemSummary(...)`，可核对 ownerCount 与键名。

---

## 十、文件导航（关键实现位置）
- 入口与主逻辑：`ModBehaviour.cs`
- 槽位识别/工具：`Core/ModBehaviour.ItemHelpers.cs`
- UI 严格绑定与路径后备：`Core/ModBehaviour.PanelBinding.cs`
- UI 高度与滚动修正：`Core/ModBehaviour.UIEnsure.cs`
- UI/诊断工具：`Core/ModBehaviour.UI.cs`, `Diagnostics/ApiProbe.cs`
- Gem 偏好与服务：`Services/GemPrefs.cs`, `Services/GemService.cs`
- 动态宿主同步器：`Core/ModBehaviour.GemSync.cs`
- 关卡/装备/存储等补丁：`Patches/*`

---

## English quick brief
- Loader: ModBehaviour = <name>.ModBehaviour; inherit Duckov.Modding.ModBehaviour; netstandard2.1; reference Duckov Managed + 0Harmony.
- Discovery: Harmony patches with safe Prepare(); print stack once; strict UI binding via Duckov.UI.ItemDetailsDisplay with path fallbacks.
- Slot system: SlotCollection/Slot; identify Totem/Gem via name/tags; generate stable keys.
- Persistence: Gem count per-item-type in PlayerPrefs (stable id); only expand; persist key prefix; sync owner fields (GemSlots/GemSlotCount/SocketCount/GemSlotCapacity and Totem counterparts); touch change on quit; optional live sync via GemOwnerSyncer.
- Patch points: LevelManager.InitLevel, CharacterCreator.CreateCharacter, CharacterMainControl.SetItem, CharacterEquipmentController.SetItem, storage-like Add/Insert/Set; plus Item OnAfterDeserialize/OnEnable/Awake/Start.
- Performance: cache and budgeted passes, not per-frame.
- Hotkeys: custom (default LCtrl+LAlt+F8), LCtrl+F6, permanent Ctrl+N; F9 strict bind; F11 UI raycast; F3 dump grids; F5 UI fix.
- Avoid legacy approaches: per-frame global scans, relying on old panel names only, shrinking slots on load, not syncing owner counts, non-stable key names.
