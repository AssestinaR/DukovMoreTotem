using ItemStatsSystem; // for Item
using ItemStatsSystem.Items;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using HarmonyLib;
using System.Text;

namespace MoreTotem
{
    // Mod adds/removes extra Totem slots with F6/F7 and lets base save system persist items.
    public class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private const string PrefKeyExtra = "MoreTotem.Extra"; // extra slots beyond base 2
        internal const int BaseTotemSlots = 2;
        // Cap total totem slots at 72 (base 2 + extra up to 70)
        internal const int MaxTotalTotemSlots = 72;
        internal const int MaxExtraSlots = MaxTotalTotemSlots - BaseTotemSlots;

        internal static ModBehaviour? Instance;

        // Guard to avoid repeated heavy scans
        private static int _lastEnsuredDesiredTotal = -1;
        private static int _lastEnsuredSceneBuildIndex = -1;
        private static float _lastEnsureTime = -999f;
        private const float MinEnsureIntervalSeconds = 5f; // throttle safety window

        // Trace guard
        private static bool _printedStackOnce = false;
        private static bool _verbose = false; // quieter by default to avoid log spam

        // UI panel state (IMGUI only)
        private static bool _uiOpen = false;
        private static Rect _uiRect = new Rect(30, 150, 320, 180);
        private static bool _isResizing = false;
        private const float MinWinW = 280f;
        private const float MinWinH = 160f;
        private static readonly string[] WeaponKeywords = new[] { "weapon", "gun", "rifle", "pistol", "枪", "武器" };
        // Gem identification keywords (tightened): remove overly generic words like "slot/插槽"
        private static readonly string[] GemKeywords = new[] { "gem", "socket", "jewel", "宝石", "镶嵌" };
        // Explicit non-gem keywords to avoid misclassification (e.g., graphics card slots)
        private static readonly string[] NonGemKeywords = new[] { "graphics", "gpu", "graphicscard", "显卡" };
        // 已不再进行面板关键字扫描，移除相关常量

        // Inspector state (kept for debug; UI not shown)
        // 旧的鼠标检查/调试已移除
        private static SlotCollection? _lastInspectedWeaponCol;
        private static SlotCollection? _overrideWeaponCol; // user-selected target
        private static int _lastGemCount = -1; // cached gem count to avoid heavy scans per frame
        // IMGUI-only fields removed

        // Persistence for gem slot counts (per item type key)
        private const string PrefKeyGemPrefix = "MoreTotem.Gems.";
        private const string PrefKeyToggleHotkey = "MoreTotem.ToggleHotkey"; // e.g., "LeftControl+LeftAlt+F8"

        // Throttle for global gem ensure
        private static float _lastGemEnsureTime = -999f;
        private static int _lastGemEnsureSceneIndex = -1;
        private const float MinGemEnsureIntervalSeconds = 3.0f;

        // --- Global enumeration cache (scene-level snapshot) ---
        private static SlotCollection[] _colsCache = Array.Empty<SlotCollection>();
        private static int _colsCacheSceneIndex = -1;
        private static GridLayoutGroup[] _gridsCache = Array.Empty<GridLayoutGroup>();
        private static int _gridsCacheSceneIndex = -1;

        // Ensure certain UI passes only once per scene
        private static int _twoRowAdjustedSceneIndex = -1;
        private static bool _twoRowAdjustedThisScene = false;
        // Gem ensure once-per-scene (done via scene-load routine)
        private static int _gemAppliedSceneIndex = -1;


        // Removed UGUI elements (IMGUI only)
        
        // Track UI scroll fixes per collection to avoid repeated setup
        private static readonly HashSet<int> _scrollFixed = new HashSet<int>();
        
        // Marker component used to avoid duplicating ScrollRect setup
        private sealed class MoreTotemScrollMarker : MonoBehaviour { }

        // 严格模式常开：仅通过 Duckov 详情面板精确获取
        private static string _lastStrictMatchInfo = "";

        // Toggle hotkey state
        private static List<KeyCode> _toggleHotkey = new List<KeyCode>();
        private static bool _bindingHotkey = false;

        // --- Hotkey helpers ---
        private static List<KeyCode> LoadToggleHotkeyFromPrefs()
        {
            try
            {
                var s = PlayerPrefs.GetString(PrefKeyToggleHotkey, "LeftControl+LeftAlt+F8");
                var list = new List<KeyCode>();
                foreach (var part in s.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (Enum.TryParse(part, out KeyCode k)) list.Add(k);
                }
                if (list.Count == 0) list = GetDefaultToggleHotkey();
                return NormalizeHotkey(list);
            }
            catch { return GetDefaultToggleHotkey(); }
        }

        private static void SaveToggleHotkeyToPrefs(List<KeyCode> hotkey)
        {
            try
            {
                var norm = NormalizeHotkey(hotkey);
                string s = string.Join("+", norm.Select(k => k.ToString()).ToArray());
                PlayerPrefs.SetString(PrefKeyToggleHotkey, s);
                PlayerPrefs.Save();
            }
            catch { }
        }

        private static string HotkeyToString(List<KeyCode> keys)
        {
            if (keys == null || keys.Count == 0) return "(Not set)";
            return string.Join(" + ", keys.Select(k => k.ToString()).ToArray());
        }

        private static bool IsModifierKey(KeyCode k)
        {
            return k == KeyCode.LeftControl || k == KeyCode.RightControl ||
                   k == KeyCode.LeftAlt || k == KeyCode.RightAlt ||
                   k == KeyCode.LeftShift || k == KeyCode.RightShift;
        }

        private static List<KeyCode> NormalizeHotkey(List<KeyCode> src)
        {
            var nonMods = src.Where(k => !IsModifierKey(k)).ToList();
            var mods = src.Where(k => IsModifierKey(k)).Distinct().ToList();
            var res = new List<KeyCode>();
            if (nonMods.Count > 0) res.Add(nonMods[0]);
            res.AddRange(mods);
            if (res.Count == 0) res = GetDefaultToggleHotkey();
            if (res.Count > 3) res = res.Take(3).ToList();
            return res;
        }

        private static List<KeyCode> CaptureCurrentlyPressedKeys(int maxCount)
        {
            var pressed = new List<KeyCode>();
            foreach (KeyCode k in Enum.GetValues(typeof(KeyCode)))
            {
                // Ignore mouse buttons and non-typical values
                if ((int)k < (int)KeyCode.Backspace) continue; // heuristic skip
                var name = k.ToString();
                if (name.StartsWith("Mouse")) continue;
                try { if (Input.GetKey(k)) pressed.Add(k); } catch { }
            }
            pressed = NormalizeHotkey(pressed);
            if (pressed.Count > maxCount) pressed = pressed.Take(maxCount).ToList();
            return pressed;
        }

        private static List<KeyCode> GetDefaultToggleHotkey()
        {
            return new List<KeyCode> { KeyCode.LeftControl, KeyCode.LeftAlt, KeyCode.F8 };
        }

        void Awake()
        {
            Instance = this;
            Debug.Log("MoreTotem Loaded");
            try
            {
                var harmony = new Harmony("mod.moretotem");
                harmony.PatchAll(); // apply patches in Patches/*
                Debug.Log("[MoreTotem] Harmony patches applied");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MoreTotem] Harmony patching failed: {ex}");
            }

