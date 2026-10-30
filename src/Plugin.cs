// Better Graves – Graveyard Keeper 2 mod (BepInEx 5)
//
// Adds a new grave cross and fence, each with a default quality of 10.
// The game's existing rules remain unchanged: grave quality is still
// (cross + fence - red skulls), capped at the number of white skulls on the body.
// A body with 20 white skulls can therefore have a maximum grave quality of 20, and one with 14 can reach 14.
//
// How it works:
//  1. Before the game builds its database caches (GameBalance.InitCache),
//     find the best vanilla cross ("gravetop" group) and fence ("gravebot" group),
//     then clone them with new IDs and a quality of 10.
//  2. Clone every recipe that references a vanilla item (crafting, placing it in a grave,
//     and removing it from a grave) and redirect it to the new item.
//     Multiply the crafting recipe's material cost (1.5x by default).
//  3. The new recipes unlock at the same time as their vanilla counterparts.
//  4. Give the new items English names and descriptions.

using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using LazyBearTechnology;

namespace BetterGraves
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "hu.burni.bettergraves";
        public const string Name = "Better Graves";
        public const string Version = "1.0.0";

        internal static ManualLogSource Log;
        internal static ConfigEntry<int> Quality;
        internal static ConfigEntry<float> CostMultiplier;

        private void Awake()
        {
            Log = Logger;

            Quality = Config.Bind("General", "Quality", 10,
                new ConfigDescription("Quality of each new cross and fence.",
                    new AcceptableValueRange<int>(1, 50)));
            CostMultiplier = Config.Bind("General", "CostMultiplier", 1.5f,
                new ConfigDescription("Material cost multiplier compared with the best vanilla item.",
                    new AcceptableValueRange<float>(0.1f, 10f)));

            new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);

            var loaded = AccessTools.Field(typeof(GameBalance), "instance")?.GetValue(null) as GameBalance;
            if (loaded != null && loaded.itemDefs != null && loaded.itemDefs.Count > 0 && !GraveContent.Injected)
            {
                Log.LogWarning("Game data was loaded before this mod; the new grave parts will not appear in this session.");
            }

            Log.LogInfo($"{Name} {Version} loaded (quality: {Quality.Value}, cost multiplier: {CostMultiplier.Value}).");
        }
    }

    internal static class GraveContent
    {
        public const string TopId = "bettergraves_cross";
        public const string BotId = "bettergraves_fence";
        private const string CraftPrefix = "bettergraves_";

        // The game's Groups.TOMBSTONE_ITEM_GROUP / Groups.FENCE_ITEM_GROUP values.
        private const string TombstoneGroup = "gravetop";
        private const string FenceGroup = "gravebot";

        public static bool Injected;

        // Cloned recipe ID -> original recipe ID (used to synchronize unlocks).
        public static readonly Dictionary<string, string> CraftOrigins = new Dictionary<string, string>();

        public static void Inject(GameBalance gb)
        {
            if (gb == null || gb.itemDefs == null || gb.craftDefs == null) return;
            // Inject once into each loaded database instance (and again if the game reloads it).
            if (gb.itemDefs.Any(d => d != null && (d.id == TopId || d.id == BotId))) return;

            try
            {
                AddPart(gb, TombstoneGroup, TopId);
                AddPart(gb, FenceGroup, BotId);
                Injected = true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Failed to add the new grave parts: " + e);
            }
        }

        private static void AddPart(GameBalance gb, string group, string newId)
        {
            ItemDef source = FindBestPart(gb, group);
            if (source == null)
            {
                Plugin.Log.LogError($"No craftable vanilla grave part found in group '{group}'.");
                return;
            }

            ItemDef item = source.Copy();
            item.id = newId;
            item.quality = Plugin.Quality.Value;
            item.itemGroupIds = new List<string>(source.itemGroupIds);
            gb.itemDefs.Add(item);

            int cloned = 0;
            foreach (CraftDef craft in gb.craftDefs.ToList())
            {
                if (craft == null || craft.id.StartsWith(CraftPrefix) || !References(craft, source.id)) continue;

                CraftDef copy = CloneCraft(craft, source.id, newId);
                copy.id = CraftPrefix + newId + "__" + craft.id;
                gb.craftDefs.Add(copy);
                CraftOrigins[copy.id] = craft.id;
                cloned++;
            }

            Plugin.Log.LogInfo($"Added '{newId}' (based on '{source.id}', quality {source.quality} -> {item.quality}), {cloned} recipe(s) cloned.");
        }

        // The highest-quality craftable item in the group (typically the Marble II cross/fence).
        private static ItemDef FindBestPart(GameBalance gb, string group)
        {
            return gb.itemDefs
                .Where(d => d != null && d.itemGroupIds != null && d.itemGroupIds.Contains(group)
                            && !d.isQuestItem && d.id != TopId && d.id != BotId
                            && gb.craftDefs.Any(c => c != null && Outputs(c.outputItems, d.id)))
                .OrderByDescending(d => d.quality)
                .ThenBy(d => d.id, StringComparer.Ordinal)
                .FirstOrDefault();
        }

        // ---- Recepies ----

        private static bool References(CraftDef c, string id)
        {
            return Needs(c.needItems, id) || Needs(c.needItemsFromWgo, id)
                || Needs(c.dropFromWgoItemsStart, id) || Needs(c.dropFromWgoItemsEnd, id)
                || Needs(c.removeItemsFromWgo, id)
                || Outputs(c.outputItems, id) || Outputs(c.addItemsToWgoOnStart, id) || Outputs(c.addItemsToWgoOnFinish, id);
        }

        private static bool Needs(List<NeedItemData> list, string id)
        {
            return list != null && list.Any(n => n != null && n.id == id);
        }

        private static bool Outputs(OutputItems o, string id)
        {
            if (o == null) return false;
            if (o.chanceOutputItems != null && o.chanceOutputItems.Any(c => c != null && c.id == id)) return true;
            return o.groupChanceOutputItems != null && o.groupChanceOutputItems.Any(g =>
                g != null && g.chanceItems != null && g.chanceItems.Any(c => c != null && c.id == id));
        }

        private static CraftDef CloneCraft(CraftDef src, string oldId, string newId)
        {
            // CraftDef.Copy() is a shallow copy, so rebuild the lists shared with the original.
            CraftDef c = src.Copy();
            bool producesPart = Outputs(src.outputItems, oldId);
            float mult = producesPart ? Plugin.CostMultiplier.Value : 1f;

            c.needItems = CloneNeeds(src.needItems, oldId, newId, mult);
            c.needItemsFromWgo = CloneNeeds(src.needItemsFromWgo, oldId, newId, 1f);
            c.dropFromWgoItemsStart = CloneNeeds(src.dropFromWgoItemsStart, oldId, newId, 1f);
            c.dropFromWgoItemsEnd = CloneNeeds(src.dropFromWgoItemsEnd, oldId, newId, 1f);
            c.removeItemsFromWgo = CloneNeeds(src.removeItemsFromWgo, oldId, newId, 1f);
            c.outputItems = CloneOutputs(src.outputItems, oldId, newId);
            c.addItemsToWgoOnStart = CloneOutputs(src.addItemsToWgoOnStart, oldId, newId);
            c.addItemsToWgoOnFinish = CloneOutputs(src.addItemsToWgoOnFinish, oldId, newId);
            c.craftsIn = src.craftsIn == null ? null : new List<string>(src.craftsIn);

            // Otherwise, the cached "result" would still point to the old item.
            CachedResult(c).SetValue(c, null);
            CachedResults(c).SetValue(c, null);
            return c;
        }

        private static List<NeedItemData> CloneNeeds(List<NeedItemData> list, string oldId, string newId, float mult)
        {
            if (list == null) return null;
            var result = new List<NeedItemData>(list.Count);
            foreach (NeedItemData n in list)
            {
                if (n == null) { result.Add(null); continue; }

                NeedItemData copy = Clone(n);
                NeedItemDefField.SetValue(copy, null); // The game will look it up again by ID.
                if (n.id == oldId) copy.id = newId;
                else if (Math.Abs(mult - 1f) > 0.001f) copy.count = Multiply(n.count, mult);
                result.Add(copy);
            }
            return result;
        }

        private static OutputItems CloneOutputs(OutputItems o, string oldId, string newId)
        {
            if (o == null) return null;
            OutputItems copy = Clone(o);
            copy.chanceOutputItems = o.chanceOutputItems?.Select(x => CloneChance(x, oldId, newId)).ToList();
            copy.groupChanceOutputItems = o.groupChanceOutputItems?.Select(g =>
            {
                if (g == null) return null;
                GroupChanceOutputItem gc = Clone(g);
                gc.chanceItems = g.chanceItems?.Select(x => CloneChance(x, oldId, newId)).ToList();
                return gc;
            }).ToList();
            return copy;
        }

        private static ChanceOutputItem CloneChance(ChanceOutputItem x, string oldId, string newId)
        {
            if (x == null) return null;
            ChanceOutputItem copy = Clone(x);
            if (x.id == oldId) copy.id = newId;
            return copy;
        }

        private static LazyExpression Multiply(LazyExpression count, float mult)
        {
            // Multiply plain numbers only; leave expressions unchanged.
            string expr = count == null ? null : ExpressionStringField.GetValue(count) as string;
            if (expr != null && int.TryParse(expr.Trim(), out int n) && n > 0)
                return new LazyExpression(Math.Max(1, (int)Math.Ceiling(n * mult)).ToString());
            return count;
        }

        // Non-public fields accessed through reflection.
        private static readonly System.Reflection.FieldInfo NeedItemDefField = AccessTools.Field(typeof(NeedItemData), "itemDef");
        private static readonly System.Reflection.FieldInfo ExpressionStringField = AccessTools.Field(typeof(LazyExpressionBase), "expressionString");
        private static System.Reflection.FieldInfo CachedResult(CraftDef c) => AccessTools.Field(typeof(CraftDef), "cachedResultingItemDef");
        private static System.Reflection.FieldInfo CachedResults(CraftDef c) => AccessTools.Field(typeof(CraftDef), "cachedPossibleResultingItemDefs");

        private static readonly System.Reflection.MethodInfo MemberwiseCloneMethod =
            AccessTools.Method(typeof(object), "MemberwiseClone");

        private static T Clone<T>(T obj) where T : class
        {
            return (T)MemberwiseCloneMethod.Invoke(obj, null);
        }

        // ---- unlocks ----

        private static int lastSync;

        public static void SyncUnlocks()
        {
            if (CraftOrigins.Count == 0) return;
            // The game calls this getter frequently; checking about five times per second is sufficient.
            int now = Environment.TickCount;
            if (now - lastSync < 200 && now - lastSync >= 0) return;
            lastSync = now;
            KnowledgeSystem ks = MainGame.Instance?.GameSave?.knowledgeSystem;
            if (ks == null) return;

            foreach (var kv in CraftOrigins)
            {
                Mirror(ks.unlockedCrafts, kv.Value, kv.Key);
                Mirror(ks.blackListCrafts, kv.Value, kv.Key);
            }
        }

        private static void Mirror(List<string> list, string original, string clone)
        {
            if (list == null) return;
            bool has = list.Contains(original);
            if (has && !list.Contains(clone)) list.Add(clone);
            else if (!has && list.Contains(clone)) list.Remove(clone);
        }

        // ---- text ----

        private static readonly Dictionary<string, string> Texts = new Dictionary<string, string>
        {
            { TopId,        "Ornate Marble Cross" },
            { TopId + "_d", "A finely carved marble cross. The best you can put on a grave." },
            { BotId,        "Ornate Marble Fence" },
            { BotId + "_d", "A marble fence with wrought-iron ornaments. The best a grave can be surrounded with." },
        };

        public static bool TryGetText(string key, out string text)
        {
            text = null;
            if (key == null || !Texts.TryGetValue(key, out text)) return false;
            return true;
        }
    }

    // ---- Harmony patches ----

    [HarmonyPatch(typeof(GameBalance), nameof(GameBalance.InitCache))]
    internal static class Patch_GameBalance_InitCache
    {
        // Inject the new items and recipes BEFORE the caches are built.
        private static void Prefix(GameBalance __instance) => GraveContent.Inject(__instance);
    }

    [HarmonyPatch(typeof(CraftComponent), nameof(CraftComponent.AvailableCrafts), MethodType.Getter)]
    internal static class Patch_CraftComponent_AvailableCrafts
    {
        private static void Prefix() => GraveContent.SyncUnlocks();
    }

    [HarmonyPatch(typeof(UICraftPreviewItemCellData), "IsUnknown", MethodType.Getter)]
    internal static class Patch_UICraftPreviewItemCellData_IsUnknown
    {
        private static void Prefix() => GraveContent.SyncUnlocks();
    }

    [HarmonyPatch(typeof(LLBase), nameof(LLBase.L), new[] { typeof(string) })]
    internal static class Patch_LLBase_L
    {
        private static bool Prefix(string lngId, ref string __result)
        {
            if (!GraveContent.TryGetText(lngId, out string text)) return true;
            __result = text;
            return false;
        }
    }

    [HarmonyPatch(typeof(LLBase), "HasL", new[] { typeof(string) })]
    internal static class Patch_LLBase_HasL
    {
        private static bool Prefix(string __0, ref bool __result)
        {
            if (!GraveContent.TryGetText(__0, out _)) return true;
            __result = true;
            return false;
        }
    }
}
