using CommandLine;
using DynamicData.Kernel;
using Loqui;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Aspects;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Noggog;
using OneOf.Types;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Reflection.PortableExecutable;
using System.Security.AccessControl;
using System.Text.Json;
using System.Timers;
using static Mutagen.Bethesda.Skyrim.FaceFxPhonemes;

namespace RoleRimLootOverhaul
{
    public class Program
    {
        public static async Task<int> Main(string[] args)
        {
            return await SynthesisPipeline.Instance
                .AddPatch<ISkyrimMod, ISkyrimModGetter>(RunPatch)
                .SetTypicalOpen(GameRelease.SkyrimSE, "RoleRim - Loot Patch.esp")
                .Run(args);
        }

        // Prefix for leveled list strings
        private const string llPrefix = "rr_Mod_";

        public static void RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            // Check for requirements
            var RoleRimModKey = ModKey.FromFileName("RoleRim - Loot.esp");
            if (!state.LoadOrder.TryGetValue(RoleRimModKey, out var RoleRimListing))
            {
                Console.WriteLine("RoleRim - Loot.esp was not found in the load order.");
                return;
            }
            if (RoleRimListing.Mod is null)
            {
                Console.WriteLine("RoleRim - Loot .esp could not be loaded.");
                return;
            }
            var RoleRimMod = RoleRimListing.Mod;

            // Notify patcher is running
            Console.WriteLine($"RoleRim Loot Patcher is running - Load Order Count: {state.LoadOrder.Count}");
            Console.WriteLine();

            // Exclusion / Inclusion Lists
            HashSet<string> excludedPlugins =
            [
                // BASE GAME PLUGINS
                "Skyrim.esm",
                "Update.esm",
                "Dawnguard.esm",
                "HearthFires.esm",
                "Dragonborn.esm"
            ];
            HashSet<string> excludedMods =
            [
                // PLACE MODS THAT SHOULD BE EXCLUDED HERE
            ];
            HashSet<string> excludedItems =
            [
                // PLACE ITEMS THAT SHOULD BE EXLCUDED NOT CAUGHT BY EXCLUSION CHECKS HERE
            ];

            // Inclusion List
            HashSet<string> includedItems =
            [
                // PLACE MODS THAT DISTRIBUTE ITEMS BY MEANS OTHER THAN LEVELED LISTS HERE
                "Hothtrooper44_ArmorCompilation.esp",
                "Sentinel.esp",
                "Sentinel - Master Plugin.esp",
                "Sentinel - City Guards.esp",
                "Sentinel - Priests and Acolytes.esp",
            ];

            // Build Leveled List Reference List - Only include items distributed by originating mod
            var leveledListReferences = Build_References(state);

