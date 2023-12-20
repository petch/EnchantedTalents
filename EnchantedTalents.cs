using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using HarmonyLib.Tools;
using PotionCraft.Core.Extensions;
using PotionCraft.LocalizationSystem;
using PotionCraft.ManagersSystem;
using PotionCraft.ManagersSystem.Player;
using PotionCraft.ManagersSystem.SaveLoad;
using PotionCraft.ManagersSystem.Trade;
using PotionCraft.ObjectBased.Garden.GrowingSpot;
using PotionCraft.ObjectBased.Haggle;
using PotionCraft.ObjectBased.InteractiveItem.InventoryObject;
using PotionCraft.ObjectBased.UIElements.FloatingText;
using PotionCraft.ObjectBased.UIElements.TalentsWindow;
using PotionCraft.ScriptableObjects.Potion;
using PotionCraft.ScriptableObjects.Talents;
using PotionCraft.TMPAtlasGenerationSystem;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Zapetch
{
    [BepInPlugin(GUID, NAME, VERSION)]
    [BepInProcess("Potion Craft.exe")]
    public class EnchantedTalents : BaseUnityPlugin
    {
        private const string GUID = "com.zapetch.potioncraft.enchantedtalents";
        private const string NAME = "Enchanted Talents";
        private const string VERSION = "1.1.0";

        private readonly Harmony harmony = new Harmony(GUID);

        private void Awake()
        {
            harmony.PatchAll();
        }

        [HarmonyPatch(typeof(SpotPlant), "GatherIngredient")]
        class SpotPlantPatch
        {
            static AccessTools.FieldRef<SpotPlant, Vector2Int> amountRef = AccessTools.FieldRefAccess<SpotPlant, Vector2Int>("ingredientAmount");
            static AccessTools.FieldRef<SpotPlant, Vector2> offsetRef = AccessTools.FieldRefAccess<SpotPlant, Vector2>("floatingTextCursorSpawnOffset");
            public static float doubleChance = 0f;
            static Vector2Int amount;

            static bool Prefix(SpotPlant __instance)
            {
                amount = amountRef(__instance);
                if (Random.value < doubleChance)
                    amountRef(__instance) = 2 * amount;
                return true;
            }

            static void Postfix(SpotPlant __instance)
            {
                amountRef(__instance) = amount;
            }
        }

        [HarmonyPatch(typeof(PlayerManager.TalentsSubManager), "OnManagerAwake")]
        class TalentsManagerPatch
        {
            static void Postfix(PlayerManager.TalentsSubManager __instance)
            {
                OnLoad();

                ReplaceAlchemicalPractices();

                ReplaceHaggles();

                var slots = CreateTalents(0, "Slots");
                Talent.allTalents.AddRange(slots);

                var plants = CreateTalents(1, "Plants");
                Talent.allTalents.AddRange(plants);

                LocalizationManager.OnLocaleChanged.AddListener(KeyPatch.UpdateWords);
                Managers.SaveLoad.onProgressLoad.AddListener(-100f, OnLoad);
            }

            static void OnLoad()
            {
                SpotPlantPatch.doubleChance = 0f;
            }

            static void ReplaceAlchemicalPractices()
            {
                var practices = new List<TalentAlchemicalPractice>();
                int last = -1;
                for (int i = 0; i < Talent.allTalents.Count; i++)
                {
                    var practice = Talent.allTalents[i] as TalentAlchemicalPractice;
                    if (practice == null)
                        continue;
                    practices.Add(practice);
                    last = i;
                }

                while (practices.Count < 10)
                {
                    var practice = ScriptableObject.CreateInstance<TalentAlchemicalPractice>();
                    if (last != -1)
                    {
                        Talent.allTalents[last].nextTalent = practice;
                        practice.parentTalent = Talent.allTalents[last];
                    }
                    Talent.allTalents.Insert(last + 1, practice);
                    last++;
                    practices.Add(practice);
                }

                for (int i = 0; i < practices.Count; i++)
                {
                    var practice = practices[i];
                    practice.spawnChanceAddendum = 0.1f;
                    practice.name = $"2-{i + 1} AlchemicalPractice";
                    practice.cost = Mathf.Max(1, (i + 1) / 2);
                }
            }

            static void ReplaceHaggles()
            {
                var haggles = new List<TalentHaggle>();
                int last = -1;
                for (int i = 0; i < Talent.allTalents.Count; i++)
                {
                    var haggle = Talent.allTalents[i] as TalentHaggle;
                    if (haggle == null)
                        continue;
                    haggles.Add(haggle);
                    last = i;
                }

                while (haggles.Count < 5)
                {
                    var haggle = ScriptableObject.CreateInstance<TalentHaggle>();
                    if (last != -1)
                    {
                        Talent.allTalents[last].nextTalent = haggle;
                        haggle.parentTalent = Talent.allTalents[last];
                    }
                    Talent.allTalents.Insert(last + 1, haggle);
                    last++;
                    haggles.Add(haggle);
                }

                for (int i = 0; i < haggles.Count; i++)
                {
                    var haggle = haggles[i];
                    haggle.difficultyToUnlock = (PotionCraft.ObjectBased.Haggle.Difficulty)(i);
                    haggle.name = $"4-{i + 1} Haggling";
                    haggle.cost = (i + 1) * 2;
                }
            }

            static List<TalentRecipeBookSlots> CreateTalents(int id, string name)
            {
                int count = 10;
                var talents = new List<TalentRecipeBookSlots>();
                for (int i = 0; i < count; i++)
                {
                    var talent = ScriptableObject.CreateInstance<TalentRecipeBookSlots>();
                    talent.name = $"ET-{i + 1} {name}";
                    talent.maxSlotsCount = id;
                    talent.cost = Mathf.Max(1, (i + 1) / 2);
                    talents.Add(talent);
                }
                for (int i = 0; i < count; i++)
                {
                    talents[i].nextTalent = i < count - 1 ? talents[i + 1] : null;
                    talents[i].parentTalent = i > 0 ? talents[i - 1] : null;
                }
                return talents;
            }
        }

        [HarmonyPatch(typeof(TalentRecipeBookSlots), "OnEarn")]
        class TalentRecipeBookSlotsPatch
        {
            static bool Prefix(TalentRecipeBookSlots __instance)
            {
                if (Managers.SaveLoad.SystemState == SaveLoadManager.SystemStateEnum.Loading)
                    return true;
                var id = __instance.maxSlotsCount;
                if (id == 0)
                    Managers.Potion.recipeBook.ChangeMaxRecipesCount(Managers.Potion.recipeBook.GetPagesCount() + 1);
                if (id == 1)
                    SpotPlantPatch.doubleChance += 0.1f;
                return false;
            }
        }

        [HarmonyPatch(typeof(TalentsWindow), "Awake")]
        class TalentsWindowPatch
        {
            static AccessTools.FieldRef<TalentsWindow, Transform> buttonsContainerRef = AccessTools.FieldRefAccess<TalentsWindow, Transform>("buttonsContainer");

            static void Postfix(TalentsWindow __instance)
            {
                var container = buttonsContainerRef(__instance);
                __instance.transform.Find("TalentTitleText1").parent = container;
                __instance.transform.Find("TalentTitleText2").parent = container;
                __instance.transform.Find("TalentTitleText3").parent = container;
                __instance.transform.Find("TalentTitleText4").parent = container;

                var slotsTitle = Instantiate(container.Find("TalentTitleText4"), container);
                slotsTitle.localPosition += new Vector3(4.36f, 0f, 0f);
                var slotsText = slotsTitle.GetComponent<TMPro.TextMeshPro>();
                slotsText.text = new Key("#recipe_book_slots_upgrade").GetText();
                slotsText.GetComponent<LocalizedText>().key = new Key("#recipe_book_slots_upgrade");

                var plantsTitle = Instantiate(container.Find("TalentTitleText4"), container);
                plantsTitle.localPosition += new Vector3(2f * 4.36f, 0f, 0f);
                var plantsText = plantsTitle.GetComponent<TMPro.TextMeshPro>();
                plantsText.text = new Key("#tutorial_hint_move_to_lab_1_title").GetText();
                plantsText.GetComponent<LocalizedText>().key = new Key("#tutorial_hint_move_to_lab_1_title");

                var scale = container.localScale;
                scale *= 4f / 6f;
                container.localScale = scale;
                container.localPosition = new Vector3(-2.9f, 1.9f, 0f);

                var background = container.parent.Find("Background");
                background.localScale = container.localScale;
                background.localPosition = new Vector3(0f, 1.9f, 0f);
                var renderer = background.GetComponent<SpriteRenderer>();
                renderer.sprite = LoadSprite("BepInEx/plugins/EnchantedTalents.png");
                background.parent = container;

                var levelText = container.parent.Find("LevelText");
                levelText.parent = container;
                levelText.localPosition += new Vector3(0f, 2.1f, 0);
                levelText.gameObject.SetActive(false);

                var talentPoints = container.parent.Find("TalentPoints");
                talentPoints.parent = container;
                talentPoints.localPosition += new Vector3(0f, 0.4f, 0);

                container.localPosition += new Vector3(0, -1.9f, 0f);
            }

            public static Sprite LoadSprite(string path)
            {
                if (!File.Exists(path))
                    return null;
                var data = File.ReadAllBytes(path);
                var texture = new Texture2D(0, 0, TextureFormat.ARGB32, false, false);
                texture.filterMode = FilterMode.Bilinear;
                texture.LoadImage(data);
                return Sprite.Create(texture, new Rect(0.0f, 0.0f, texture.width, texture.height), new Vector2(0.5f, 0.5f));
            }
        }

        [HarmonyPatch(typeof(Key), nameof(Key.GetText), new System.Type[] { typeof(LocalizationManager.Locale) })]
        class KeyPatch
        {
            static AccessTools.FieldRef<Key, string> keyRef = AccessTools.FieldRefAccess<Key, string>("key");
            static Dictionary<string, string> words = new Dictionary<string, string>();
            static bool isUpdating;

            static KeyPatch()
            {
                UpdateWords();
            }

            public static void UpdateWords()
            {
                print("EnchantedTalents.KeyPatch.UpdateWords");
                words.Clear();
                isUpdating = true;

                var practiceTitle = new Key("talent_2-1_AlchemicalPractice").GetText();
                var practiceDesc = new Key("talent_description_2-1_AlchemicalPractice").GetText();
                for (int i = 1; i <= 10; i++)
                {
                    words.Add($"talent_2-{i}_AlchemicalPractice", practiceTitle.Replace("I", i.ToRoman()));
                    words.Add($"talent_description_2-{i}_AlchemicalPractice", practiceDesc.Replace("10%", $"{i * 10}%"));
                }

                var haggleTitle = new Key("talent_4-1_Haggling").GetText();
                var haggleDesc = new Key("goal_HaggleAndGetBetterDeal").GetText();
                var difficulties = new string[] {
                    new Key("haggle_difficulty_veryeasy").GetText(),
                    new Key("haggle_difficulty_easy").GetText(),
                    new Key("haggle_difficulty_medium").GetText(),
                    new Key("haggle_difficulty_hard").GetText(),
                    new Key("haggle_difficulty_veryhard").GetText()
                };
                for (int i = 1; i <= 5; i++)
                {
                    words.Add($"talent_4-{i}_Haggling", haggleTitle.Replace("I", i.ToRoman()));
                    words.Add($"talent_description_4-{i}_Haggling", $"{haggleDesc}: {difficulties[i - 1]}");
                }

                var slotsTitle = new Key("recipe_book_slots_upgrade").GetText();
                var slotsDesc = new Key("recipe_book_slots_upgrade_description").GetText();
                for (int i = 1; i <= 10; i++)
                {
                    words.Add($"talent_ET-{i}_Slots", $"{slotsTitle} {i.ToRoman()}");
                    words.Add($"talent_description_ET-{i}_Slots", slotsDesc);
                }

                var plantsTitle = new Key("tutorial_hint_move_to_lab_1_title").GetText();
                var plantsDesc = new Key("goal_GatherIngredient").GetText();
                var chance = practiceDesc.Split('\n').Last();
                for (int i = 1; i <= 10; i++)
                {
                    words.Add($"talent_ET-{i}_Plants", $"{plantsTitle} {i.ToRoman()}");
                    words.Add($"talent_description_ET-{i}_Plants", $"{plantsDesc} ×2.\n\n{chance.Replace("10%", $"{i * 10}%")}");
                }

                isUpdating = false;
            }

            static bool Prefix(Key __instance, ref string __result, LocalizationManager.Locale locale)
            {
                var name = keyRef(__instance);
                string text;
                if (!isUpdating && words.TryGetValue(name, out text))
                {
                    __result = text;
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(TradeManagerHaggleSettings), "GetDifficultySettings")]
        class TradeManagerHaggleSettingsPatch
        {
            static void Postfix(TradeManagerHaggleSettings __instance, ref DifficultySettings __result)
            {
                __result.lockedByDefault = true;
                __result.pointerMovingSpeedMultiplier = 1f;
                var average = 0.5f * __result.amplitudeOnCorrectBonusMultiplier + 0.5f * __result.amplitudeOnIncorrectBonusMultiplier;
                __result.amplitudeOnCorrectBonusMultiplier = average;
                __result.amplitudeOnIncorrectBonusMultiplier = average;
            }
        }

        [HarmonyPatch(typeof(HaggleWindow), "OnLoad")]
        class HaggleWindowPatch
        {
            static void Postfix(HaggleWindow __instance)
            {
                __instance.unlockedDifficulties.Clear();
            }
        }
    }
}
