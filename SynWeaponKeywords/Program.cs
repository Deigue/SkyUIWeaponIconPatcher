using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using WeaponKeywords.Types;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.Skyrim;
using Newtonsoft.Json.Linq;

namespace WeaponKeywords
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            return SynthesisPipeline.Instance.Patch<ISkyrimMod, ISkyrimModGetter>(
                args: args,
                patcher: RunPatch,
                userPreferences: new UserPreferences()
                {
                    ActionsForEmptyArgs = new RunDefaultPatcher()
                    {
                        IdentifyingModKey = "WeapTypeKeywords.esp",
                        TargetRelease = GameRelease.SkyrimSE,
                        BlockAutomaticExit = false,
                    }
                });
        }

        private static void RunPatch(SynthesisState<ISkyrimMod, ISkyrimModGetter> state)
        {
            var database = JObject.Parse(File.ReadAllText(Path.Combine(state.ExtraSettingsDataPath, "database.json")))
                .ToObject<Database>();
            Dictionary<string, FormKey> formkeys = new Dictionary<string, FormKey>();
            Dictionary<string, List<FormKey>> alternativekeys = new Dictionary<string, List<FormKey>>();
            foreach (var (key, value) in database.DB)
            {
                foreach (var src in database.sources)
                {
                    if (value.keyword == null) continue;
                    var keyword = state.LoadOrder.PriorityOrder.Keyword()
                        .WinningOverrides()
                        .FirstOrDefault(kywd =>
                            ((kywd.FormKey.ModKey.Equals(src)) &&
                             ((kywd.EditorID?.ToString() ?? "") == value.keyword)));
                    if (keyword == null || formkeys.ContainsKey(key)) continue;
                    formkeys[key] = keyword.FormKey;
                    break;
                }
            }

            foreach (var weapon in state.LoadOrder.PriorityOrder.Weapon().WinningOverrides())
            {
                var edid = weapon.EditorID;
                var nameToTest = weapon.Name?.String?.ToLower();
                var matchingKeywords = database.DB.Where(kv =>
                        kv.Value.commonNames.Any(cn => nameToTest?.Contains(cn) ?? false))
                    .Select(kd => kd.Key)
                    .ToArray();
                var globalExcludes = database.excludes.phrases.Any(ph => nameToTest?.Contains(ph) ?? false) ||
                                     database.excludes.weapons.Contains(edid);

                if (database.includes.ContainsKey(edid ?? ""))
                {
                    if (formkeys.ContainsKey(database.includes[edid ?? ""]))
                    {
                        var nw = state.PatchMod.Weapons.GetOrAddAsOverride(weapon);
                        nw.Keywords?.Add(formkeys[database.includes[edid ?? ""]]);
                        Console.WriteLine(
                            $"{nameToTest} is {database.DB[database.includes[edid ?? ""]].outputDescription}, adding {database.includes[edid ?? ""]} from {formkeys[database.includes[edid ?? ""]].ModKey}");
                    }
                    else
                    {
                        Console.WriteLine(
                            $"{nameToTest} is {database.DB[database.includes[edid ?? ""]].outputDescription}, but not changing (missing esp?)");
                    }
                }

                if (matchingKeywords.Length <= 0 || globalExcludes) continue;

                if (!matchingKeywords.All(weaponType =>
                    weapon.Keywords?.Contains(formkeys.GetValueOrDefault(weaponType)) ?? false))
                {
                    var nw = state.PatchMod.Weapons.GetOrAddAsOverride(weapon);

                    foreach (var kyd in matchingKeywords)
                    {
                        if (formkeys.ContainsKey(kyd) && !(nw.Keywords?.Contains(formkeys[kyd]) ?? false) &&
                            !database.DB[kyd].exclude.Any(cn => nameToTest?.Contains(cn) ?? false))
                        {
                            nw.Keywords?.Add(formkeys[kyd]);
                            Console.WriteLine(
                                $"{nameToTest} is {database.DB[kyd].outputDescription}, adding {kyd} from {formkeys[kyd].ModKey}");
                        }
                    }
                }

                foreach (var kyd in matchingKeywords)
                {
                    if (!matchingKeywords.All(kyd => database.DB[kyd].akeywords?.Length > 0))
                    {
                        if (!alternativekeys.ContainsKey(kyd))
                        {
                            alternativekeys[kyd] = new List<FormKey>();
                            foreach (var keywd in database.DB[kyd].akeywords)
                            {
                                var test = state.LoadOrder.PriorityOrder.Keyword().WinningOverrides()
                                    .Where(kywd => ((kywd.EditorID ?? "") == keywd)).FirstOrDefault();
                                if (test != null)
                                {
                                    Console.WriteLine(
                                        $"Alternative Keyword found using {test.FormKey.ModKey} for {kyd}");
                                    alternativekeys[kyd].Add(test.FormKey);
                                }
                                else
                                {
                                    Console.WriteLine(
                                        $"Alternative Keyword not found generating {keywd} for {kyd}");
                                    alternativekeys[kyd].Add(state.PatchMod.Keywords.AddNew(keywd).FormKey);
                                }
                            }
                        }

                        if (alternativekeys[kyd].Count <= 0) continue;
                        foreach (var alt in alternativekeys[kyd])
                        {
                            var nw = state.PatchMod.Weapons.GetOrAddAsOverride(weapon);
                            nw.Keywords?.Add(alt);
                            Console.WriteLine(
                                $"{nameToTest} is {database.DB[kyd].outputDescription}, adding extra keyword from {alt.ModKey}");
                        }
                    }
                }
            }
        }
    }
}