            // 不再进行鼠标下的周期性检查
            // Load hotkey
            _toggleHotkey = LoadToggleHotkeyFromPrefs();
        }

        // Utility: print a compact one-shot call stack to locate real load/save entrypoints
        internal static void TraceOnceFrom(string where)
        {
            if (_printedStackOnce) return;
            _printedStackOnce = true;
            try
            {
                var st = new System.Diagnostics.StackTrace(skipFrames: 1, fNeedFileInfo: false);
                var frames = st.GetFrames() ?? Array.Empty<System.Diagnostics.StackFrame>();
                var lines = frames
                    .Take(12)
                    .Select(f =>
                    {
                        var m = f.GetMethod();
                        var dt = m?.DeclaringType;
                        var tn = dt != null ? dt.FullName : "<null>";
                        return $"  at {tn}.{m?.Name}()";
                    });
                Debug.Log("[MoreTotem] Call stack from " + where + "\n" + string.Join("\n", lines));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[MoreTotem] TraceOnceFrom failed: " + ex.Message);
            }
        }

        private static void HarmonyEnsurePrefix()
        {
            EnsureTotemSlotsEarly();
        }

        void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            // On scene change, reset guard so we ensure once in new scene
            _lastEnsuredDesiredTotal = -1;
            _lastEnsuredSceneBuildIndex = SceneManager.GetActiveScene().buildIndex;
            ResetPerSceneFlags(_lastEnsuredSceneBuildIndex);
            StartCoroutine(EnsureAfterSceneLoadRoutine(initialDelay: 0.25f));
        }

        void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            // IMGUI only: nothing to hide
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Reset ensure guard for new scene
            _lastEnsuredDesiredTotal = -1;
            _lastEnsuredSceneBuildIndex = scene.buildIndex;
            ResetPerSceneFlags(scene.buildIndex);
            StartCoroutine(EnsureAfterSceneLoadRoutine(initialDelay: 0.35f));
        }

        private IEnumerator EnsureAfterSceneLoadRoutine(float initialDelay)
        {
            if (initialDelay > 0) yield return new WaitForSeconds(initialDelay);
            // Pass 1: ensure slots and UI, and apply saved gem counts (once per scene)
            int desiredTotal = BaseTotemSlots + GetExtra();
            int changed1 = EnsureTotemSlotsForAll(desiredTotal);
            TryEnsureScrollableEquipmentUIForAll();
            TryApplySavedGemsOncePerScene(budget: 96);
            if (changed1 >= 0)
            {
                _lastEnsuredDesiredTotal = desiredTotal;
                _lastEnsureTime = Time.realtimeSinceStartup;
            }

            // Pass 2 (delayed): UI may appear slightly later; run a lightweight UI-only ensure and a small gem pass if not done
            yield return new WaitForSeconds(0.4f);
            TryEnsureScrollableEquipmentUIForAll();
            TryApplySavedGemsOncePerScene(budget: 48);
        }

        private void Update()
        {
            // Hotkey: only open window (does not close)
            if (ShouldOpenMiniWindow() || IsAlwaysOpenComboPressed())
            {
                _uiOpen = true;
                Debug.Log("[MoreTotem] Hotkey pressed. UI opened");
            }

            // F9: retarget deterministically: InspectPanel item > cursor candidate > global best
            if (Input.GetKeyDown(KeyCode.F9))
            {
                try
                {
                    var chosen = PickDeterministicTarget();
                    if (chosen != null)
                    {
                        _overrideWeaponCol = chosen;
                        _lastInspectedWeaponCol = chosen;
                        RefreshGemInfo();
                        Debug.Log($"[MoreTotem] Item：{GetTransformPath(chosen.transform)}，Gem={chosen.list?.Count(IsGemSlot) ?? 0}");
                        if (!string.IsNullOrEmpty(_lastStrictMatchInfo)) Debug.Log("[MoreTotem] StrictRoute " + _lastStrictMatchInfo);
                    }
                    else
                    {
                        Debug.Log("[MoreTotem] No selectable item found (Strict Mode).");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[MoreTotem] F9 inspect failed: " + ex.Message);
                }
            }

            // F11: 从鼠标下的 UI 精确拾取（不做全局扫描）
            if (Input.GetKeyDown(KeyCode.F11))
            {
                try
                {
                    var hit = RaycastTopUI();
                    if (hit != null)
                    {
                        var cols = GetCollectionsNear(hit); // 仅在命中的对象邻域内查找
                        var ordered = OrderCandidates(cols);
                        var chosen = ordered.FirstOrDefault();
                        if (chosen != null)
                        {
                            _overrideWeaponCol = chosen; _lastInspectedWeaponCol = chosen; RefreshGemInfo();
                            Debug.Log($"[MoreTotem] F11 精确拾取：{GetTransformPath(chosen.transform)}，Gem={chosen.list?.Count(IsGemSlot) ?? 0}");
                        }
                        else Debug.Log("[MoreTotem] F11 未在命中UI附近找到 SlotCollection。");
                    }
                    else Debug.Log("[MoreTotem] F11 未命中任何 UI 元素。");
                }
                catch (Exception ex) { Debug.LogWarning("[MoreTotem] F11 pick failed: " + ex.Message); }
            }

            // F3: dump UI grids for diagnostics
            if (Input.GetKeyDown(KeyCode.F3))
            {
                try { DumpAllGrids(); } catch (Exception ex) { Debug.LogWarning("[MoreTotem] F3 dump grids failed: " + ex.Message); }
            }
            // F4: removed per user request
            // F5: run grid-by-name pass
            if (Input.GetKeyDown(KeyCode.F5))
            {
                try
                {
                    int n = TryEnsureTwoRowHeightForItemSlotsDisplays();
                    Debug.Log($"[MoreTotem] F5 ensured two-row height on ItemSlotsDisplay: {n}");
                }
                catch (Exception ex) { Debug.LogWarning("[MoreTotem] F5 two-row pass failed: " + ex.Message); }
            }

            // 严格模式常开，无需 F12 切换

            // F10 调试扫描已移除

            // 移除 F6/F7 热键，改为仅在小窗口面板中调整
        }

        private static bool ShouldOpenMiniWindow()
        {
            var keys = (_toggleHotkey != null && _toggleHotkey.Count > 0) ? _toggleHotkey : GetDefaultToggleHotkey();
            var primary = keys[0];
            if (!Input.GetKeyDown(primary)) return false;
            for (int i = 1; i < keys.Count; i++)
            {
                if (!Input.GetKey(keys[i])) return false;
            }
            return true;
        }

        private static bool IsAlwaysOpenComboPressed()
        {
            // Always-open combos: Ctrl+Alt+F8 OR Ctrl+F6
            bool combo1 = Input.GetKey(KeyCode.LeftControl) &&
                          Input.GetKey(KeyCode.LeftAlt) &&
                          Input.GetKeyDown(KeyCode.F8);
            bool combo2 = Input.GetKey(KeyCode.LeftControl) &&
                          Input.GetKeyDown(KeyCode.F6);
            return combo1 || combo2;
        }

        // IMGUI window only
        void OnGUI()
        {
            if (!_uiOpen) return;

            _uiRect = GUILayout.Window(0x4D540001, _uiRect, id =>
            {
                // Header
                GUILayout.Label("MoreTotem Classic (Totem slot expansion)");
                var hint = new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 12 };
                hint.normal.textColor = new Color(1f, 1f, 1f, 0.75f);

                // Close button (top-right)
                var last = GUILayoutUtility.GetLastRect();
                var closeBtnRect = new Rect(_uiRect.width - 28f, 8f, 20f, 20f);
                if (GUI.Button(closeBtnRect, "X"))
                {
                    _uiOpen = false;
                }

                // Totem +/- row
                int extra = GetExtra();
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("-", GUILayout.Width(40)))
                {
                    try
                    {
                        int newExtra = Mathf.Max(extra - 1, 0);
                        SaveExtra(newExtra);
                        int desired = BaseTotemSlots + newExtra;
                        int changed = EnsureTotemSlotsForAll(desired);
                        _lastEnsuredDesiredTotal = desired;
                        _lastEnsureTime = Time.realtimeSinceStartup;
                        Debug.Log($"[MoreTotem] Desired extra set to {newExtra}. Collections changed: {changed}");
                    }
                    catch (Exception ex) { Debug.LogError($"[MoreTotem] UI decrement error: {ex}"); }
                }
                GUILayout.Label($"Extra: {extra}  (Total: {BaseTotemSlots + extra}, Maximum {MaxTotalTotemSlots})", GUILayout.Width(220));
                if (GUILayout.Button("+", GUILayout.Width(40)))
                {
                    try
                    {
                        int newExtra = Mathf.Min(extra + 1, MaxExtraSlots);
                        SaveExtra(newExtra);
                        int desired = BaseTotemSlots + newExtra;
                        int changed = EnsureTotemSlotsForAll(desired);
                        _lastEnsuredDesiredTotal = desired;
                        _lastEnsureTime = Time.realtimeSinceStartup;
                        Debug.Log($"[MoreTotem] Desired extra set to {newExtra}. Collections changed: {changed}");
                    }
                    catch (Exception ex) { Debug.LogError($"[MoreTotem] UI increment error: {ex}"); }
                }
                GUILayout.EndHorizontal();
                GUILayout.Label("Tip/Hint: You may need to scroll your equipment bar/inventory to see the newly added totem slot.", hint);

                // Gem +/- row
                GUILayout.Space(6f);
                GUILayout.Label("Gem Slot (Current Target)");
                GUILayout.BeginHorizontal();
                GUI.enabled = _lastGemCount >= 0;
                if (GUILayout.Button("-", GUILayout.Width(40)))
                {
                    try { int changed = ChangeGemSlotsOnSelectedWeapon(-1); Debug.Log($"[MoreTotem] Gem -1, changed: {changed}"); RefreshGemInfo(); }
                    catch (Exception ex) { Debug.LogError($"[MoreTotem] Gem UI decrement error: {ex}"); }
                }
                GUILayout.Label(_lastGemCount >= 0 ? $"Gems: {_lastGemCount}" : "Gems: n/a", GUILayout.Width(180));
                if (GUILayout.Button("+", GUILayout.Width(40)))
                {
                    try { int changed = ChangeGemSlotsOnSelectedWeapon(+1); Debug.Log($"[MoreTotem] Gem +1, changed: {changed}"); RefreshGemInfo(); }
                    catch (Exception ex) { Debug.LogError($"[MoreTotem] Gem UI increment error: {ex}"); }
                }
                GUI.enabled = true;
                GUILayout.EndHorizontal();
                GUILayout.Label("Instructions: Click item and press F9. Re-select the item to refresh the display.", hint);

                // Actions
                GUILayout.Space(6f);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Detect (F9)", GUILayout.Width(140)))
                {
                    try
                    {
                        var chosen = PickDeterministicTarget();
                        if (chosen != null)
                        {
                            _overrideWeaponCol = chosen; _lastInspectedWeaponCol = chosen; RefreshGemInfo();
                            Debug.Log($"[MoreTotem] Item：{GetTransformPath(chosen.transform)}，Gem={chosen.list?.Count(IsGemSlot) ?? 0}");
                        }
                        else { Debug.Log("[MoreTotem] No selectable target found."); }
                    }
                    catch (Exception ex) { Debug.LogWarning("[MoreTotem] detection failed " + ex.Message); }
                }
                if (GUILayout.Button("Clear", GUILayout.Width(100)))
                {
                    _overrideWeaponCol = null; _lastGemCount = -1;
                }
                GUILayout.EndHorizontal();

                // Hotkey binding
                GUILayout.Space(6f);
                GUILayout.BeginHorizontal();
                GUILayout.Label("Open Window Key:", GUILayout.Width(110));
                GUILayout.Label(HotkeyToString(_toggleHotkey), GUILayout.Width(180));
                if (!_bindingHotkey)
                {
                    if (GUILayout.Button("Rebind", GUILayout.Width(90))) _bindingHotkey = true;
                    if (GUILayout.Button("Restore", GUILayout.Width(90))) { _toggleHotkey = GetDefaultToggleHotkey(); SaveToggleHotkeyToPrefs(_toggleHotkey); }
                }
                else
                {
                    GUILayout.Label("Press any key on the keyboard", hint);
                }
                GUILayout.EndHorizontal();

                if (_bindingHotkey)
                {
                    var ev = Event.current;
                    if (ev != null && ev.isKey && ev.type == EventType.KeyDown)
                    {
                        var pressed = CaptureCurrentlyPressedKeys(maxCount: 3);
                        if (pressed.Count > 0)
                        {
                            _toggleHotkey = pressed; SaveToggleHotkeyToPrefs(_toggleHotkey); _bindingHotkey = false;
                        }
                    }
                    if (Event.current != null && Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
                    {
                        _bindingHotkey = false;
                    }
                }

                // Resize handle
                var e = Event.current;
                var handleRect = new Rect(_uiRect.width - 18f, _uiRect.height - 18f, 16f, 16f);
                GUI.DrawTexture(handleRect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, new Color(1,1,1,0.1f), 0, 0);
                if (e.type == EventType.MouseDown && handleRect.Contains(e.mousePosition)) { _isResizing = true; e.Use(); }
                if (_isResizing && e.type == EventType.MouseDrag)
                {
                    _uiRect.width = Mathf.Max(MinWinW, _uiRect.width + e.delta.x);
                    _uiRect.height = Mathf.Max(MinWinH, _uiRect.height + e.delta.y);
                    e.Use();
                }
                if (e.type == EventType.MouseUp) { _isResizing = false; }

                GUI.DragWindow();
            }, "MoreTotem");
        }

        // All UGUI helpers removed


        // Called by Harmony patches to ensure expansion before equipment/deserialize
        public static void EnsureTotemSlotsEarly()
        {
            var inst = Instance;
            if (inst == null) return;
            try
            {
                int desiredTotal = BaseTotemSlots + GetExtra();

                // cheap guard: if same desired total already ensured this scene and recent, skip heavy scan
                var activeSceneIndex = SceneManager.GetActiveScene().buildIndex;
                if (_lastEnsuredDesiredTotal == desiredTotal && _lastEnsuredSceneBuildIndex == activeSceneIndex)
                {
                    if (Time.realtimeSinceStartup - _lastEnsureTime < MinEnsureIntervalSeconds)
                        return;
                }

                int changed = inst.EnsureTotemSlotsForAll(desiredTotal);
                if (changed >= 0) // even 0 means up-to-date
                {
                    _lastEnsuredDesiredTotal = desiredTotal;
                    _lastEnsuredSceneBuildIndex = activeSceneIndex;
                    _lastEnsureTime = Time.realtimeSinceStartup;
                    // Try to make equipment grids scrollable and height-limited
                    inst.TryEnsureScrollableEquipmentUIForAll();
                    // Gem counts are handled in scene-load routine to avoid frequent global scans
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MoreTotem] EnsureTotemSlotsEarly error: {ex}");
            }
        }

        internal static int GetExtra()
        {
            // Clamp persisted value to safe bounds [0, MaxExtraSlots]
            int v = PlayerPrefs.GetInt(PrefKeyExtra, 1);
            if (v < 0) v = 0;
            if (v > MaxExtraSlots) v = MaxExtraSlots;
            return v; // default to 1 extra so players see the 3rd slot by default
        }
        private static void SaveExtra(int extra)
        {
            // Clamp before save
            if (extra < 0) extra = 0;
            if (extra > MaxExtraSlots) extra = MaxExtraSlots;
            PlayerPrefs.SetInt(PrefKeyExtra, extra);
            PlayerPrefs.Save();
        }

        // Ensure all relevant SlotCollections have exactly desiredTotal totem slots
        private int EnsureTotemSlotsForAll(int desiredTotal)
        {
            int changedCollections = 0;
            var cols = GetAllSlotCollections();
            foreach (var col in cols)
            {
                if (col == null || col.list == null) continue;
                var totemSlots = col.list.Where(IsTotemSlot).ToList();
                int count = totemSlots.Count;
                if (count < BaseTotemSlots) continue; // not an equipment collection
                if (count == desiredTotal) continue;

                if (count < desiredTotal)
                {
                    var tpl = totemSlots[0];
                    int need = desiredTotal - count;
                    Slot lastChanged = null;
                    for (int i = 0; i < need; i++)
                    {
                        string newKey = NextIncrementalKey(totemSlots.Select(s => s.Key), tpl.Key);
                        var ns = new Slot(newKey)
                        {
                            SlotIcon = tpl.SlotIcon
                        };
                        if (tpl.requireTags != null) ns.requireTags.AddRange(tpl.requireTags);
                        if (tpl.excludeTags != null) ns.excludeTags.AddRange(tpl.excludeTags);
                        try
                        {
                            var f = typeof(Slot).GetField("forbidItemsWithSameID", BindingFlags.NonPublic | BindingFlags.Instance);
                            if (f != null)
                            {
                                var v = (bool)f.GetValue(tpl);
                                f.SetValue(ns, v);
                            }
                        }
                        catch { }

                        ns.Initialize(col);
                        try
                        {
                            if (col.OnSlotContentChanged != null)
                                ns.onSlotContentChanged += col.OnSlotContentChanged;
                            var evField = typeof(Slot).GetField("onSlotContentChanged", BindingFlags.NonPublic | BindingFlags.Instance);
                            if (evField != null)
                            {
                                var del = evField.GetValue(tpl) as Action<Slot>;
                                if (del != null)
                                {
                                    foreach (var d in del.GetInvocationList())
                                    {
                                        if (d is Action<Slot> a) ns.onSlotContentChanged += a;
                                    }
                                }
                            }
                        }
                        catch { }

                        col.Add(ns);
                        totemSlots.Add(ns);
                        lastChanged = ns;
                    }
                    // Coalesce event storm: notify once at the end
                    if (lastChanged != null) { try { lastChanged.ForceInvokeSlotContentChangedEvent(); } catch { } }
                }
                else // count > desiredTotal
                {
                    int removable = count - desiredTotal;
                    for (int i = 0; i < removable; i++)
                    {
                        var s = totemSlots[totemSlots.Count - 1 - i];
                        try { s.Unplug(); } catch { }
                        try { col.Remove(s); } catch { }
                    }
                    // Fire a single change event on the last remaining slot if possible
                    var notify = totemSlots.Count > removable ? totemSlots[totemSlots.Count - 1 - removable] : null;
                    if (notify != null) { try { notify.ForceInvokeSlotContentChangedEvent(); } catch { } }
                }

                InvalidateSlotCollectionCache(col, totemSlots.FirstOrDefault()?.Key);
                changedCollections++;
                // After changing slots, also ensure the UI container is scrollable and height-limited
                TryEnsureScrollableEquipmentUI(col);
            }
            return changedCollections;
        }

        // Public-ish helper: ensure all equipment collections with many totem slots have a scrollable UI
        private void TryEnsureScrollableEquipmentUIForAll()
        {
            try
            {
                var cols = GetAllSlotCollections();
                if (_verbose) Debug.Log($"[MoreTotem] UI ensure scan: {cols?.Length ?? 0} SlotCollections");
                foreach (var col in cols)
                {
                    if (col == null || col.list == null) continue;
                    try
                    {
                        // Identify equipment collections by presence of totem slots
                        int totemCount = col.list.Count(IsTotemSlot);
                        if (totemCount >= BaseTotemSlots)
                        {
                            TryEnsureScrollableEquipmentUI(col);
                        }
                        else if (_verbose && totemCount > 0)
                        {
                            Debug.Log($"[MoreTotem] Skip UI ensure (not equip col?): totemCount={totemCount} path={GetTransformPath(col.transform)}");
                        }
                    }
                    catch { }
                }
                // Additionally, run a grid-name based pass for known UI: ItemSlotsDisplay, once per scene
                int activeScene = SceneManager.GetActiveScene().buildIndex;
                if (_twoRowAdjustedSceneIndex != activeScene)
                {
                    _twoRowAdjustedSceneIndex = activeScene;
                    _twoRowAdjustedThisScene = false;
                }
                if (!_twoRowAdjustedThisScene)
                {
                    TryEnsureTwoRowHeightForItemSlotsDisplays();
                    _twoRowAdjustedThisScene = true;
                }
            }
            catch { }
        }

        // Core routine: for a given SlotCollection, find its GridLayoutGroup and add ScrollRect + RectMask2D
        private void TryEnsureScrollableEquipmentUI(SlotCollection col)
        {
            try
            {
                if (_scrollFixed.Contains(col.GetInstanceID())) return;

                // Find an associated GridLayoutGroup near the collection
                GridLayoutGroup grid = null;
                RectTransform gridRT = null;
                // Prefer child grids
                grid = col.GetComponentInChildren<GridLayoutGroup>(true);
                if (grid == null)
                {
                    // Try parents within a short range
                    var t = col.transform;
                    int hops = 0;
                    while (grid == null && t != null && hops < 6)
                    {
                        grid = t.GetComponentInChildren<GridLayoutGroup>(true);
                        t = t.parent;
                        hops++;
                    }
                }
                if (grid == null)
                {
                    if (_verbose)
                        Debug.Log($"[MoreTotem] No GridLayoutGroup found near {GetTransformPath(col.transform)}; UI not modified.");
                    return; // no UI grid nearby
                }
                gridRT = grid.transform as RectTransform;
                if (gridRT == null) return;

                // Viewport will be the parent RectTransform of the grid
                var viewport = gridRT.parent as RectTransform;
                if (viewport == null)
                {
                    if (_verbose) Debug.Log($"[MoreTotem] Grid has no RectTransform parent for viewport: {GetTransformPath(gridRT)}");
                    return;
                }

                // Add or reuse ScrollRect on the viewport
                var sr = viewport.GetComponent<ScrollRect>();
                if (sr == null) sr = viewport.gameObject.AddComponent<ScrollRect>();
                sr.horizontal = false;
                sr.vertical = true;
                sr.movementType = ScrollRect.MovementType.Clamped;
                sr.viewport = viewport;
                sr.content = gridRT;
                sr.scrollSensitivity = 20f;

                // Ensure masking to clip overflow
                if (viewport.GetComponent<RectMask2D>() == null)
                {
                    viewport.gameObject.AddComponent<RectMask2D>();
                }

                // Limit height to exactly two rows of icons
                float cellH = grid.cellSize.y;
                float spacingY = grid.spacing.y;
                var pad = grid.padding;
                int rows = 2;
                float viewportHeight = pad.top + pad.bottom + (rows * cellH) + ((rows - 1) * spacingY);

                // Apply height on viewport via LayoutElement to avoid fighting other layout systems
                var le = viewport.GetComponent<LayoutElement>();
                if (le == null) le = viewport.gameObject.AddComponent<LayoutElement>();
                le.minHeight = viewportHeight;
                le.preferredHeight = viewportHeight;
                le.flexibleHeight = 0f;

                // As a fallback, also try sizeDelta when anchors allow
                try
                {
                    if (Mathf.Approximately(viewport.anchorMin.y, viewport.anchorMax.y))
                    {
                        var sd = viewport.sizeDelta;
                        sd.y = viewportHeight;
                        viewport.sizeDelta = sd;
                    }
                }
                catch { }

                // Mark as fixed to avoid re-adding components
                if (viewport.GetComponent<MoreTotemScrollMarker>() == null)
                {
                    viewport.gameObject.AddComponent<MoreTotemScrollMarker>();
                }
                _scrollFixed.Add(col.GetInstanceID());
                if (_verbose)
                {
                    Debug.Log($"[MoreTotem] Scroll UI ensured: grid={GetTransformPath(gridRT)}, viewport={GetTransformPath(viewport)} height={viewportHeight:F1}");
                }
            }
            catch { }
        }

        // Scan by UI naming convention: limit parent (ItemSlotsDisplay) height to two rows.
        private static int TryEnsureTwoRowHeightForItemSlotsDisplays()
        {
            int affected = 0;
            try
            {
                var grids = GetAllGrids();
                foreach (var g in grids)
                {
                    if (g == null) continue;
                    var rt = g.transform as RectTransform;
                    if (rt == null) continue;
                    var parent = rt.parent as RectTransform;
                    if (parent == null) continue;
                    string pn = parent.gameObject.name ?? string.Empty;
                    if (pn.IndexOf("ItemSlotsDisplay", StringComparison.OrdinalIgnoreCase) < 0) continue;

                    // Compute two-row height based on this grid
                    var pad = g.padding; float h = pad.top + pad.bottom + (2 * g.cellSize.y) + (1 * g.spacing.y);
                    var le = parent.GetComponent<LayoutElement>();
                    if (le == null) le = parent.gameObject.AddComponent<LayoutElement>();
                    bool changed = !Mathf.Approximately(le.preferredHeight, h) || le.flexibleHeight != 0f;
                    le.minHeight = h; le.preferredHeight = h; le.flexibleHeight = 0f;

                    // Ensure the parent behaves as a vertical viewport with scrolling
                    var viewport = parent;
                    var sr = viewport.GetComponent<ScrollRect>();
                    if (sr == null)
                    {
                        sr = viewport.gameObject.AddComponent<ScrollRect>();
                        sr.horizontal = false;
                        sr.vertical = true;
                        sr.movementType = ScrollRect.MovementType.Clamped;
                    }
                    sr.viewport = viewport;
                    sr.content = rt;

                    // Clip overflow
                    if (viewport.GetComponent<RectMask2D>() == null)
                    {
                        viewport.gameObject.AddComponent<RectMask2D>();
                    }

                    // Make sure content anchors/pivot are at top so it scrolls naturally
                    try
                    {
                        if (!Mathf.Approximately(rt.anchorMin.x, 0f) || !Mathf.Approximately(rt.anchorMax.x, 1f))
                        {
                            rt.anchorMin = new Vector2(0f, rt.anchorMin.y);
                            rt.anchorMax = new Vector2(1f, rt.anchorMax.y);
                        }
                        rt.pivot = new Vector2(0.5f, 1f);
                        rt.anchorMin = new Vector2(rt.anchorMin.x, 1f);
                        rt.anchorMax = new Vector2(rt.anchorMax.x, 1f);
                        var ap = rt.anchoredPosition; ap.y = 0f; rt.anchoredPosition = ap;
                    }
                    catch { }

                    // Mark with a small component to avoid duplicate setup by other passes
                    if (viewport.GetComponent<MoreTotemScrollMarker>() == null)
                    {
                        viewport.gameObject.AddComponent<MoreTotemScrollMarker>();
                    }

                    if (changed)
                    {
                        affected++;
                        if (_verbose)
                        {
                            string path = GetTransformPath(parent);
                            Debug.Log($"[MoreTotem] Two-row height set on {path} -> {h:F1} (Scroll UI ensured)");
                        }
                    }
                }
            }
            catch { }
            return affected;
        }

        // Diagnostics: dump all GridLayoutGroup with object paths and immediate RT size
        private static void DumpAllGrids()
        {
            try
            {
                var grids = GetAllGrids();
                Debug.Log($"[MoreTotem] Dump grids: {grids.Length}");
                foreach (var g in grids)
                {
                    var rt = g.transform as RectTransform;
                    if (rt == null) continue;
                    var vp = rt.parent as RectTransform;
                    string msg = $"grid={GetTransformPath(rt)} cell=({g.cellSize.x},{g.cellSize.y}) spacing=({g.spacing.x},{g.spacing.y}) parent={(vp!=null?GetTransformPath(vp):"<none>")}";
                    Debug.Log(msg);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[MoreTotem] DumpAllGrids error: " + ex.Message);
            }
        }

        // Try to change gem slots on the currently selected weapon (heuristic based)
        private int ChangeGemSlotsOnSelectedWeapon(int delta)
        {
            try
            {
                var col = _overrideWeaponCol != null ? _overrideWeaponCol : PickSelectedWeaponCollectionAllowEmpty();
                if (col == null || col.list == null)
                {
                    Debug.LogWarning("[MoreTotem] No weapon gem SlotCollection candidate found.");
                    return 0;
                }
                int current = col.list.Count(IsGemSlot);
                int desired = Math.Max(0, current + delta);
                int r = EnsureSlotsForCollection(col, IsGemSlot, desired);
                // Persist desired for this item type key
                string key = GetItemTypeKey(col);
                if (!string.IsNullOrEmpty(key)) SaveGemCountForKey(key, desired);
                // Sync to owner weapon numeric field if any
                TrySetWeaponGemCountOnOwner(col, desired);
                return r;
            }
            catch (Exception ex)
            {
                Debug.LogError("[MoreTotem] ChangeGemSlotsOnSelectedWeapon error: " + ex);
                return 0;
            }
        }

        // Return current gem count and the candidate collection if any
        private int TryGetSelectedWeaponGemCount(out SlotCollection? col)
        {
            col = null;
            try
            {
                col = _overrideWeaponCol != null ? _overrideWeaponCol : PickSelectedWeaponCollectionAllowEmpty();
                if (col != null && col.list != null)
                {
                    return col.list.Count(IsGemSlot);
                }
            }
            catch { }
            return -1;
        }

        // Prefer: with gem slots; else weapon-like; else any nearby collection
        private SlotCollection? PickSelectedWeaponGemCollection()
        {
            var cols = GetAllSlotCollections();
            // Candidates with at least one gem-like slot
            var candidates = cols.Where(c => c != null && c.list != null && c.list.Any(IsGemSlot)).ToList();
            if (candidates.Count == 0) return null;

            // Prefer active ones
            var active = candidates.Where(c => c.isActiveAndEnabled).ToList();
            if (active.Count > 0) candidates = active;

            // Prefer collections whose GameObject name hints weapon
            candidates = candidates
                .OrderByDescending(c => NameContainsAny(c.gameObject?.name, WeaponKeywords))
                .ThenByDescending(c => TransformPathContainsAny(c.transform, WeaponKeywords))
                .ToList();

            return candidates.FirstOrDefault();
        }

        private SlotCollection? PickSelectedWeaponCollectionAllowEmpty()
        {
            // First try existing behavior
            var withGem = PickSelectedWeaponGemCollection();
            if (withGem != null) return withGem;

            // Fallback: any SlotCollection that looks like a weapon container
            var cols = GetAllSlotCollections();
            var candidates = cols.Where(c => c != null && c.list != null).ToList();
            var active = candidates.Where(c => c.isActiveAndEnabled).ToList();
            if (active.Count > 0) candidates = active;
            candidates = candidates
                .OrderByDescending(c => NameContainsAny(c.gameObject?.name, WeaponKeywords))
                .ThenByDescending(c => TransformPathContainsAny(c.transform, WeaponKeywords))
                .ThenByDescending(c => c.list.Any())
                .ToList();
            return candidates.FirstOrDefault();
        }

        private static bool NameContainsAny(string name, string[] keys)
        {
            if (string.IsNullOrEmpty(name)) return false;
            foreach (var k in keys)
            {
                if (name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        private static bool TransformPathContainsAny(Transform t, string[] keys)
        {
            while (t != null)
            {
                if (NameContainsAny(t.name, keys)) return true;
                t = t.parent;
            }
            return false;
        }

        // Generalized ensure for one collection by predicate
        private int EnsureSlotsForCollection(SlotCollection col, Func<Slot, bool> predicate, int desired)
        {
            if (col == null || col.list == null) return 0;
            var matches = col.list.Where(predicate).ToList();
            int count = matches.Count;
            if (count == desired) return 0;
            // If no existing match but need to add, we must choose a template from global
            Slot? tpl = null;
            if (count == 0 && desired > 0)
            {
                tpl = GetGlobalGemSlotTemplate() ?? GetAnySlotTemplate();
                if (tpl == null)
                {
                    Debug.LogWarning("[MoreTotem] No template slot found to create new gem slots.");
                    return 0;
                }
            }
            else if (count > 0)
            {
                tpl = matches[0];
            }

            if (count < desired)
            {
                int need = desired - count;
                Slot lastChanged = null;
                for (int i = 0; i < need; i++)
                {
                    string baseKey = tpl != null && !string.IsNullOrEmpty(tpl.Key) ? tpl.Key : "gem";
                    string newKey = NextIncrementalKey(matches.Select(s => s.Key), baseKey);
                    var ns = new Slot(newKey)
                    {
                        SlotIcon = tpl != null ? tpl.SlotIcon : null
                    };
                    if (tpl != null)
                    {
                        if (tpl.requireTags != null) ns.requireTags.AddRange(tpl.requireTags);
                        if (tpl.excludeTags != null) ns.excludeTags.AddRange(tpl.excludeTags);
                    }
                    // 强制设置为“宝石槽”语义，移除显卡相关的标签，避免序列化后被识别为显卡槽
                    try
                    {
                        var gemTag = FindOrCreateTag(ns, GemKeywords);
                        var listObj = ns.requireTags as System.Collections.IList;
                        if (gemTag != null && listObj != null)
                        {
                            bool exists = false;
                            foreach (var t in listObj) { if (ReferenceEquals(t, gemTag)) { exists = true; break; } }
                            if (!exists) listObj.Add(gemTag);
                        }
                        RemoveTagsByKeywords(ns, NonGemKeywords);
                    }
                    catch { }
                    try
                    {
                        var f = typeof(Slot).GetField("forbidItemsWithSameID", BindingFlags.NonPublic | BindingFlags.Instance);
                        if (f != null)
                        {
                            if (tpl != null)
                            {
                                var v = (bool)f.GetValue(tpl);
                                f.SetValue(ns, v);
                            }
                        }
                    }
                    catch { }

                    ns.Initialize(col);
                    try
                    {
                        if (col.OnSlotContentChanged != null)
                            ns.onSlotContentChanged += col.OnSlotContentChanged;
                        if (tpl != null)
                        {
                            var evField = typeof(Slot).GetField("onSlotContentChanged", BindingFlags.NonPublic | BindingFlags.Instance);
                            if (evField != null)
                            {
                                var del = evField.GetValue(tpl) as Action<Slot>;
                                if (del != null)
                                {
                                    foreach (var d in del.GetInvocationList())
                                    {
                                        if (d is Action<Slot> a) ns.onSlotContentChanged += a;
                                    }
                                }
                            }
                        }
                    }
                    catch { }

                    col.Add(ns);
                    matches.Add(ns);
                    lastChanged = ns;
                }
                if (lastChanged != null) { try { lastChanged.ForceInvokeSlotContentChangedEvent(); } catch { } }
            }
            else // count > desired
            {
                int removable = count - desired;
                for (int i = 0; i < removable; i++)
                {
                    var s = matches[matches.Count - 1 - i];
                    try { s.Unplug(); } catch { }
                    try { col.Remove(s); } catch { }
                }
                var notify = matches.Count > removable ? matches[matches.Count - 1 - removable] : null;
                if (notify != null) { try { notify.ForceInvokeSlotContentChangedEvent(); } catch { } }
            }

            InvalidateSlotCollectionCache(col, matches.FirstOrDefault()?.Key);
            return 1;
        }

        private static Slot? GetGlobalGemSlotTemplate()
        {
            try
            {
                var cols = GetAllSlotCollections();
                foreach (var col in cols)
                {
                    if (col == null || col.list == null) continue;
                    var s = col.list.FirstOrDefault(IsGemSlot);
                    if (s != null) return s;
                }
            }
            catch { }
            return null;
        }

        private static Slot? GetAnySlotTemplate()
        {
            try
            {
                var cols = GetAllSlotCollections();
                foreach (var col in cols)
                {
                    if (col == null || col.list == null || col.list.Count == 0) continue;
                    return col.list[0];
                }
            }
            catch { }
            return null;
        }

        private static bool IsGemSlot(Slot s)
        {
            if (s == null) return false;
            string k = s.Key ?? string.Empty;
            string name = s.DisplayName ?? string.Empty;
            // Exclude known non-gem markers first
            if (ContainsAny(k, NonGemKeywords) || ContainsAny(name, NonGemKeywords)) return false;
            bool byKey = ContainsAny(k, GemKeywords);
            bool byName = ContainsAny(name, GemKeywords);
            if (byKey || byName) return true;
            string tagName = SafeFirstRequireTagName(s);
            if (ContainsAny(tagName, NonGemKeywords)) return false;
            return ContainsAny(tagName, GemKeywords);
        }

        private static bool ContainsAny(string text, string[] keys)
        {
            foreach (var kw in keys)
            {
                if (!string.IsNullOrEmpty(text) && text.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        // 旧的检查/调试逻辑已移除

        private static void RefreshGemInfo()
        {
            try
            {
                var col = _overrideWeaponCol;
                if (col != null && col.list != null)
                    _lastGemCount = col.list.Count(IsGemSlot);
                else
                    _lastGemCount = -1;
            }
            catch { _lastGemCount = -1; }
        }

        // Static helper to get gem count once (using override if set)
        private static int TryStaticGetSelectedWeaponGemCount()
        {
            try
            {
                SlotCollection tmp;
                var inst = Instance;
                if (inst == null) return -1;
                return inst.TryGetSelectedWeaponGemCount(out tmp);
            }
            catch { return -1; }
        }

        // 旧的鼠标/全局候选收集已移除

        private static IEnumerable<SlotCollection> GetCollectionsNear(GameObject go)
        {
            var found = new List<SlotCollection>();
            try { found.AddRange(go.GetComponents<SlotCollection>()); } catch { }
            try { found.AddRange(go.GetComponentsInChildren<SlotCollection>(true)); } catch { }
            try { found.AddRange(go.GetComponentsInParent<SlotCollection>(true)); } catch { }
            return found.Distinct();
        }

        private static GameObject RaycastTopUI()
        {
            if (EventSystem.current == null) return null;
            var ped = new PointerEventData(EventSystem.current) { position = Input.mousePosition };
            // 优先当前激活的 GraphicRaycaster（不枚举全部）
            var esGo = EventSystem.current.gameObject;
            var gr = esGo != null ? esGo.GetComponentInParent<GraphicRaycaster>() : null;
            if (gr == null)
            {
                // 退一步只取第一个激活的
                gr = Resources.FindObjectsOfTypeAll<GraphicRaycaster>().FirstOrDefault(g => g.isActiveAndEnabled && g.gameObject.activeInHierarchy);
            }
            if (gr == null) return null;
            var results = new List<RaycastResult>();
            gr.Raycast(ped, results);
            return results.Count > 0 ? results[0].gameObject : null;
        }

        private static List<SlotCollection> OrderCandidates(IEnumerable<SlotCollection> input)
        {
            return input
                .Where(c => c != null)
                .OrderByDescending(c => c.isActiveAndEnabled)
                .ThenByDescending(c => { try { return c.list != null && c.list.Any(IsGemSlot); } catch { return false; } })
                .ThenByDescending(c => NameContainsAny(c.gameObject?.name, WeaponKeywords) || TransformPathContainsAny(c.transform, WeaponKeywords))
                .ThenByDescending(c => { try { return c.list != null && c.list.Count > 0; } catch { return false; } })
                .ToList();
        }

        private static string GetTransformPath(Transform t)
        {
            if (t == null) return "<null>";
            var stack = new List<string>();
            while (t != null)
            {
                stack.Add(t.name);
                t = t.parent;
            }
            stack.Reverse();
            return string.Join("/", stack);
        }

        private static void InvalidateSlotCollectionCache(SlotCollection col, string anyKeyToTouch)
        {
            try
            {
                var cacheField = typeof(SlotCollection).GetField("_cachedSlotsDictionary", BindingFlags.NonPublic | BindingFlags.Instance);
                cacheField?.SetValue(col, null);
            }
            catch { }

            try { if (!string.IsNullOrEmpty(anyKeyToTouch)) _ = col.GetSlot(anyKeyToTouch); } catch { }
        }

        // Tag utilities to enforce gem semantics and strip non-gem semantics
        private static object FindOrCreateTag(Slot slot, string[] keywords)
        {
            try
            {
                // Prefer existing tags that match gem keywords on this slot
                var listObj = slot.requireTags as System.Collections.IList;
                if (listObj != null)
                {
                    foreach (var t in listObj)
                    {
                        if (t == null) continue;
                        var n = t.GetType().GetProperty("name")?.GetValue(t) as string;
                        if (ContainsAny(n ?? string.Empty, keywords)) return t;
                    }
                }
            }
            catch { }
            return null;
        }

        private static void RemoveTagsByKeywords(Slot slot, string[] keywords)
        {
            try
            {
                var listObj = slot.requireTags as System.Collections.IList;
                if (listObj == null || listObj.Count == 0) return;
                for (int i = listObj.Count - 1; i >= 0; i--)
                {
                    var t = listObj[i];
                    if (t == null) continue;
                    var n = t.GetType().GetProperty("name")?.GetValue(t) as string ?? string.Empty;
                    if (ContainsAny(n, keywords)) listObj.RemoveAt(i);
                }
            }
            catch { }
        }

        // Persistence helpers
        private static string GetItemTypeKey(SlotCollection col)
        {
            try
            {
                var item = col.GetComponentInParent<Item>(true);
                if (item != null)
                {
                    // Prefer a stable TypeID if exists
                    var t = item.GetType();
                    var pi = t.GetProperty("TypeID", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                    if (pi != null)
                    {
                        var v = pi.GetValue(item);
                        if (v != null)
                        {
                            return (t.FullName ?? t.Name) + "#" + v.ToString();
                        }
                    }
                    var fi = t.GetField("TypeID", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                    if (fi != null)
                    {
                        var v = fi.GetValue(item);
                        if (v != null)
                        {
                            return (t.FullName ?? t.Name) + "#" + v.ToString();
                        }
                    }
                    return t.FullName ?? t.Name;
                }
            }
            catch { }
            // Fallback: transform path
            try { return "GO:" + GetTransformPath(col.transform); } catch { }
            return "";
        }

        private static string SanitizePrefKey(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            var chars = raw.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (!(char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-' || c == '#'))
                    chars[i] = '_';
            }
            return new string(chars);
        }

        private static void SaveGemCountForKey(string itemTypeKey, int gems)
        {
            try
            {
                string key = PrefKeyGemPrefix + SanitizePrefKey(itemTypeKey);
                PlayerPrefs.SetInt(key, gems);
                PlayerPrefs.Save();
                Debug.Log($"[MoreTotem] Saved gem count {gems} for '{itemTypeKey}'");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[MoreTotem] SaveGemCountForKey failed: " + ex.Message);
            }
        }

        private static bool TryLoadGemCountForKey(string itemTypeKey, out int gems)
        {
            gems = -1;
            try
            {
                string key = PrefKeyGemPrefix + SanitizePrefKey(itemTypeKey);
                if (PlayerPrefs.HasKey(key))
                {
                    gems = Mathf.Max(0, PlayerPrefs.GetInt(key, -1));
                    return gems >= 0;
                }
            }
            catch { }
            return false;
        }

        private int ApplySavedGemCountsForAll(int maxPerPass)
        {
            int applied = 0;
            try
            {
                var cols = GetAllSlotCollections();
                foreach (var col in cols)
                {
                    if (col == null || col.list == null) continue;
                    string key = GetItemTypeKey(col);
                    if (string.IsNullOrEmpty(key)) continue;
                    if (TryLoadGemCountForKey(key, out var desired))
                    {
                        int current = 0;
                        try { current = col.list.Count(IsGemSlot); } catch { }
                        if (current != desired)
                        {
                            EnsureSlotsForCollection(col, IsGemSlot, desired);
                            applied++;
                            if (applied >= maxPerPass) break;
                        }
                    }
                }
            }
            catch { }
            return applied;
        }

        // ---- Cached enumeration helpers ----
        private static SlotCollection[] GetAllSlotCollections()
        {
            int scene = SceneManager.GetActiveScene().buildIndex;
            if (_colsCacheSceneIndex != scene || _colsCache == null || _colsCache.Length == 0)
            {
                try { _colsCache = Resources.FindObjectsOfTypeAll<SlotCollection>() ?? Array.Empty<SlotCollection>(); }
                catch { _colsCache = Array.Empty<SlotCollection>(); }
                _colsCacheSceneIndex = scene;
            }
            return _colsCache;
        }

        private static GridLayoutGroup[] GetAllGrids()
        {
            int scene = SceneManager.GetActiveScene().buildIndex;
            if (_gridsCacheSceneIndex != scene || _gridsCache == null || _gridsCache.Length == 0)
            {
                try { _gridsCache = Resources.FindObjectsOfTypeAll<GridLayoutGroup>() ?? Array.Empty<GridLayoutGroup>(); }
                catch { _gridsCache = Array.Empty<GridLayoutGroup>(); }
                _gridsCacheSceneIndex = scene;
            }
            return _gridsCache;
        }

        private static void ResetPerSceneFlags(int sceneIndex)
        {
            // invalidate caches and per-scene once flags
            _colsCache = Array.Empty<SlotCollection>();
            _gridsCache = Array.Empty<GridLayoutGroup>();
            _colsCacheSceneIndex = sceneIndex; _gridsCacheSceneIndex = sceneIndex;
            _twoRowAdjustedSceneIndex = sceneIndex; _twoRowAdjustedThisScene = false;
            _scrollFixed.Clear();
            _gemAppliedSceneIndex = -1;
            _lastGemEnsureSceneIndex = -1; // allow scene-load routine to run gems
        }

        

        private void TryApplySavedGemsOncePerScene(int budget)
        {
            int scene = SceneManager.GetActiveScene().buildIndex;
            if (_gemAppliedSceneIndex == scene) return;
            var applied = ApplySavedGemCountsForAll(maxPerPass: budget);
            // mark as done regardless to avoid repeated global scans; late UI can be handled by manual actions
            _gemAppliedSceneIndex = scene;
            if (applied > 0)
            {
                _lastGemEnsureTime = Time.realtimeSinceStartup;
                _lastGemEnsureSceneIndex = scene;
            }
        }

        private static void LogCurrentTargetInfo()
        {
            var col = _overrideWeaponCol;
            if (col == null)
            {
                Debug.Log("[MoreTotem] No target bound.");
                return;
            }
            int total = 0, gems = 0, totems = 0;
            try { total = col.list != null ? col.list.Count : 0; } catch { }
            try { gems = col.list != null ? col.list.Count(IsGemSlot) : 0; } catch { }
            try { totems = col.list != null ? col.list.Count(IsTotemSlot) : 0; } catch { }
            string key = GetItemTypeKey(col);
            Debug.Log($"[MoreTotem] Target info: Path='{GetTransformPath(col.transform)}' Total={total} Gems={gems} Totems={totems} Key='{key}'");
        }

        private static SlotCollection? PickDeterministicTarget()
        {
            SlotCollection bound = null;
            // 1) Inspect panel
            if (TryBindFromDuckovDetailsStrict(out var p) && p != null) bound = p;
            // 严格模式：不做其他回退
            return bound;
        }

        // 仅使用 Duckov 官方面板 API：Duckov.UI.ItemDetailsPanel / Duckov.UI.ItemDetailsDisplay
        private static bool TryBindFromDuckovDetailsStrict(out SlotCollection bound)
        {
            bound = null;
            try
            {
                var names = new[] {
                    "Duckov.UI.ItemDetailsPanel",
                    "Duckov.UI.ItemDetailsDisplay",
                    // 某些版本通过 Tooltips 组件承载详情
                    "Duckov.UI.Tooltips",
                    "Duckov.UI.TooltipsProvider"
                };
                var memberNames = new[] {
                    "CurrentItem","currentItem","Item","item","TargetItem","targetItem","Target","target",
                    "ShownItem","shownItem","DisplayedItem","displayedItem","DisplayingItem","displayingItem",
                    "InspectingItem","inspectingItem","InspectedItem","inspectedItem",
                    "m_CurrentItem","_currentItem","mItem","_item"
                };
                // 先用 Type.GetType 直接解析，避免枚举所有类型
                foreach (var tn in names)
                {
                    var t = Type.GetType(tn);
                    if (t == null)
                    {
                        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                        {
                            t = a.GetType(tn, throwOnError: false);
                            if (t != null) break;
                        }
                    }
                    if (t == null) continue;
                    // 收集候选实例：静态 Instance 或场景中的该类型对象
                    var insts = new List<object>();
                    var inst = t.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null)
                               ?? t.GetField("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
                    if (inst != null) insts.Add(inst);
                    try
                    {
                        var arr = Resources.FindObjectsOfTypeAll(t);
                        foreach (var o in arr) if (o != null && !insts.Contains(o)) insts.Add(o);
                    }
                    catch { }

                    foreach (var obj in insts)
                    {
                        if (obj == null) continue;
                        // 仅使用激活的 UI 对象，提高精确度
                        if (obj is Component cc)
                        {
                            if (cc.gameObject == null || !cc.gameObject.activeInHierarchy) continue;
                        }

                        // 提取当前物品
                        GameObject go = null;
                        object itemObj = null;
                        string memberUsed = "";
                        bool instanceIsStatic = (inst != null && ReferenceEquals(obj, inst));
                        foreach (var mn in memberNames)
                        {
                            var pi = t.GetProperty(mn, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            if (pi != null) { itemObj = pi.GetValue(obj); if (itemObj != null) { memberUsed = mn; break; } }
                            var fi = t.GetField(mn, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            if (fi != null) { itemObj = fi.GetValue(obj); if (itemObj != null) { memberUsed = mn; break; } }
                        }
                        if (itemObj == null)
                        {
                            // 兜底：在该实例的所有成员中查找 ItemStatsSystem.Item 类型
                            var itemType = Type.GetType("ItemStatsSystem.Item") ?? AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("ItemStatsSystem.Item")).FirstOrDefault(x => x != null);
                            if (itemType != null)
                            {
                                foreach (var m in t.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                                {
                                    try
                                    {
                                        Type mt = null; object val = null;
                                        if (m is PropertyInfo pi2) { mt = pi2.PropertyType; if (pi2.CanRead) val = pi2.GetValue(obj); }
                                        else if (m is FieldInfo fi2) { mt = fi2.FieldType; val = fi2.GetValue(obj); }
                                        else continue;
                                        if (val == null || mt == null) continue;
                                        if (itemType.IsAssignableFrom(mt)) { itemObj = val; memberUsed = "#ItemTypeScan:" + m.Name; break; }
                                    }
                                    catch { }
                                }
                            }
                        }
                        if (itemObj == null) continue;

                        if (itemObj is Component comp) go = comp.gameObject;
                        else if (itemObj is GameObject igo) go = igo;
                        else
                        {
                            var tr = itemObj.GetType().GetProperty("transform", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(itemObj) as Transform;
                            if (tr != null) go = tr.gameObject;
                        }
                        if (go == null) continue;

                        var col = go.GetComponentInChildren<SlotCollection>(true) ?? go.GetComponentInParent<SlotCollection>(true);
                        if (col != null)
                        {
                            bound = col;
                            int gems = 0; try { gems = col.list != null ? col.list.Count(IsGemSlot) : 0; } catch { }
                            _lastStrictMatchInfo = $"panel={t.FullName}, inst={(instanceIsStatic ? "static" : "scene")}, member={(string.IsNullOrEmpty(memberUsed)?"?":memberUsed)}, itemGO={GetTransformPath(go.transform)}, colGO={GetTransformPath(col.transform)}, gems={gems}";
                            return true;
                        }
                    }
                }
                Debug.Log("[MoreTotem] Strict Mode: The current item was not found in the Duckov details panel instance.");
            }
            catch { }
            return false;
        }



        private static bool TryExtractAllItemGameObjectsFromPanel(object inst, Type t, out List<GameObject> list)
        {
            list = new List<GameObject>();
            try
            {
                var itemType = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType("ItemStatsSystem.Item")).FirstOrDefault(tp => tp != null);
                foreach (var m in t.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    try
                    {
                        Type mt = null; object val = null;
                        switch (m)
                        {
                            case PropertyInfo pi: mt = pi.PropertyType; if (pi.CanRead) val = pi.GetValue(inst); break;
                            case FieldInfo fi: mt = fi.FieldType; val = fi.GetValue(inst); break;
                            default: continue;
                        }
                        if (val == null) continue;

                        GameObject go = null;
                        if (itemType != null && mt != null && itemType.IsAssignableFrom(mt))
                        {
                            if (val is Component c1) go = c1.gameObject;
                            else if (val is GameObject ig) go = ig;
                            else
                            {
                                var tr = mt.GetProperty("transform", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(val) as Transform;
                                if (tr != null) go = tr.gameObject;
                            }
                        }
                        else if (m.Name.IndexOf("item", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            if (val is Component c2) go = c2.gameObject;
                            else if (val is GameObject go2) go = go2;
                            else
                            {
                                var tr2 = mt?.GetProperty("transform", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(val) as Transform;
                                if (tr2 != null) go = tr2.gameObject;
                            }
                        }
                        if (go != null) list.Add(go);
                    }
                    catch { }
                }
            }
            catch { }
            return list.Count > 0;
        }


        // 移除旧的候选缓存与循环切换

        // 移除旧的已知面板名绑定（改为严格面板专用逻辑）

        private static bool TryResolvePanelCurrentItem(Type panelType, out GameObject go)
        {
            go = null;
            try
            {
                var inst = panelType.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null)
                           ?? panelType.GetField("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null)
                           ?? panelType.GetProperty("ins", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null)
                           ?? panelType.GetField("ins", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
                if (inst == null)
                {
                    // Try scene instance
                    inst = FindAnyInstanceOfType(panelType);
                    if (inst == null) return false;
                }

                // Probe common member names
                var memberNames = new[] { "CurrentItem", "TargetItem", "Item", "currentItem", "targetItem", "item" };
                object item = null;
                foreach (var n in memberNames)
                {
                    var pi = panelType.GetProperty(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (pi != null) { item = pi.GetValue(inst); if (item != null) break; }
                    var fi = panelType.GetField(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (fi != null) { item = fi.GetValue(inst); if (item != null) break; }
                }
                if (item == null)
                {
                    // 深度扫描字段/属性，找 ItemStatsSystem.Item 或任何引用到有 SlotCollection 的对象
                    if (!TryExtractItemGameObjectFromPanel(inst, panelType, out go))
                        return false;
                    return go != null;
                }

                if (item is Component comp) go = comp.gameObject;
                else if (item is GameObject ig) go = ig;
                else
                {
                    var tr = item.GetType().GetProperty("transform", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(item) as Transform;
                    if (tr != null) go = tr.gameObject;
                }
                return go != null;
            }
            catch { return false; }
        }

        private static object FindAnyInstanceOfType(Type t)
        {
            try
            {
                if (typeof(UnityEngine.Object).IsAssignableFrom(t))
                {
                    var arr = Resources.FindObjectsOfTypeAll(t);
                    if (arr != null && arr.Length > 0) return arr.GetValue(0);
                }
            }
            catch { }
            return null;
        }

        private static bool TryExtractItemGameObjectFromPanel(object inst, Type t, out GameObject go)
        {
            go = null;
            try
            {
                // 1) 先找 ItemStatsSystem.Item 类型的成员
                var itemType = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType("ItemStatsSystem.Item"))
                    .FirstOrDefault(tp => tp != null);

                foreach (var m in t.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    try
                    {
                        Type mt = null; object val = null;
                        switch (m)
                        {
                            case PropertyInfo pi:
                                mt = pi.PropertyType; val = pi.CanRead ? pi.GetValue(inst) : null; break;
                            case FieldInfo fi:
                                mt = fi.FieldType; val = fi.GetValue(inst); break;
                            default: continue;
                        }

                        if (val == null) continue;

                        // Match ItemStatsSystem.Item first
                        if (itemType != null && itemType.IsAssignableFrom(mt))
                        {
                            if (val is Component c1) { go = c1.gameObject; return true; }
                            if (val is GameObject go1) { go = go1; return true; }
                            var tr = mt.GetProperty("transform", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(val) as Transform;
                            if (tr != null) { go = tr.gameObject; return true; }
                        }

                        // 名称里包含 item 的引用，尝试解析
                        if (m.Name.IndexOf("item", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            GameObject cand = null;
                            if (val is Component c2) cand = c2.gameObject;
                            else if (val is GameObject go2) cand = go2;
                            else
                            {
                                var tr2 = mt?.GetProperty("transform", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(val) as Transform;
                                if (tr2 != null) cand = tr2.gameObject;
                            }
                            if (cand != null)
                            {
                                // 该对象/父子中如有 SlotCollection 则认为命中
                                var col = cand.GetComponentInChildren<SlotCollection>(true) ?? cand.GetComponentInParent<SlotCollection>(true);
                                if (col != null) { go = cand; return true; }
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return false;
        }

        // 移除旧的泛化面板扫描

        // 移除旧的F10扫描调试

        private static void TrySetWeaponGemCountOnOwner(SlotCollection col, int desired)
        {
            try
            {
                var owner = col.GetComponentInParent<Component>();
                if (owner == null) return;
                var t = owner.GetType();
                // Candidate member names per README
                var names = new[] { "GemSlots", "GemSlotCount", "SocketCount", "gemSlots", "gemSlotCount", "socketCount" };
                foreach (var n in names)
                {
                    var pi = t.GetProperty(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (pi != null && pi.CanWrite && (pi.PropertyType == typeof(int) || pi.PropertyType == typeof(short) || pi.PropertyType == typeof(byte)))
                    {
                        object v = Convert.ChangeType(desired, pi.PropertyType);
                        pi.SetValue(owner, v);
                        Debug.Log($"[MoreTotem] Set {t.Name}.{n} = {desired}");
                        return;
                    }
                    var fi = t.GetField(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (fi != null && (fi.FieldType == typeof(int) || fi.FieldType == typeof(short) || fi.FieldType == typeof(byte)))
                    {
                        object v = Convert.ChangeType(desired, fi.FieldType);
                        fi.SetValue(owner, v);
                        Debug.Log($"[MoreTotem] Set {t.Name}.{n} = {desired}");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[MoreTotem] TrySetWeaponGemCountOnOwner failed: " + ex.Message);
            }
        }

        private static bool IsTotemSlot(Slot s)
        {
            if (s == null) return false;
            string k = s.Key ?? string.Empty;
            string name = s.DisplayName ?? string.Empty;
            bool byKey = k.IndexOf("totem", StringComparison.OrdinalIgnoreCase) >= 0;
            bool byName = name.IndexOf("totem", StringComparison.OrdinalIgnoreCase) >= 0;
            if (byKey || byName) return true;
            string tagName = SafeFirstRequireTagName(s);
            return tagName.IndexOf("totem", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string SafeFirstRequireTagName(Slot s)
        {
            try
            {
                if (s.requireTags != null && s.requireTags.Count > 0 && s.requireTags[0] != null)
                {
                    return s.requireTags[0].name;
                }
            }
            catch { }
            return "?";
        }

        private static string NextIncrementalKey(IEnumerable<string> existingKeys, string templateKey)
        {
            string baseName = templateKey;
            int lastDigits = 0;
            int i = templateKey.Length - 1;
            while (i >= 0 && char.IsDigit(templateKey[i])) { i--; lastDigits++; }
            if (lastDigits > 0)
            {
                baseName = templateKey.Substring(0, templateKey.Length - lastDigits);
            }
            int maxIndex = -1;
            foreach (var k in existingKeys)
            {
                if (string.IsNullOrEmpty(k) || !k.StartsWith(baseName)) continue;
                int j = k.Length - 1; int digits = 0;
                while (j >= 0 && char.IsDigit(k[j])) { j--; digits++; }
                int idx = -1;
                if (digits > 0) int.TryParse(k.Substring(k.Length - digits), out idx);
                if (digits == 0) idx = 0; // treat no-suffix as index 0
                if (idx > maxIndex) maxIndex = idx;
            }
            int next = maxIndex + 1;
            return next == 0 ? baseName : $"{baseName}{next}";
        }
    }
}
