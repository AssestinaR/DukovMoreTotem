using HarmonyLib;

namespace MoreTotem.Patches
{
    // Ensure totem slots are expanded before the game equips/deserializes the character item
    [HarmonyPatch]
    internal static class SetItemPatches
    {
        // CharacterMainControl.SetItem prefix
        static System.Reflection.MethodBase TargetMethod()
        {
            foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = a.GetType("CharacterMainControl");
                if (t == null) continue;
                var m = t.GetMethod("SetItem", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (m != null) return m;
            }
            return null;
        }

        static void Prefix()
        {
            ModBehaviour.EnsureTotemSlotsEarly();
            ModBehaviour.TraceOnceFrom("CharacterMainControl.SetItem");
        }
    }

    [HarmonyPatch]
    internal static class EquipmentControllerSetItemPatches
    {
        static System.Reflection.MethodBase TargetMethod()
        {
            foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = a.GetType("CharacterEquipmentController");
                if (t == null) continue;
                var m = t.GetMethod("SetItem", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (m != null) return m;
            }
            return null;
        }

        static void Prefix()
        {
            ModBehaviour.EnsureTotemSlotsEarly();
            ModBehaviour.TraceOnceFrom("CharacterEquipmentController.SetItem");
        }
    }
}