            // Distribute by record
            Distribute_ALCH(state, RoleRimMod, excludedPlugins, excludedMods, excludedItems, includedItems, leveledListReferences);
            Distribute_AMMO(state, RoleRimMod, excludedPlugins, excludedMods, excludedItems, includedItems, leveledListReferences);
            Distribute_ARMO(state, RoleRimMod, excludedPlugins, excludedMods, excludedItems, includedItems, leveledListReferences);
            Distribute_BOOK(state, RoleRimMod, excludedPlugins, excludedMods, excludedItems, includedItems, leveledListReferences);
            Distribute_INGR(state, RoleRimMod, excludedPlugins, excludedMods, excludedItems, includedItems, leveledListReferences);
            Distribute_MISC(state, RoleRimMod, excludedPlugins, excludedMods, excludedItems, includedItems, leveledListReferences);
            Distribute_SCRL(state, RoleRimMod, excludedPlugins, excludedMods, excludedItems, includedItems, leveledListReferences);
            Distribute_SLGM(state, RoleRimMod, excludedPlugins, excludedMods, excludedItems, includedItems, leveledListReferences);
            Distribute_WEAP(state, RoleRimMod, excludedPlugins, excludedMods, excludedItems, includedItems, leveledListReferences);
        }

        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        // ALCH: FOOD, POISONS, POTIONS
        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        private static void Distribute_ALCH(
            IPatcherState<ISkyrimMod, ISkyrimModGetter> state,
            ISkyrimModGetter RoleRimMod,
            HashSet<string> excludedPlugins,
            HashSet<string> excludedMods,
            HashSet<string> excludedItems,
            HashSet<string> includedItems,
            Dictionary<FormKey, HashSet<FormKey>> leveledListReferences)
        {
            // Start ALCH Distribution
            Console.WriteLine("=== Alchemical Records ===");

            // Set minimum values for tiers
            int[] tierMins_Foods = [0, 3, 6, 12];
            int[] tierMins_Poisons = [0, 100, 200, 350, 500];
            int[] tierMins_Potions = [0, 150, 300, 525, 750];

            // Set tiers to the higher of the mins or calculated
            int[] tiers_Foods = ApplyMinimumTierBreaks(CalcTierBreak(state.LoadOrder.PriorityOrder.Ingestible().WinningOverrides().Where(ingestible =>
                ingestible.Flags.HasFlag(Ingestible.Flag.FoodItem))
                .Select(ingestible => ingestible.Value), tierMins_Foods.Length), tierMins_Foods);

            int[] tiers_Poisons = ApplyMinimumTierBreaks(CalcTierBreak(state.LoadOrder.PriorityOrder.Ingestible().WinningOverrides().Where(ingestible =>
                ingestible.Flags.HasFlag(Ingestible.Flag.Poison))
                .Select(ingestible => ingestible.Value), tierMins_Poisons.Length), tierMins_Poisons);

            int[] tiers_Potions = ApplyMinimumTierBreaks(CalcTierBreak(state.LoadOrder.PriorityOrder.Ingestible().WinningOverrides().Where(ingestible =>
                !ingestible.Flags.HasFlag(Ingestible.Flag.FoodItem) &&
                !ingestible.Flags.HasFlag(Ingestible.Flag.Poison))
                .Select(ingestible => ingestible.Value), tierMins_Potions.Length), tierMins_Potions);

            // Display Tier Breaks
            Console.WriteLine($"[TIERS] Food (Value): {string.Join(", ", tiers_Foods)}");
            Console.WriteLine($"[TIERS] Poison (Value): {string.Join(", ", tiers_Poisons)}");
            Console.WriteLine($"[TIERS] Potion (Value): {string.Join(", ", tiers_Potions)}");

            // Distribute each item to the appropriate LVLI
            foreach (var ingestible in state.LoadOrder.PriorityOrder.Ingestible().WinningOverrides())
            {

                // Exclusions
                string distribution = Distribution_Check(leveledListReferences, excludedPlugins, excludedMods, excludedItems, includedItems, ingestible);
                if (distribution == "Base")
                { 
                    continue;
                }
                if (distribution != "Distribute")
                {
                    Console.WriteLine($"[ALCH] {ingestible.Name} {ingestible.FormKey} Skipped: {distribution}");
                    continue;
                }

                // Build Leveled List Target String: Type (Food, Poison, Potion)
                string llTarget = llPrefix + "Alch_";
                string alchType = "";

                if (ingestible.Flags.HasFlag(Ingestible.Flag.FoodItem))
                {
                    alchType = "Food";
                }
                else if (ingestible.Flags.HasFlag(Ingestible.Flag.Poison))
                {
                    alchType = "Poison";
                }
                else
                {
                    alchType = "Potion";
                }
                llTarget += $"{alchType}_";

                // Build Leveled List Target String: Poison / Potion (by Effect)
                if (llTarget.Contains("Poison") || llTarget.Contains("Potion")) llTarget += Get_Alchemy_Effect(ingestible, state.LinkCache) + "_";

                // Build Leveled List Target String: Tier
                int tier = 0;

                if (ingestible.Flags.HasFlag(Ingestible.Flag.FoodItem))
                {
                    tier = ingestible.Value switch
                    {
                        _ when ingestible.Value < tiers_Foods[1] => 1,
                        _ when ingestible.Value < tiers_Foods[2] => 2,
                        _ when ingestible.Value < tiers_Foods[3] => 3,
                        _ => 4
                    };
                }
                else if (ingestible.Flags.HasFlag(Ingestible.Flag.Poison))
                {
                    tier = ingestible.Value switch
                    {
                        _ when ingestible.Value < tiers_Poisons[1] => 1,
                        _ when ingestible.Value < tiers_Poisons[2] => 2,
                        _ when ingestible.Value < tiers_Poisons[3] => 3,
                        _ when ingestible.Value < tiers_Poisons[4] => 4,
                        _ => 5
                    };
                }
                else
                {
                    tier = ingestible.Value switch
                    {
                        _ when ingestible.Value < tiers_Potions[1] => 1,
                        _ when ingestible.Value < tiers_Potions[2] => 2,
                        _ when ingestible.Value < tiers_Potions[3] => 3,
                        _ when ingestible.Value < tiers_Potions[4] => 4,
                        _ => 5
                    };
                }
                llTarget += tier;

                // Add to Leveled List
                AddToLeveledList(state, RoleRimMod, ingestible, llTarget);
                Console.WriteLine($"[{alchType.ToUpper()}] {ingestible.Name} {ingestible.EditorID} added to {llTarget}");
            }
            Console.WriteLine("=== End Alchemical Records ===");
            Console.WriteLine();
        }

        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        // AMMO: ARROWS, BOLTS
        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        private static void Distribute_AMMO(
            IPatcherState<ISkyrimMod, ISkyrimModGetter> state,
            ISkyrimModGetter RoleRimMod,
            HashSet<string> excludedPlugins,
            HashSet<string> excludedMods,
            HashSet<string> excludedItems,
            HashSet<string> includedItems,
            Dictionary<FormKey, HashSet<FormKey>> leveledListReferences)
        {
            // Start AMMO Distribution
            Console.WriteLine("=== Ammunition Records ===");

            // Set minimum values for tiers
            int[] tierMins_Arrows = [0, 2, 4, 6, 8];
            int[] tierMins_Bolts = [0, 2, 4, 6, 8];

            // Set tiers to the higher of the mins or calculated
            int[] tiersArrows = ApplyMinimumTierBreaks(
                CalcTierBreak(state.LoadOrder.PriorityOrder.Ammunition().WinningOverrides().Where(ammo =>
                ammo.Flags.HasFlag(Ammunition.Flag.NonBolt))
                .Select(ammo => ammo.Value), tierMins_Arrows.Length), tierMins_Arrows);

            int[] tiersBolts = ApplyMinimumTierBreaks(
                CalcTierBreak(state.LoadOrder.PriorityOrder.Ammunition().WinningOverrides().Where(ammo =>
                !ammo.Flags.HasFlag(Ammunition.Flag.NonBolt))
                .Select(ammo => ammo.Value), tierMins_Bolts.Length), tierMins_Bolts);

            // Display Tier Breaks
            Console.WriteLine($"[TIERS] Arrows (Value): {string.Join(", ", tiersArrows)}");
            Console.WriteLine($"[TIERS] Bolts (Value): {string.Join(", ", tiersBolts)}");

            // Distribute each item to the appropriate LVLI
            foreach (var ammo in state.LoadOrder.PriorityOrder.Ammunition().WinningOverrides())
            {
                // Exclusions
                string distribution = Distribution_Check(leveledListReferences, excludedPlugins, excludedMods, excludedItems, includedItems, ammo);
                if (distribution == "Base")
                {
                    continue;
                }
                if (distribution != "Distribute")
                {
                    Console.WriteLine($"[AMMO] {ammo.Name} {ammo.FormKey} Skipped: {distribution}");
                    continue;
                }
                else if (ammo.MajorFlags.HasFlag(Ammunition.MajorFlag.NonPlayable))
                {
                    Console.WriteLine($"[AMMO] {ammo.Name} {ammo.FormKey} Skipped: Nonplayable");
                    continue;
                }

                // Build Leveled List Target String: Tier (Material >> Value)
                string llTarget = llPrefix + "Ammo_";
                int tier = GetTierByMaterial_Ammo(ammo, state.LinkCache);

                if (tier == 0 && ammo.Flags.HasFlag(Ammunition.Flag.NonBolt))
                {
                    tier = ammo.Value switch
                    {
                        _ when ammo.Value < tiersArrows[1] => 1,
                        _ when ammo.Value < tiersArrows[2] => 2,
                        _ when ammo.Value < tiersArrows[3] => 3,
                        _ when ammo.Value < tiersArrows[4] => 4,
                        _ => 5
                    };
                }
                if (tier == 0 && !ammo.Flags.HasFlag(Ammunition.Flag.NonBolt))
                {
                    tier = ammo.Value switch
                    {
                        _ when ammo.Value < tiersBolts[1] => 1,
                        _ when ammo.Value < tiersBolts[2] => 2,
                        _ when ammo.Value < tiersBolts[3] => 3,
                        _ when ammo.Value < tiersBolts[4] => 4,
                        _ => 5
                    };
                }
                llTarget += tier;

                // Add to Leveled List
                AddToLeveledList(state, RoleRimMod, ammo, llTarget);
                Console.WriteLine($"[AMMO] {ammo.Name} {ammo.EditorID} added to {llTarget}");
            }
            Console.WriteLine("=== End Ammunition Records ===");
            Console.WriteLine();
        }

        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        // ARMO: CLOTHING, ARMOR, JEWELRY
        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        private static void Distribute_ARMO(
            IPatcherState<ISkyrimMod, ISkyrimModGetter> state,
            ISkyrimModGetter RoleRimMod,
            HashSet<string> excludedPlugins,
            HashSet<string> excludedMods,
            HashSet<string> excludedItems,
            HashSet<string> includedItems,
            Dictionary<FormKey, HashSet<FormKey>> leveledListReferences)
        {
            // Start ARMO Distribution
            Console.WriteLine("=== Armor Records ===");

            // Set minimum values for tiers
            int[] tierMins_Clothing = [0, 10, 25, 50, 100];

            int[] tierMins_Hvy_Cuirass   = [0, 225, 350, 1250, 2000];
            int[] tierMins_Hvy_Boots     = [0,  50,  75,  250,  500];
            int[] tierMins_Hvy_Gauntlets = [0,  50,  75,  250,  500];
            int[] tierMins_Hvy_Helmet    = [0,  80, 160,  700, 1500];
            int[] tierMins_Hvy_Shield    = [0,  80, 160,  700, 1500];

            int[] tierMins_Lit_Cuirass   = [0, 60, 200, 800, 1400];
            int[] tierMins_Lit_Boots     = [0, 20,  50, 175,  350];
            int[] tierMins_Lit_Gauntlets = [0, 20,  50, 175,  350];
            int[] tierMins_Lit_Helmet    = [0, 40, 100, 350,  700];
            int[] tierMins_Lit_Shield    = [0, 40, 100, 350,  700];

            int[] tierMins_Circlet  = [0,  75, 175, 300,  500];
            int[] tierMins_Necklace = [0, 100, 350, 700, 1000];
            int[] tierMins_Ring     = [0,  60, 180, 400,  750];

            // Set tiers to the higher of the mins or calculated
            int[] tiersClothing = ApplyMinimumTierBreaks(CalcTierBreak(state.LoadOrder.PriorityOrder.Armor().WinningOverrides().Where(armor =>
                armor.HasKeyword("ArmorClothing", state.LinkCache))
                .Select(armor => armor.Value), tierMins_Clothing.Length), tierMins_Clothing);

            int[] tiersHvyCuirasses = ApplyMinimumTierBreaks(CalcTierBreak(state.LoadOrder.PriorityOrder.Armor().WinningOverrides().Where(armor =>
                armor.HasKeyword("ArmorCuirass", state.LinkCache) &&
                armor.HasKeyword("ArmorHeavy", state.LinkCache))
                .Select(armor => armor.Value), tierMins_Hvy_Cuirass.Length), tierMins_Hvy_Cuirass);

            int[] tiersHvyBoots = ApplyMinimumTierBreaks(CalcTierBreak(state.LoadOrder.PriorityOrder.Armor().WinningOverrides().Where(armor =>
                armor.HasKeyword("ArmorBoots", state.LinkCache) &&
                armor.HasKeyword("ArmorHeavy", state.LinkCache))
                .Select(armor => armor.Value), tierMins_Hvy_Boots.Length), tierMins_Hvy_Boots);

            int[] tiersHvyGauntlets = ApplyMinimumTierBreaks(CalcTierBreak(state.LoadOrder.PriorityOrder.Armor().WinningOverrides().Where(armor =>
                armor.HasKeyword("ArmorGauntlets", state.LinkCache) &&
                armor.HasKeyword("ArmorHeavy", state.LinkCache))
                .Select(armor => armor.Value), tierMins_Hvy_Gauntlets.Length), tierMins_Hvy_Gauntlets);

            int[] tiersHvyHelmets = ApplyMinimumTierBreaks(CalcTierBreak(state.LoadOrder.PriorityOrder.Armor().WinningOverrides().Where(armor =>
                armor.HasKeyword("ArmorHelmet", state.LinkCache) &&
                armor.HasKeyword("ArmorHeavy", state.LinkCache))
                .Select(armor => armor.Value), tierMins_Hvy_Helmet.Length), tierMins_Hvy_Helmet);

            int[] tiersHvyShields = ApplyMinimumTierBreaks(CalcTierBreak(state.LoadOrder.PriorityOrder.Armor().WinningOverrides().Where(armor =>
                armor.HasKeyword("ArmorShield", state.LinkCache) &&
                armor.HasKeyword("ArmorHeavy", state.LinkCache))
                .Select(armor => armor.Value), tierMins_Hvy_Shield.Length), tierMins_Hvy_Shield);

            int[] tiersLitCuirasses = ApplyMinimumTierBreaks(CalcTierBreak(state.LoadOrder.PriorityOrder.Armor().WinningOverrides().Where(armor =>
                armor.HasKeyword("ArmorCuirass", state.LinkCache) &&
                armor.HasKeyword("ArmorLight", state.LinkCache))
                .Select(armor => armor.Value), tierMins_Lit_Cuirass.Length), tierMins_Lit_Cuirass);

            int[] tiersLitBoots = ApplyMinimumTierBreaks(CalcTierBreak(state.LoadOrder.PriorityOrder.Armor().WinningOverrides().Where(armor =>
                armor.HasKeyword("ArmorBoots", state.LinkCache) &&
                armor.HasKeyword("ArmorLight", state.LinkCache))
                .Select(armor => armor.Value), tierMins_Lit_Boots.Length), tierMins_Lit_Boots);

            int[] tiersLitGauntlets = ApplyMinimumTierBreaks(CalcTierBreak(state.LoadOrder.PriorityOrder.Armor().WinningOverrides().Where(armor =>
                armor.HasKeyword("ArmorGauntlets", state.LinkCache) &&
                armor.HasKeyword("ArmorLight", state.LinkCache))
                .Select(armor => armor.Value), tierMins_Lit_Gauntlets.Length), tierMins_Lit_Gauntlets);

            int[] tiersLitHelmets = ApplyMinimumTierBreaks(CalcTierBreak(state.LoadOrder.PriorityOrder.Armor().WinningOverrides().Where(armor =>
                armor.HasKeyword("ArmorHelmet", state.LinkCache) &&
                armor.HasKeyword("ArmorLight", state.LinkCache))
                .Select(armor => armor.Value), tierMins_Lit_Helmet.Length), tierMins_Lit_Helmet);

            int[] tiersLitShields = ApplyMinimumTierBreaks(CalcTierBreak(state.LoadOrder.PriorityOrder.Armor().WinningOverrides().Where(armor =>
                armor.HasKeyword("ArmorShield", state.LinkCache) &&
                armor.HasKeyword("ArmorLight", state.LinkCache))
                .Select(armor => armor.Value), tierMins_Lit_Shield.Length), tierMins_Lit_Shield);

            int[] tiersCirclets = ApplyMinimumTierBreaks(CalcTierBreak(state.LoadOrder.PriorityOrder.Armor().WinningOverrides().Where(armor =>
                armor.HasKeyword("ArmorJewelry", state.LinkCache) &&
                armor.HasKeyword("ClothingCirclet", state.LinkCache))
                .Select(armor => armor.Value), tierMins_Circlet.Length), tierMins_Circlet);

            int[] tiersNecklaces = ApplyMinimumTierBreaks(CalcTierBreak(state.LoadOrder.PriorityOrder.Armor().WinningOverrides().Where(armor =>
                armor.HasKeyword("ArmorJewelry", state.LinkCache) &&
                armor.HasKeyword("ClothingNecklace", state.LinkCache))
                .Select(armor => armor.Value), tierMins_Necklace.Length), tierMins_Necklace);

            int[] tiersRings = ApplyMinimumTierBreaks(CalcTierBreak(state.LoadOrder.PriorityOrder.Armor().WinningOverrides().Where(armor =>
                armor.HasKeyword("ArmorJewelry", state.LinkCache) &&
                armor.HasKeyword("ClothingRing", state.LinkCache))
                .Select(armor => armor.Value), tierMins_Ring.Length), tierMins_Ring);

            // Display Tier Breaks
            Console.WriteLine($"[TIERS] Clothing (Value): {string.Join(", ", tiersClothing)}");
            Console.WriteLine($"[TIERS] Heavy Cuirasses (Value): {string.Join(", ", tiersHvyCuirasses)}");
            Console.WriteLine($"[TIERS] Heavy Boots (Value): {string.Join(", ", tiersHvyBoots)}");
            Console.WriteLine($"[TIERS] Heavy Gauntlets (Value): {string.Join(", ", tiersHvyGauntlets)}");
            Console.WriteLine($"[TIERS] Heavy Helmets (Value): {string.Join(", ", tiersHvyHelmets)}");
            Console.WriteLine($"[TIERS] Heavy Shields (Value): {string.Join(", ", tiersHvyShields)}");
            Console.WriteLine($"[TIERS] Light Cuirasses (Value): {string.Join(", ", tiersLitCuirasses)}");
            Console.WriteLine($"[TIERS] Light Boots (Value): {string.Join(", ", tiersLitBoots)}");
            Console.WriteLine($"[TIERS] Light Gauntlets (Value): {string.Join(", ", tiersLitGauntlets)}");
            Console.WriteLine($"[TIERS] Light Helmets (Value): {string.Join(", ", tiersLitHelmets)}");
            Console.WriteLine($"[TIERS] Light Shields (Value): {string.Join(", ", tiersLitShields)}");
            Console.WriteLine($"[TIERS] Circlets (Value): {string.Join(", ", tiersCirclets)}");
            Console.WriteLine($"[TIERS] Necklaces (Value): {string.Join(", ", tiersNecklaces)}");
            Console.WriteLine($"[TIERS] Rings (Value): {string.Join(", ", tiersRings)}");

            // Distribute each item to the appropriate LVLI
            foreach (var armor in state.LoadOrder.PriorityOrder.Armor().WinningOverrides())
            {
                // Exclusions
                string distribution = Distribution_Check(leveledListReferences, excludedPlugins, excludedMods, excludedItems, includedItems, armor);
                if (distribution == "Base")
                {
                    continue;
                }
                if (distribution != "Distribute")
                {
                    Console.WriteLine($"[ARMO] {armor.Name} {armor.FormKey} Skipped: {distribution}");
                    continue;
                }
                else if (armor.MajorFlags.HasFlag(Armor.MajorFlag.NonPlayable))
                {
                    Console.WriteLine($"[ARMO] {armor.Name} {armor.FormKey} Skipped: Nonplayable");
                    continue;
                }

                // Build Leveled List Target String: Enchanted or Non-Enchanted
                string llTarget = llPrefix + "Armo_";
                string armorPiece = GetArmorPiece(armor, state.LinkCache);
                string armorEnc = "";
                if (armor.ObjectEffect.FormKey != FormKey.Null && (
                    armorPiece == "Cuirass" ||
                    armorPiece == "Boots" ||
                    armorPiece == "Gauntlets" ||
                    armorPiece == "Shield"))
                    armorEnc = "Enc";
                llTarget += $"{armorEnc}_";

                // Build Leveled List Target String: Armor Type
                string armorType = GetArmorType(armor, state.LinkCache);
                llTarget += $"{armorType}_";

                // Build Leveled List Target String: Armor Piece
                if (armorPiece == "UNKNOWN")
                {
                    Console.WriteLine($"[ARMO] {armor.Name} {armor.EditorID} Skipped: Unable to resolve armor type/piece");
                    continue;
                }
                llTarget += $"{armorPiece}_";

                // Build Leveled List Target String: Tier (Material >> Value)
                int tier = GetTierByMaterial_Armor(armor, state.LinkCache);
                if (tier > 0)
                {
                    llTarget += tier;
                }
                else
                {
                    int[] tierBreaks;

                    if (armorPiece == "Clothing")
                    {
                        tierBreaks = tiersClothing;
                    }
                    else if (armorPiece == "Circlet")
                    {
                        tierBreaks = tiersCirclets;
                    }
                    else if (armorPiece == "Necklace")
                    {
                        tierBreaks = tiersNecklaces;
                    }
                    else if (armorPiece == "Ring")
                    {
                        tierBreaks = tiersRings;
                    }
                    else if (armorType == "Hvy" && armorPiece == "Cuirass")
                    {
                        tierBreaks = tiersHvyCuirasses;
                    }
                    else if (armorType == "Hvy" && armorPiece == "Boots")
                    {
                        tierBreaks = tiersHvyBoots;
                    }
                    else if (armorType == "Hvy" && armorPiece == "Gauntlets")
                    {
                        tierBreaks = tiersHvyGauntlets;
                    }
                    else if (armorType == "Hvy" && armorPiece == "Helmet")
                    {
                        tierBreaks = tiersHvyHelmets;
                    }
                    else if (armorType == "Hvy" && armorPiece == "Shield")
                    {
                        tierBreaks = tiersHvyShields;
                    }
                    else if (armorType == "Lit" && armorPiece == "Cuirass")
                    {
                        tierBreaks = tiersLitCuirasses;
                    }
                    else if (armorType == "Lit" && armorPiece == "Boots")
                    {
                        tierBreaks = tiersLitBoots;
                    }
                    else if (armorType == "Lit" && armorPiece == "Gauntlets")
                    {
                        tierBreaks = tiersLitGauntlets;
                    }
                    else if (armorType == "Lit" && armorPiece == "Helmet")
                    {
                        tierBreaks = tiersLitHelmets;
                    }
                    else if (armorType == "Lit" && armorPiece == "Shield")
                    {
                        tierBreaks = tiersLitShields;
                    }
                    else
                    {
                        Console.WriteLine($"[ARMO] {armor.Name} {armor.EditorID} Skipped: Unable to resolve armor item");
                        continue;
                    }
                    tier = armor.Value switch
                    {
                        _ when armor.Value < tierBreaks[1] => 1,
                        _ when armor.Value < tierBreaks[2] => 2,
                        _ when armor.Value < tierBreaks[3] => 3,
                        _ when armor.Value < tierBreaks[4] => 4,
                        _ => 5
                    };
                }
                llTarget += tier;

                // Add to Leveled List
                AddToLeveledList(state, RoleRimMod, armor, llTarget);
                Console.WriteLine($"[ARMO] {armor.Name} {armor.EditorID} added to {llTarget}");
            }
            Console.WriteLine("=== End Armor Records ===");
            Console.WriteLine();
        }

        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        // BOOK: BOOKS, SPELL TOMES, SKILL BOOKS
        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        private static void Distribute_BOOK(
            IPatcherState<ISkyrimMod, ISkyrimModGetter> state,
            ISkyrimModGetter RoleRimMod,
            HashSet<string> excludedPlugins,
            HashSet<string> excludedMods,
            HashSet<string> excludedItems,
            HashSet<string> includedItems,
            Dictionary<FormKey, HashSet<FormKey>> leveledListReferences)
        {
            // Start BOOK Distribution
            Console.WriteLine("=== Book Records ===");

            // Set minimum values for tiers
            int[] tierMins_Books = [0, 5, 10, 20, 30];
            int[] tierMins_Tomes = [0, 100, 300, 600, 1000];
            int[] tierMins_SkillBooks = [0];

            // Set tiers to the higher of the mins or calculated

            int[] tiersBooks = ApplyMinimumTierBreaks(CalcTierBreak(state.LoadOrder.PriorityOrder.Book().WinningOverrides().Where(book =>
                    book.Teaches is null)
                    .Select(book => book.Value), tierMins_Books.Length), tierMins_Books);

            int[] tiersTomes = ApplyMinimumTierBreaks(CalcTierBreak(state.LoadOrder.PriorityOrder.Book().WinningOverrides().Where(book =>
                    book.Teaches is BookSpell)
                    .Select(book => book.Value), tierMins_Tomes.Length), tierMins_Tomes);

            int[] tiersSkillBooks = ApplyMinimumTierBreaks(CalcTierBreak(state.LoadOrder.PriorityOrder.Book().WinningOverrides().Where(book =>
                    book.Teaches is BookSkill)
                    .Select(book => book.Value), tierMins_SkillBooks.Length), tierMins_SkillBooks);

            // Display Tier Breaks
            Console.WriteLine($"[TIERS] Books (Value): {string.Join(", ", tiersBooks)}");
            Console.WriteLine($"[TIERS] Tomes (Value): {string.Join(", ", tiersTomes)}");
            Console.WriteLine($"[TIERS] Skill Books (Value): {string.Join(", ", tiersSkillBooks)}");

            // Distribute each item to the appropriate LVLI
            foreach (var book in state.LoadOrder.PriorityOrder.Book().WinningOverrides())
            {
                // Exclusions
                string distribution = Distribution_Check(leveledListReferences, excludedPlugins, excludedMods, excludedItems, includedItems, book);
                if (distribution == "Base")
                {
                    continue;
                }
                if (distribution != "Distribute")
                {
                    Console.WriteLine($"[BOOK] {book.Name} {book.FormKey} Skipped: {distribution}");
                    continue;
                }
                else if (book.EditorID?.Contains("Bounty") == true
                    || book.EditorID?.Contains("Journal") == true
                    || book.EditorID?.Contains("Letter") == true
                    || book.EditorID?.Contains("Note") == true
                    || book.Name?.String?.Contains("Bounty") == true
                    || book.Name?.String?.Contains("Journal") == true
                    || book.Name?.String?.Contains("Letter") == true
                    || book.Name?.String?.Contains("Note") == true)
                {
                    Console.WriteLine($"[BOOK] {book.Name} {book.FormKey} Skipped: Special Item");
                    continue;
                }

                // Build Leveled List Target String: Book + Type (Tome, Skill, Regular) + Magic School (Tome)
                string llTarget = llPrefix + "Book_";

                // Build Leveled List Target String: Skill Book
                if (book.Teaches is BookSkill)
                {
                    llTarget += "Skill";
                }

                // Build Leveled List Target String: Tome + Type (Magic School) + Tier (Spell Level)
                else if (book.Teaches is BookSpell bookSpell)
                {
                    llTarget += "Tome_";

                    // Determine magic school and spell level
                    var spell = bookSpell.Spell.TryResolve(state.LinkCache);
                    if (spell is null)
                    {
                        Console.WriteLine($"[BOOK] {book.Name} {book.EditorID} Skipped: Unable to resolve Spell");
                        continue;
                    }
                    uint spellLevel = 0;
                    ActorValue magicSkill = ActorValue.None;

                    foreach (var effect in spell.Effects)
                    {
                        var magEff = effect.BaseEffect.TryResolve(state.LinkCache);
                        if (magEff?.MinimumSkillLevel > spellLevel)
                        {
                            spellLevel = (magEff.MinimumSkillLevel / 25) + 1;
                            magicSkill = magEff.MagicSkill;
                        }
                    }
                    string magicSchool = magicSkill switch
                    {
                        ActorValue.Alteration => "Alt_",
                        ActorValue.Conjuration => "Con_",
                        ActorValue.Destruction => "Des",
                        ActorValue.Illusion => "Ill",
                        ActorValue.Restoration => "Res",
                        _ => ""
                    };
                    llTarget += $"{magicSchool}_{spellLevel}";
                }

                // Build Leveled List Target String: Regular Book + Tier (Value)
                else
                {
                    int tier = 0;
                    tier = book.Value switch
                    {
                        _ when book.Value < tiersBooks[1] => 1,
                        _ when book.Value < tiersBooks[2] => 2,
                        _ when book.Value < tiersBooks[3] => 3,
                        _ when book.Value < tiersBooks[4] => 4,
                        _ => 5
                    };
                    llTarget += tier;
                }

                // Add to Leveled List
                AddToLeveledList(state, RoleRimMod, book, llTarget);
                Console.WriteLine($"[BOOK] {book.Name} {book.EditorID} added to {llTarget}");
            }
            Console.WriteLine("=== End Book Records ===");
            Console.WriteLine();
        }

        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        // INGR: INGREDIENTS
        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        private static void Distribute_INGR(
            IPatcherState<ISkyrimMod, ISkyrimModGetter> state,
            ISkyrimModGetter RoleRimMod,
            HashSet<string> excludedPlugins,
            HashSet<string> excludedMods,
            HashSet<string> excludedItems,
            HashSet<string> includedItems,
            Dictionary<FormKey, HashSet<FormKey>> leveledListReferences)
        {
            // Start INGR Distribution
            Console.WriteLine("=== Ingredient Records ===");

            // Set minimum values for tiers
            int[] tierMins_Ingredients = [0, 10, 20];

            // Set tiers to the higher of the mins or calculated
            int[] tiers_Ingredients = ApplyMinimumTierBreaks(CalcTierBreak(state.LoadOrder.PriorityOrder.Ingredient().WinningOverrides()
                    .Select(ingredient => ingredient.Value), tierMins_Ingredients.Length), tierMins_Ingredients);

            // Display Tier Breaks
            Console.WriteLine($"[TIERS] Ingredients (Value): {string.Join(", ", tiers_Ingredients)}");

            // Distribute each item to the appropriate LVLI
            foreach (var ingredient in state.LoadOrder.PriorityOrder.Ingredient().WinningOverrides())
            {
                // Exclusions
                string distribution = Distribution_Check(leveledListReferences, excludedPlugins, excludedMods, excludedItems, includedItems, ingredient);
                if (distribution == "Base")
                {
                    continue;
                }
                if (distribution != "Distribute")
                {
                    Console.WriteLine($"[INGR] {ingredient.Name} {ingredient.FormKey} Skipped: {distribution}");
                    continue;
                }

                // Build Leveled List Target String: Tier (Value)
                string llTarget = llPrefix + "Ingr_";
                int tier;
                tier = ingredient.Value switch
                {
                    _ when ingredient.Value < tiers_Ingredients[1] => 1,
                    _ when ingredient.Value < tiers_Ingredients[2] => 2,
                    _ => 3
                };
                llTarget += tier;

                // Add to Leveled List
                AddToLeveledList(state, RoleRimMod, ingredient, llTarget);
                Console.WriteLine($"[INGR] {ingredient.Name} {ingredient.EditorID} added to {llTarget}");
            }

            Console.WriteLine("=== End Ingredient Records ===");
            Console.WriteLine();
        }

        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        // MISC: MISC ITEMS (ANIMAL PARTS, CLUTTER, GEMS, INGOTS, ORES
        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        private static void Distribute_MISC(
            IPatcherState<ISkyrimMod, ISkyrimModGetter> state,
            ISkyrimModGetter RoleRimMod,
            HashSet<string> excludedPlugins,
            HashSet<string> excludedMods,
            HashSet<string> excludedItems,
            HashSet<string> includedItems,
            Dictionary<FormKey, HashSet<FormKey>> leveledListReferences)
        {
            // Start MISC Distribution
            Console.WriteLine("=== Misc Item Records ===");

            // Set minimum values for tiers
            int[] tierMins_AnimalParts = [0, 25, 50, 100, 250];
            int[] tierMins_Clutter = [0, 6, 12, 18, 25];
            int[] tierMins_Gems = [0, 150, 350, 500, 750];
            int[] tierMins_Ingots = [0, 20, 40, 60, 100];
            int[] tierMins_Ores = [0, 20, 25, 30, 50];

            // Set tiers to the higher of the mins or calculated
            int[] tiers_AnimalParts = ApplyMinimumTierBreaks(CalcTierBreak(
                state.LoadOrder.PriorityOrder.MiscItem().WinningOverrides().Where(miscItem => GetMiscItemGroup(miscItem, state.LinkCache) == "Animal")
                    .Select(miscItem => miscItem.Value), tierMins_AnimalParts.Length), tierMins_AnimalParts);

            int[] tiers_Clutter = ApplyMinimumTierBreaks(CalcTierBreak(
                state.LoadOrder.PriorityOrder.MiscItem().WinningOverrides().Where(miscItem => GetMiscItemGroup(miscItem, state.LinkCache) == "Clutter")
                    .Select(miscItem => miscItem.Value), tierMins_Clutter.Length), tierMins_Clutter);

            int[] tiers_Gems = ApplyMinimumTierBreaks(CalcTierBreak(
                state.LoadOrder.PriorityOrder.MiscItem().WinningOverrides().Where(miscItem => GetMiscItemGroup(miscItem, state.LinkCache) == "Gem")
                    .Select(miscItem => miscItem.Value), tierMins_Gems.Length), tierMins_Gems);

            int[] tiers_Ingots = ApplyMinimumTierBreaks(CalcTierBreak(
                state.LoadOrder.PriorityOrder.MiscItem().WinningOverrides().Where(miscItem => GetMiscItemGroup(miscItem, state.LinkCache) == "Ingot")
                    .Select(miscItem => miscItem.Value), tierMins_Ingots.Length), tierMins_Ingots);

            int[] tiers_Ores = ApplyMinimumTierBreaks(CalcTierBreak(
                state.LoadOrder.PriorityOrder.MiscItem().WinningOverrides().Where(miscItem => GetMiscItemGroup(miscItem, state.LinkCache) == "Ore")
                    .Select(miscItem => miscItem.Value), tierMins_Ores.Length), tierMins_Ores);

            // Display Tier Breaks
            Console.WriteLine($"[TIERS] Animal Parts (Value): {string.Join(", ", tiers_AnimalParts)}");
            Console.WriteLine($"[TIERS] Clutter (Value): {string.Join(", ", tiers_Clutter)}");
            Console.WriteLine($"[TIERS] Gems (Value): {string.Join(", ", tiers_Gems)}");
            Console.WriteLine($"[TIERS] Ingots (Value): {string.Join(", ", tiers_Ingots)}");
            Console.WriteLine($"[TIERS] Ores (Value): {string.Join(", ", tiers_Ores)}");

            // Distribute each item to the appropriate LVLI
            foreach (var miscItem in state.LoadOrder.PriorityOrder.MiscItem().WinningOverrides())
            {
                // Exclusions
                string distribution = Distribution_Check(leveledListReferences, excludedPlugins, excludedMods, excludedItems, includedItems, miscItem);
                if (distribution == "Base")
                {
                    continue;
                }
                if (distribution != "Distribute")
                {
                    Console.WriteLine($"[MISC] {miscItem.Name} {miscItem.FormKey} Skipped: {distribution}");
                    continue;
                }
                else if (miscItem.MajorFlags.HasFlag(MiscItem.MajorFlag.NonPlayable))
                {
                    Console.WriteLine($"[MISC] {miscItem.Name} {miscItem.FormKey} Skipped: Nonplayable");
                    continue;
                }

                // Build Leveled List Target String: Animal Part, Clutter, Gem, Ingot, Ore
                string llTarget = llPrefix + "Misc_";
                string miscType = "";

                if (miscItem.HasKeyword("VendorItemAnimalHide", state.LinkCache) ||
                    miscItem.HasKeyword("VendorItemAnimalPart", state.LinkCache))
                {
                    miscType = "Animal";
                }
                else if (miscItem.HasKeyword("VendorItemClutter", state.LinkCache))
                {
                    miscType = "Clutter";
                }
                else if (miscItem.HasKeyword("VendorItemGem", state.LinkCache))
                {
                    miscType = "Gem";
                }
                else if (miscItem.HasKeyword("VendorItemOreIngot", state.LinkCache))
                {
                    if (miscItem.EditorID?.Contains("Ingot") == true)
                    {
                        miscType = "Ingot";
                    }
                    else if (miscItem.EditorID?.Contains("Ore") == true)
                    {
                        miscType = "Ore";
                    }
                    else
                    {
                        Console.WriteLine($"[MISC] {miscItem.Name} {miscItem.EditorID} Skipped: Unable to resolve Type");
                        continue;
                    }
                }
                else
                {
                    Console.WriteLine($"[MISC] {miscItem.Name} {miscItem.EditorID} Skipped: Unable to resolve Type");
                    continue;
                }
                llTarget += $"_{miscType}";

                // Build Leveled List Target String: Tier (Value)
                int tier = 0;
                int[] tierBreaks;

                if (miscType == "Animal")
                {
                    tierBreaks = tiers_AnimalParts;
                }
                else if (miscType == "Clutter")
                {
                    tierBreaks = tiers_Clutter;
                }
                else if (miscType == "Gem")
                {
                    tierBreaks = tiers_Gems;
                }
                else if (miscType == "Ingot")
                {
                    tierBreaks = tiers_Ingots;
                }
                else if (miscType == "Ore")
                {
                    tierBreaks = tiers_Ores;
                }
                else
                {
                    Console.WriteLine($"[MISC] {miscItem.Name} {miscItem.EditorID} Skipped: Unable to resolve Type");
                    continue;
                }
                tier = miscItem.Value switch
                {
                    _ when miscItem.Value < tierBreaks[1] => 1,
                    _ when miscItem.Value < tierBreaks[2] => 2,
                    _ when miscItem.Value < tierBreaks[3] => 3,
                    _ when miscItem.Value < tierBreaks[4] => 4,
                    _ => 5
                };
                llTarget += tier;

                // Add to Leveled List
                AddToLeveledList(state, RoleRimMod, miscItem, llTarget);
                Console.WriteLine($"[MISC] {miscItem.Name} {miscItem.EditorID} added to {llTarget}");
            }
            Console.WriteLine("=== End MiscItem Records ===");
            Console.WriteLine();
        }

        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        // SCRL: SCROLLS
        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        private static void Distribute_SCRL(
            IPatcherState<ISkyrimMod, ISkyrimModGetter> state,
            ISkyrimModGetter RoleRimMod,
            HashSet<string> excludedPlugins,
            HashSet<string> excludedMods,
            HashSet<string> excludedItems,
            HashSet<string> includedItems,
            Dictionary<FormKey, HashSet<FormKey>> leveledListReferences)
        {
            // Start SCRL Distribution
            Console.WriteLine("=== Scroll Records ===");

            // Set minimum values for tiers // TIERED BY SPELL LEVEL ONLY
            //int[] tierMins_Scrolls = [0, 50, 100, 250, 500];

            // Set tiers to the higher of the mins or calculated // TIERED BY SPELL LEVEL ONLY
            //int[] tiers_Scrolls = ApplyMinimumTierBreaks(CalcTierBreak(state.LoadOrder.PriorityOrder.Scroll().WinningOverrides()
            //        .Select(scroll => scroll.Value), tierMins_Scrolls.Length), tierMins_Scrolls);

            // Display Tier Breaks // TIERED BY SPELL LEVEL ONLY
            //Console.WriteLine($"[TIERS] Scrolls (Value): {string.Join(", ", tiers_Scrolls)}");

            // Distribute each item to the appropriate LVLI
            foreach (var scroll in state.LoadOrder.PriorityOrder.Scroll().WinningOverrides())
            {
                // Exclusions
                string distribution = Distribution_Check(leveledListReferences, excludedPlugins, excludedMods, excludedItems, includedItems, scroll);
                if (distribution == "Base")
                {
                    continue;
                }
                if (distribution != "Distribute")
                {
                    Console.WriteLine($"[SCRL] {scroll.Name} {scroll.FormKey} Skipped: {distribution}");
                    continue;
                }

                // Build Leveled List Target String: Type (Magic School) + Tier (Spell Level)
                string llTarget = "Scrl_";
                uint spellLevel = 0;
                ActorValue magicSkill = ActorValue.None;
                foreach (var effect in scroll.Effects)
                {
                    var magEff = effect.BaseEffect.TryResolve(state.LinkCache);
                    if (magEff?.MinimumSkillLevel > spellLevel)
                    {
                        spellLevel = (magEff.MinimumSkillLevel / 25) + 1;
                        magicSkill = magEff.MagicSkill;
                    }
                }
                string spellSchool = magicSkill switch
                {
                    ActorValue.Alteration => "Alt_",
                    ActorValue.Conjuration => "Con_",
                    ActorValue.Destruction => "Des",
                    ActorValue.Illusion => "Ill",
                    ActorValue.Restoration => "Res",
                    _ => ""
                };
                llTarget += $"{spellSchool}_{spellLevel}";

                // Add to Leveled List
                AddToLeveledList(state, RoleRimMod, scroll, llTarget);
                Console.WriteLine($"[SCRL] {scroll.Name} {scroll.EditorID} added to {llTarget}");
            }
            Console.WriteLine("=== End Scroll Records ===");
            Console.WriteLine();
        }

        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        // SLGM: SOUL GEMS
        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        private static void Distribute_SLGM(
            IPatcherState<ISkyrimMod, ISkyrimModGetter> state,
            ISkyrimModGetter RoleRimMod,
            HashSet<string> excludedPlugins,
            HashSet<string> excludedMods,
            HashSet<string> excludedItems,
            HashSet<string> includedItems,
            Dictionary<FormKey, HashSet<FormKey>> leveledListReferences)
        {
            // Start SLGM Distribution
            Console.WriteLine("=== Soul Gem Records ===");

            // Set minimum values for tiers
            int[] tierMins_SoulGems = [0, 25, 50, 100, 200];

            // Set tiers to the higher of the mins or calculated
            int[] tiers_SoulGems = ApplyMinimumTierBreaks(CalcTierBreak(state.LoadOrder.PriorityOrder.SoulGem().WinningOverrides()
                    .Select(soulGems => soulGems.Value), tierMins_SoulGems.Length), tierMins_SoulGems);

            // Display Tier Breaks
            Console.WriteLine($"[TIERS] Soul Gems (Value): {string.Join(", ", tiers_SoulGems)}");

            // Distribute each item to the appropriate LVLI
            foreach (var soulGem in state.LoadOrder.PriorityOrder.SoulGem().WinningOverrides())
            {
                // Exclusions
                string distribution = Distribution_Check(leveledListReferences, excludedPlugins, excludedMods, excludedItems, includedItems, soulGem);
                if (distribution == "Base")
                {
                    continue;
                }
                if (distribution != "Distribute")
                {
                    Console.WriteLine($"[SLGM] {soulGem.Name} {soulGem.FormKey} Skipped: {distribution}");
                    continue;
                }

                // Build Leveled List Target String: Tier (Size + Fill)
                string llTarget = llPrefix + "Slgm_";
                int tier = soulGem.MaximumCapacity switch
                {
                    SoulGem.Level.Petty => 1,
                    SoulGem.Level.Lesser => 2,
                    SoulGem.Level.Common => 3,
                    SoulGem.Level.Greater => 4,
                    SoulGem.Level.Grand => 5,
                    _ => 0
                };
                
                // Increment tier by 1 if the soul gem has a stored soul; capped at 5
                if (soulGem.ContainedSoul != SoulGem.Level.None) tier++;
                if (tier > 5) tier = 5;
                llTarget += tier;

                // Add to Leveled List
                AddToLeveledList(state, RoleRimMod, soulGem, llTarget);
                Console.WriteLine($"[SLGM] {soulGem.Name} {soulGem.EditorID} added to {llTarget}");
            }
            Console.WriteLine("=== End Soul Gem Records ===");
            Console.WriteLine();
        }

        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        // WEAP: WEAPONS
        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        private static void Distribute_WEAP(
            IPatcherState<ISkyrimMod, ISkyrimModGetter> state,
            ISkyrimModGetter RoleRimMod,
            HashSet<string> excludedPlugins,
            HashSet<string> excludedMods,
            HashSet<string> excludedItems,
            HashSet<string> includedItems,
            Dictionary<FormKey, HashSet<FormKey>> leveledListReferences)
        {
            // Start WEAP Distribution
            Console.WriteLine("=== Weapon Records ===");

            // Set minimum values for tiers
            int[] tierMins_Bows = [0, 100, 250, 800, 1400];

            int[] tierMins_Daggers = [0, 50, 100, 275, 500];
            int[] tierMins_Swords = [0, 75, 225, 725, 1250];
            int[] tierMins_Maces = [0, 100, 325, 1000, 1750];
            int[] tierMins_Waraxes = [0, 85, 300, 850, 1500];

            int[] tierMins_GreatSwords = [0, 85, 275, 800, 2500];
            int[] tierMins_Warhammers = [0, 100, 325, 975, 1725];
            int[] tierMins_Battleaxes = [0, 125, 300, 900, 1575];

            int[] tierMins_Staffs = [0, 250, 500, 1000, 2000];

            // Set tiers to the higher of the mins or calculated
            int[] tiersBows = ApplyMinimumTierBreaks(CalcTierBreak(
                state.LoadOrder.PriorityOrder.Weapon().WinningOverrides().Where(weapon => weapon.HasKeyword("WeapTypeBow", state.LinkCache))
                    .Select(weapon => weapon.BasicStats?.Value ?? 0), tierMins_Bows.Length), tierMins_Bows);

            int[] tiersDaggers = ApplyMinimumTierBreaks(CalcTierBreak(
                state.LoadOrder.PriorityOrder.Weapon().WinningOverrides().Where(weapon => weapon.HasKeyword("WeapTypeDagger", state.LinkCache))
                    .Select(weapon => weapon.BasicStats?.Value ?? 0), tierMins_Daggers.Length), tierMins_Daggers);

            int[] tiersSwords = ApplyMinimumTierBreaks(CalcTierBreak(
                state.LoadOrder.PriorityOrder.Weapon().WinningOverrides().Where(weapon => weapon.HasKeyword("WeapTypeSword", state.LinkCache))
                    .Select(weapon => weapon.BasicStats?.Value ?? 0), tierMins_Swords.Length), tierMins_Swords);

            int[] tiersMaces = ApplyMinimumTierBreaks(CalcTierBreak(
                state.LoadOrder.PriorityOrder.Weapon().WinningOverrides().Where(weapon => weapon.HasKeyword("WeapTypeMace", state.LinkCache))
                    .Select(weapon => weapon.BasicStats?.Value ?? 0), tierMins_Maces.Length), tierMins_Maces);

            int[] tiersWaraxes = ApplyMinimumTierBreaks(CalcTierBreak(
                state.LoadOrder.PriorityOrder.Weapon().WinningOverrides().Where(weapon => weapon.HasKeyword("WeapTypeWaraxe", state.LinkCache))
                    .Select(weapon => weapon.BasicStats?.Value ?? 0), tierMins_Waraxes.Length), tierMins_Waraxes);

            int[] tiersGreatswords = ApplyMinimumTierBreaks(CalcTierBreak(
                state.LoadOrder.PriorityOrder.Weapon().WinningOverrides().Where(weapon => weapon.HasKeyword("WeapTypeGreatsword", state.LinkCache))
                    .Select(weapon => weapon.BasicStats?.Value ?? 0), tierMins_GreatSwords.Length), tierMins_GreatSwords);

            int[] tiersWarhammers = ApplyMinimumTierBreaks(CalcTierBreak(
                state.LoadOrder.PriorityOrder.Weapon().WinningOverrides().Where(weapon => weapon.HasKeyword("WeapTypeWarhammer", state.LinkCache))
                    .Select(weapon => weapon.BasicStats?.Value ?? 0), tierMins_Warhammers.Length), tierMins_Warhammers);

            int[] tiersBattleaxes = ApplyMinimumTierBreaks(CalcTierBreak(
                state.LoadOrder.PriorityOrder.Weapon().WinningOverrides().Where(weapon => weapon.HasKeyword("WeapTypeBattleaxe", state.LinkCache))
                    .Select(weapon => weapon.BasicStats?.Value ?? 0), tierMins_Battleaxes.Length), tierMins_Battleaxes);

            int[] tiersStaffs = ApplyMinimumTierBreaks(CalcTierBreak(
                state.LoadOrder.PriorityOrder.Weapon().WinningOverrides().Where(weapon => weapon.HasKeyword("WeapTypeStaff", state.LinkCache))
                    .Select(weapon => weapon.BasicStats?.Value ?? 0), tierMins_Staffs.Length), tierMins_Staffs);

            // Display Tier Breaks
            Console.WriteLine($"[TIERS] Bows (Value): {string.Join(", ", tiersBows)}");
            Console.WriteLine($"[TIERS] Daggers (Value): {string.Join(", ", tiersDaggers)}");
            Console.WriteLine($"[TIERS] Swords (Value): {string.Join(", ", tiersSwords)}");
            Console.WriteLine($"[TIERS] Maces (Value): {string.Join(", ", tiersMaces)}");
            Console.WriteLine($"[TIERS] Waraxes (Value): {string.Join(", ", tiersWaraxes)}");
            Console.WriteLine($"[TIERS] Greatswords (Value): {string.Join(", ", tiersGreatswords)}");
            Console.WriteLine($"[TIERS] Warhammers (Value): {string.Join(", ", tiersWarhammers)}");
            Console.WriteLine($"[TIERS] Battleaxes (Value): {string.Join(", ", tiersBattleaxes)}");
            Console.WriteLine($"[TIERS] Staffs (Value): {string.Join(", ", tiersStaffs)}");

            // Distribute each item to the appropriate LVLI
            foreach (var weapon in state.LoadOrder.PriorityOrder.Weapon().WinningOverrides())
            {
                // Exclusions
                string distribution = Distribution_Check(leveledListReferences, excludedPlugins, excludedMods, excludedItems, includedItems, weapon);
                if (distribution == "Base")
                {
                    continue;
                }
                if (distribution != "Distribute")
                {
                    Console.WriteLine($"[WEAP] {weapon.Name} {weapon.FormKey} Skipped: {distribution}");
                    continue;
                }
                else if (weapon.MajorFlags.HasFlag(Weapon.MajorFlag.NonPlayable))
                {
                    Console.WriteLine($"[WEAP] {weapon.Name} {weapon.FormKey} Skipped: Nonplayable");
                    continue;
                }

                // Build Leveled List Target String: Enchanted or Non-Enchanted
                string llTarget = llPrefix + "Weap_";
                string wepType = GetWeaponType(weapon, state.LinkCache);
                string wepEnc = "";
                if (weapon.ObjectEffect.FormKey != FormKey.Null && wepType != "Staff")
                {
                    wepEnc = "Enc_";
                }
                llTarget += wepEnc;

                // Build Leveled List Target String: Weapon Type
                if (wepType == "UNKNOWN")
                {
                    Console.WriteLine($"[WEAP] {weapon.Name} {weapon.EditorID} Skipped: Unable to resolve weapon type");
                    continue;
                }
                llTarget += $"{wepType}_";

                // Build Leveled List Target String: Tier (Material / Spell Level >> Value)
                int tier = 0;
                int[] tierBreaks = tiersBows;
                if (wepType != "Staff")
                {
                    tier = GetTierByMaterial_Weapon(weapon, state.LinkCache);
                    if (tier == 0)
                    {
                        if (wepType == "Bow") tierBreaks = tiersBows;
                        else if (wepType == "Dagger") tierBreaks = tiersDaggers;
                        else if (wepType == "Sword") tierBreaks = tiersSwords;
                        else if (wepType == "Mace") tierBreaks = tiersMaces;
                        else if (wepType == "Waraxe") tierBreaks = tiersWaraxes;
                        else if (wepType == "Greatsword") tierBreaks = tiersGreatswords;
                        else if (wepType == "Warhammer") tierBreaks = tiersWarhammers;
                        else if (wepType == "Battleaxe") tierBreaks = tiersBattleaxes;
                        else if (wepType == "Staff") tierBreaks = tiersStaffs;
                    }
                }

                // Build Leveled List Target String: Staff Type (Magic School) + Tier (Spell Level)
                else
                {
                    tierBreaks = tiersStaffs;
                    var objectEffect = weapon.ObjectEffect.TryResolve(state.LinkCache);
                    if (objectEffect is null)
                    {
                        tier = 0;
                        continue;
                    }
                    ActorValue magicSkill = ActorValue.None;
                    int spellLevel = 0;
                    foreach (var effect in objectEffect.Effects)
                    {
                        var magEff = effect.BaseEffect.TryResolve(state.LinkCache);
                        if (magEff is null)
                        {
                            continue;
                        }
                        magicSkill = magEff.MagicSkill;
                        spellLevel = (int)(magEff.MinimumSkillLevel / 25) + 1;
                    }

                    // Determine School
                    string spellSchool = magicSkill switch
                    {
                        ActorValue.Alteration => "Alt",
                        ActorValue.Conjuration => "Con",
                        ActorValue.Destruction => "Des",
                        ActorValue.Illusion => "Ill",
                        ActorValue.Restoration => "Res",
                        _ => "UNKNOWN"
                    };
                    llTarget += $"{spellSchool}_";
                    tier = spellLevel;
                }
                if (tier == 0)
                {
                    tier = (int)weapon.BasicStats!.Value switch
                    {
                        _ when weapon.BasicStats.Value < tierBreaks[1] => 1,
                        _ when weapon.BasicStats.Value < tierBreaks[2] => 2,
                        _ when weapon.BasicStats.Value < tierBreaks[3] => 3,
                        _ when weapon.BasicStats.Value < tierBreaks[4] => 4,
                        _ => 5,
                    };
                }
                llTarget += tier;

                // Add to Leveled List
                AddToLeveledList(state, RoleRimMod, weapon, llTarget);
                Console.WriteLine($"[WEAP] {weapon.Name} {weapon.EditorID} added to {llTarget}");
            }
            Console.WriteLine("=== End Weapon Records ===");
            Console.WriteLine();
        }



        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////



        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        // CALCULATE VALUE TIERS
        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        static int[] CalcTierBreak(
            IEnumerable<uint> values,
            int tierCount)
        {
            var sortedValues = values
                .OrderBy(value => value)
                .ToArray();

            if (sortedValues.Length == 0)
                return [.. Enumerable.Repeat(1, tierCount)];

            var breaks = new int[tierCount];

            for (int tier = 1; tier < tierCount; tier++)
            {
                int index = (int)Math.Ceiling(
                    sortedValues.Length * (double)tier / tierCount) - 1;

                index = Math.Clamp(index, 0, sortedValues.Length - 1);

                breaks[tier] = (int)sortedValues[index];
            }
            breaks[0] = 0;
            return breaks;
        }

        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        // CHECK IF ITEM IS FROM BASE GAME PLUGIN
        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        static bool IsFromBaseGameMaster(IMajorRecordGetter record)
        {
            return record.FormKey.ModKey == "Skyrim.esm"
                || record.FormKey.ModKey == "Update.esm"
                || record.FormKey.ModKey == "Dawnguard.esm"
                || record.FormKey.ModKey == "HearthFires.esm"
                || record.FormKey.ModKey == "Dragonborn.esm"
                || record.FormKey.ModKey == "RoleRim - Loot.esp";
        }

        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        // ADD ITEM TO INDICATED LEVELED LIST
        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        static void AddToLeveledList(
            IPatcherState<ISkyrimMod, ISkyrimModGetter> state,
            ISkyrimModGetter RoleRimMod,
            IItemGetter item,
            string targetLL)
        {
            foreach (var leveledList in RoleRimMod.LeveledItems)
            {
                if (leveledList.EditorID == targetLL)
                {
                    var itemList = state.PatchMod.LeveledItems.GetOrAddAsOverride(leveledList);
                    if (itemList is not null && itemList.Entries is not null)
                    {
                        itemList.Entries.Add(new LeveledItemEntry
                        {
                            Data = new LeveledItemEntryData
                            {
                                Reference = item.FormKey.ToLink<IItemGetter>(),
                                Level = 1,
                                Count = 1
                            }
                        });
                    }
                    break;
                }
            }
        }

        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        // RETURN TIER BASED ON ITEM VALUE
        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        static int GetTierByValue(uint value, uint tierSize, int maxTier)
        {
            if (value <= 0)
            {
                return 1;
            }
            return Math.Min((int)((value - 1) / tierSize) + 1, maxTier);
        }

        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        // RETURN TIER BASED ON ITEM MATERIAL - AMMO
        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        static int GetTierByMaterial_Ammo(IAmmunitionGetter ammo, ILinkCache linkCache)
        {
            if (ammo.HasKeyword("WeapMaterialIron", linkCache) ||
                ammo.HasKeyword("ArmorMaterialIron", linkCache) ||
                ammo.HasKeyword("ArmorMaterialHide", linkCache) ||
                ammo.HasKeyword("ArmorMaterialStudded", linkCache))
                return 1;
            if (ammo.HasKeyword("WeapMaterialSteel", linkCache) ||
                ammo.HasKeyword("ArmorMaterialSteel", linkCache) ||
                ammo.HasKeyword("ArmorMaterialLeather", linkCache) ||
                ammo.HasKeyword("ArmorMaterialScaled", linkCache))
                return 2;
            if (ammo.HasKeyword("WeapMaterialOrcish", linkCache) ||
                ammo.HasKeyword("WeapMaterialDwarven", linkCache) ||
                ammo.HasKeyword("WeapMaterialElven", linkCache) ||
                ammo.HasKeyword("DLC2WeaponMaterialNordic", linkCache) ||
                ammo.HasKeyword("ArmorMaterialNordicHeavy", linkCache) ||
                ammo.HasKeyword("ArmorMaterialNordicLight", linkCache) ||
                ammo.HasKeyword("ArmorMaterialSteelPlate", linkCache))
                return 3;
            if (ammo.HasKeyword("WeapMaterialGlass", linkCache) ||
                ammo.HasKeyword("ArmorMaterialGlass", linkCache) ||
                ammo.HasKeyword("WeapMaterialEbony", linkCache) ||
                ammo.HasKeyword("ArmorMaterialEbony", linkCache) ||
                ammo.HasKeyword("DLC2WeaponMaterialStalhrim", linkCache) ||
                ammo.HasKeyword("DLC2ArmorMaterialStalhrimHeavy", linkCache) ||
                ammo.HasKeyword("DLC2ArmorMaterialStalhrimLight", linkCache))
                return 4;
            if (ammo.HasKeyword("WeapMaterialDaedric", linkCache) ||
                ammo.HasKeyword("ArmorMaterialDaedric", linkCache) ||
                ammo.HasKeyword("DLC1WeapMaterialDragonbone", linkCache) ||
                ammo.HasKeyword("ArmorMaterialDragonPlate", linkCache) ||
                ammo.HasKeyword("ArmorMaterialDragonscale", linkCache))
                return 5;
            return 0;
        }

        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        // RETURN TIER BASED ON ITEM MATERIAL - ARMOR
        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        static int GetTierByMaterial_Armor(IArmorGetter armor, ILinkCache linkCache)
        {
            foreach (var keywordLink in armor.Keywords ?? [])
            {
                if (!keywordLink.TryResolve(linkCache, out var keyword))
                    continue;
                string? editorID = keyword.EditorID;
                if (editorID is null)
                    continue;
                if (editorID.Contains("Daedric", StringComparison.OrdinalIgnoreCase) ||
                    editorID.Contains("Dragon", StringComparison.OrdinalIgnoreCase))
                    return 5;
                if (editorID.Contains("Glass", StringComparison.OrdinalIgnoreCase) ||
                    editorID.Contains("Ebony", StringComparison.OrdinalIgnoreCase) ||
                    editorID.Contains("Stalhrim", StringComparison.OrdinalIgnoreCase))
                    return 4;
                if (editorID.Contains("Orcish", StringComparison.OrdinalIgnoreCase) ||
                    editorID.Contains("Dwarven", StringComparison.OrdinalIgnoreCase) ||
                    editorID.Contains("Elven", StringComparison.OrdinalIgnoreCase) ||
                    editorID.Contains("Plate", StringComparison.OrdinalIgnoreCase) ||
                    editorID.Contains("Nordic", StringComparison.OrdinalIgnoreCase))
                    return 3;
                if (editorID.Contains("Steel", StringComparison.OrdinalIgnoreCase) ||
                    editorID.Contains("Leather", StringComparison.OrdinalIgnoreCase) ||
                    editorID.Contains("Imperial", StringComparison.OrdinalIgnoreCase) ||
                    editorID.Contains("Stormcloak", StringComparison.OrdinalIgnoreCase) ||
                    editorID.Contains("Scale", StringComparison.OrdinalIgnoreCase))
                    return 2;
                if (editorID.Contains("Iron", StringComparison.OrdinalIgnoreCase) ||
                    editorID.Contains("Hide", StringComparison.OrdinalIgnoreCase) ||
                    editorID.Contains("Studded", StringComparison.OrdinalIgnoreCase))
                    return 1;
            }
            return 0;
        }

        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        // RETURN TIER BASED ON ITEM MATERIAL - WEAPON
        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        static int GetTierByMaterial_Weapon(IWeaponGetter weapon, ILinkCache linkCache)
        {
            if (weapon.HasKeyword("WeapMaterialDaedric", linkCache) ||
                weapon.HasKeyword("DLC1WeapMaterialDragonbone", linkCache))
                return 5;
            if (weapon.HasKeyword("WeapMaterialGlass", linkCache) ||
                weapon.HasKeyword("WeapMaterialEbony", linkCache) ||
                weapon.HasKeyword("DLC2WeaponMaterialStalhrim", linkCache))
                return 4;
            if (weapon.HasKeyword("WeapMaterialOrcish", linkCache) ||
                weapon.HasKeyword("WeapMaterialDwarven", linkCache) ||
                weapon.HasKeyword("WeapMaterialElven", linkCache) ||
                weapon.HasKeyword("DLC2WeaponMaterialNordic", linkCache))
                return 3;
            if (weapon.HasKeyword("WeapMaterialSteel", linkCache))
                return 2;
            if (weapon.HasKeyword("WeapMaterialIron", linkCache))
                return 1;
            return 0;
        }

        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        // BUILD LEVELED LIST REFERENCES
        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        private static Dictionary<FormKey, HashSet<FormKey>> Build_References(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
            
        //private static HashSet<FormKey> Build_References(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            var references = new Dictionary<FormKey, HashSet<FormKey>>();

            // Build reference for each mod, not just the winning mod
            foreach (var modListing in state.LoadOrder.PriorityOrder)
            {
                var mod = modListing.Mod;
                if (mod is null) continue;

                // Include all LVLIs in each mod
                foreach (var lvli in mod.LeveledItems)
                {
                    if (lvli.Entries is null) continue;

                    // Include every entry in each LVLI
                    foreach (var entry in lvli.Entries)
                    {
                        var reference = entry.Data?.Reference.FormKey;
                        if (reference is null || reference.Value.IsNull) continue;
                        if (!references.TryGetValue(reference.Value, out var lvliReferences))
                        {
                            lvliReferences = [];
                            references[reference.Value] = lvliReferences;
                        }

                        lvliReferences.Add(lvli.FormKey);
                    }
                }
            }
            return references;
        }

        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        // DETERMINE WHETHER TO USE PREDETERMINED OR CALCULATED VALUE TIERS
        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        private static int[] ApplyMinimumTierBreaks(int[] calculatedBreaks, int[] manualBreaks)
        {
            for (int i = 0; i < calculatedBreaks.Length; i++)
            {
                calculatedBreaks[i] = Math.Max(
                    calculatedBreaks[i],
                    manualBreaks[i]);
            }

            return calculatedBreaks;
        }

        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        // RETURN WHY ITEM SHOULD NOT BE DISTRIBUTED IF IT SHOULD NOT BE
        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        private static string Distribution_Check(
            Dictionary<FormKey, HashSet<FormKey>> leveledListReferences,
            HashSet<string> excludedPlugins,
            HashSet<string> excludedMods,
            HashSet<string> excludedItems,
            HashSet<string> includedItems,
            IMajorRecordGetter item)
        {
            if (item is null)
            {
                return "Null Item";
            }
            if (excludedPlugins.Contains(item.FormKey.ModKey.ToString()))
            {
                return "Excluded Plugin";
            }
            if (excludedMods.Contains(item.FormKey.ModKey.ToString()))
            {
                return "Excluded Mod";
            }
            if (item.EditorID is not null && 
                excludedItems.Contains(item.EditorID))
            {
                return "Excluded Item";
            }
            bool referencedByBaseGameLVLI = false;
            if (leveledListReferences.TryGetValue(item.FormKey, out var itemLVLIs))
            {
                var checkedLVLIs = new HashSet<FormKey>();
                bool CheckLVLIChain(HashSet<FormKey> lvliReferences)
                {
                    foreach (var lvliFormKey in lvliReferences)
                    {
                        if (!checkedLVLIs.Add(lvliFormKey))
                        {
                            continue;
                        }
                        string lvliPlugin = lvliFormKey.ModKey.ToString();
                        if (excludedPlugins.Contains(lvliPlugin))
                        {
                            return true;
                        }
                        if (leveledListReferences.TryGetValue(lvliFormKey, out var parentLVLIs) &&
                            CheckLVLIChain(parentLVLIs))
                        {
                            return true;
                        }
                    }
                    return false;
                }
                referencedByBaseGameLVLI = CheckLVLIChain(itemLVLIs);
            }
            if (!referencedByBaseGameLVLI &&
                !includedItems.Contains(item.FormKey.ModKey.ToString()))
            {
                return "Not Referenced";
            }
            return "Distribute";
        }

        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        // RETURN ARMOR TYPE (HEAVY OR LIGHT)
        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        static string GetArmorType(IArmorGetter armor, ILinkCache linkCache)
        {
            if (armor.HasKeyword("ArmorHeavy", linkCache)) return "Hvy";
            if (armor.HasKeyword("ArmorLight", linkCache)) return "Lit";
            return "";
        }

        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        // RETURN ARMOR PIECE
        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        static string GetArmorPiece(IArmorGetter armor, ILinkCache linkCache)
        {
            // Clothing and Jewelry (String in Keywords)
            foreach (var keywordLink in armor.Keywords ?? [])
            {
                if (!keywordLink.TryResolve(linkCache, out var keyword))
                    continue;

                string? editorID = keyword.EditorID;
                if (editorID is null)
                    continue;

                if (editorID.Contains("Circlet", StringComparison.OrdinalIgnoreCase))
                    return "Circlet";

                if (editorID.Contains("Necklace", StringComparison.OrdinalIgnoreCase))
                    return "Necklace";

                if (editorID.Contains("Ring", StringComparison.OrdinalIgnoreCase))
                    return "Ring";

                if (editorID.Contains("Clothing", StringComparison.OrdinalIgnoreCase))
                    return "Clothing";
            }

            // Cuirass, Boots, Gauntlets, Helmet, Shield
            if (armor.HasKeyword("ArmorCuirass", linkCache))
                return "Cuirass";
            if (armor.HasKeyword("ArmorBoots", linkCache))
                return "Boots";
            if (armor.HasKeyword("ArmorGauntlets", linkCache))
                return "Gauntlets";
            if (armor.HasKeyword("ArmorHelmet", linkCache))
                return "Helmet";
            if (armor.HasKeyword("ArmorShield", linkCache))
                return "Shield";
            return "UNKNOWN";
        }

        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        // RETURN MISCITEM GROUP
        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        static string GetMiscItemGroup(IMiscItemGetter miscItem, ILinkCache linkCache)
        {
            if (miscItem.HasKeyword("VendorItemGem", linkCache))
                return "Gem";
            if (miscItem.HasKeyword("VendorItemClutter", linkCache))
                return "Clutter";
            if (miscItem.HasKeyword("VendorItemAnimalHide", linkCache) ||
                miscItem.HasKeyword("VendorItemAnimalPart", linkCache))
                return "Animal";
            if (miscItem.HasKeyword("VendorItemOreIngot", linkCache))
            {
                if (miscItem.EditorID?.Contains("Ore", StringComparison.OrdinalIgnoreCase) == true
                    || miscItem.Name?.String?.Contains("Ore", StringComparison.OrdinalIgnoreCase) == true)
                {
                    return "Ore";
                }
                else if (miscItem.EditorID?.Contains("Ingot", StringComparison.OrdinalIgnoreCase) == true
                         || miscItem.Name?.String?.Contains("Ingot", StringComparison.OrdinalIgnoreCase) == true)
                {
                    return "Ingot";
                }
            }
            return "UNKNOWN";
        }

        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        // RETURN WEAPON TYPE
        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        static string GetWeaponType(IWeaponGetter weapon, ILinkCache linkCache)
        {
            if (weapon.HasKeyword("WeapTypeBattleaxe", linkCache))
                return "Battleaxe";
            if (weapon.HasKeyword("WeapTypeBow", linkCache))
                return "Bow";
            if (weapon.HasKeyword("WeapTypeDagger", linkCache))
                return "Dagger";
            if (weapon.HasKeyword("WeapTypeGreatsword", linkCache))
                return "Greatsword";
            if (weapon.HasKeyword("WeapTypeMace", linkCache))
                return "Mace";
            if (weapon.HasKeyword("WeapTypeStaff", linkCache))
                return "Staff";
            if (weapon.HasKeyword("WeapTypeSword", linkCache))
                return "Sword";
            if (weapon.HasKeyword("WeapTypeWaraxe", linkCache))
                return "Waraxe";
            if (weapon.HasKeyword("WeapTypeWarhammer", linkCache))
                return "Warhammer";
            if (weapon.HasKeyword("WeapTypeStaff", linkCache))
                return "Staff";
            return "UNKNOWN";
        }

        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        // RETURN ALCHEMY BASE EFFECT
        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        private static string Get_Alchemy_Effect(IIngestibleGetter ingestible, ILinkCache linkCache)
        {
            foreach (var effect in ingestible.Effects)
            {
                var magicEffect = effect.BaseEffect.TryResolve(linkCache);
                var effectType = magicEffect!.Archetype.Type;
                var actorValue = magicEffect!.Archetype.ActorValue;

                if (magicEffect is not null)
                {
                    // Health / HealRate / HealRateMult
                    if ((effectType == MagicEffectArchetype.TypeEnum.ValueModifier ||
                         effectType == MagicEffectArchetype.TypeEnum.DualValueModifier ||
                         effectType == MagicEffectArchetype.TypeEnum.PeakValueModifier) && (
                         actorValue == ActorValue.Health ||
                         actorValue == ActorValue.HealRate ||
                         actorValue == ActorValue.HealRateMult))
                    {
                        return "Stat_H";
                    }


                    // Magicka / MagickaRate / MagickaRateMult
                    else if ((effectType == MagicEffectArchetype.TypeEnum.ValueModifier ||
                         effectType == MagicEffectArchetype.TypeEnum.DualValueModifier ||
                         effectType == MagicEffectArchetype.TypeEnum.PeakValueModifier) && (
                         actorValue == ActorValue.Magicka ||
                         actorValue == ActorValue.MagickaRate ||
                         actorValue == ActorValue.MagickaRateMult))
                    {
                        return "Stat_M";
                    }

                    // Stamina / StaminaRate / StaminaRateMult
                    else if ((effectType == MagicEffectArchetype.TypeEnum.ValueModifier ||
                         effectType == MagicEffectArchetype.TypeEnum.DualValueModifier ||
                         effectType == MagicEffectArchetype.TypeEnum.PeakValueModifier) && (
                         actorValue == ActorValue.Stamina ||
                         actorValue == ActorValue.StaminaRate ||
                         actorValue == ActorValue.StaminaRateMult))
                    {
                        return "Stat_S";
                    }

                    // Skills: Combat
                    else if ((effectType == MagicEffectArchetype.TypeEnum.ValueModifier ||
                         effectType == MagicEffectArchetype.TypeEnum.DualValueModifier ||
                         effectType == MagicEffectArchetype.TypeEnum.PeakValueModifier) && (
                         actorValue == ActorValue.Archery || actorValue == ActorValue.MarksmanModifier || actorValue == ActorValue.MarksmanPowerModifier ||
                         actorValue == ActorValue.Block || actorValue == ActorValue.BlockModifier  || actorValue == ActorValue.BlockPowerModifier ||
                         actorValue == ActorValue.HeavyArmor || actorValue == ActorValue.HeavyArmorModifier || actorValue == ActorValue.HeavyArmorPowerModifier ||
                         actorValue == ActorValue.OneHanded || actorValue == ActorValue.OneHandedModifier || actorValue == ActorValue.OneHandedPowerModifier ||
                         actorValue == ActorValue.Smithing || actorValue == ActorValue.SmithingModifier || actorValue == ActorValue.SmithingPowerModifier ||
                         actorValue == ActorValue.TwoHanded || actorValue == ActorValue.TwoHandedModifier || actorValue == ActorValue.TwoHandedPowerModifier))
                    {
                        return "Skill_Combat";
                    }

                    // Skills: Magic
                    else if ((effectType == MagicEffectArchetype.TypeEnum.ValueModifier ||
                         effectType == MagicEffectArchetype.TypeEnum.DualValueModifier ||
                         effectType == MagicEffectArchetype.TypeEnum.PeakValueModifier) && (
                         actorValue == ActorValue.Alteration || actorValue == ActorValue.AlterationModifier || actorValue == ActorValue.HeavyArmorPowerModifier ||
                         actorValue == ActorValue.Conjuration || actorValue == ActorValue.ConjurationModifier || actorValue == ActorValue.ConjurationPowerModifier ||
                         actorValue == ActorValue.Destruction || actorValue == ActorValue.DestructionModifier || actorValue == ActorValue.DestructionPowerModifier ||
                         actorValue == ActorValue.Enchanting || actorValue == ActorValue.EnchantingModifier || actorValue == ActorValue.EnchantingPowerModifier ||
                         actorValue == ActorValue.Illusion || actorValue == ActorValue.IllusionModifier || actorValue == ActorValue.IllusionPowerModifier ||
                         actorValue == ActorValue.Restoration || actorValue == ActorValue.RestorationModifier || actorValue == ActorValue.RestorationPowerModifier))
                    {
                        return "Skill_Magic";
                    }

                    // Skills: Stealth
                    else if ((effectType == MagicEffectArchetype.TypeEnum.ValueModifier ||
                         effectType == MagicEffectArchetype.TypeEnum.DualValueModifier ||
                         effectType == MagicEffectArchetype.TypeEnum.PeakValueModifier) && (
                         actorValue == ActorValue.Alchemy || actorValue == ActorValue.AlchemyModifier || actorValue == ActorValue.AlchemyPowerModifier ||
                         actorValue == ActorValue.LightArmor || actorValue == ActorValue.LightArmorModifier || actorValue == ActorValue.LightArmorPowerModifier ||
                         actorValue == ActorValue.Lockpicking || actorValue == ActorValue.LockpickingModifier || actorValue == ActorValue.LockpickingPowerModifier ||
                         actorValue == ActorValue.Pickpocket || actorValue == ActorValue.PickpocketModifier || actorValue == ActorValue.PickpocketPowerModifier ||
                         actorValue == ActorValue.Sneak || actorValue == ActorValue.SneakingModifier || actorValue == ActorValue.SneakingPowerModifier ||
                         actorValue == ActorValue.Speech || actorValue == ActorValue.SpeechcraftModifier || actorValue == ActorValue.SpeechcraftPowerModifier))
                    {
                        return "Skill_Stealth";
                    }

                    // Resistances
                    else if ((effectType == MagicEffectArchetype.TypeEnum.ValueModifier ||
                         effectType == MagicEffectArchetype.TypeEnum.DualValueModifier ||
                         effectType == MagicEffectArchetype.TypeEnum.PeakValueModifier) && (
                         actorValue == ActorValue.ResistDisease ||
                         actorValue == ActorValue.ResistFire ||
                         actorValue == ActorValue.ResistFrost ||
                         actorValue == ActorValue.ResistMagic ||
                         actorValue == ActorValue.ResistShock ||
                         actorValue == ActorValue.DamageResist ||
                         actorValue == ActorValue.PoisonResist))
                    {
                        return "Resist";
                    }
                }
            }
            return "Misc";
        }
    }
}
