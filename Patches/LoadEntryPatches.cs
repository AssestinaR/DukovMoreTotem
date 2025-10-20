using HarmonyLib;

namespace MoreTotem.Patches
{
    // Based on runtime stack trace, hook the earliest load path
    [HarmonyPatch]
    internal static class LevelManager_InitLevel_Patch
    {
        static System.Reflection.MethodBase TargetMethod()
        {
            foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = a.GetType("LevelManager");
                if (t == null) continue;
                var m = t.GetMethod("InitLevel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (m != null) return m;
            }
            return null;
        }

        static void Prefix()
        {
            ModBehaviour.EnsureTotemSlotsEarly();
        }
    }

    [HarmonyPatch]
    internal static class CharacterCreator_CreateCharacter_Patch
    {
        static System.Reflection.MethodBase TargetMethod()
        {
            foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = a.GetType("CharacterCreator");
                if (t == null) continue;
                var m = t.GetMethod("CreateCharacter", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                if (m != null) return m;
            }
            return null;
        }

        static void Prefix()
        {
            ModBehaviour.EnsureTotemSlotsEarly();
        }
    }
}
