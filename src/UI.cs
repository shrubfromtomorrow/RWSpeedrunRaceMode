using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HUD;
using JollyCoop.JollyMenu;
using Menu;
using Menu.Remix;
using Menu.Remix.MixedUI;
using MoreSlugcats;
using UnityEngine;
using UnityEngine.UI;
using static SpeedrunRaceMode.CWTs;
using System.IO;
using BepInEx;

namespace SpeedrunRaceMode
{
    public static class UI
    {
        public static ConditionalWeakTable<SlugcatSelectMenu, RaceModeConfig> ssmRaceModeConfig = new();
        public static ConditionalWeakTable<HUD.Map, DeathsAndEndLabel> mapDeathsLabel = new();

        public static void ApplyHooks()
        {
            On.Menu.SlugcatSelectMenu.ctor += SlugcatSelectMenu_ctor;
            On.Menu.SlugcatSelectMenu.SetChecked += SlugcatSelectMenu_SetChecked;
            On.Menu.SlugcatSelectMenu.GetChecked += SlugcatSelectMenu_GetChecked;
            On.SaveState.GetStoryDenPosition += SaveState_GetStoryDenPosition;
            On.RoomCamera.MoveCamera_Room_int += RoomCamera_MoveCamera_Room_int;
            On.Menu.Menu.ctor += Menu_ctor;
            On.Menu.SlugcatSelectMenu.UpdateSelectedSlugcatInMiscProg += SlugcatSelectMenu_UpdateSelectedSlugcatInMiscProg;

            On.HUD.Map.Update += Map_Update;
            On.HUD.Map.Draw += Map_Draw;
            On.HUD.Map.ClearSprites += Map_ClearSprites;
        }

        private static void SlugcatSelectMenu_UpdateSelectedSlugcatInMiscProg(On.Menu.SlugcatSelectMenu.orig_UpdateSelectedSlugcatInMiscProg orig, SlugcatSelectMenu self)
        {
            orig(self);
            if (ssmRaceModeConfig.TryGetValue(self, out var raceModeConfig))
            {
                if (raceModeConfig.startingRoom != null)
                {
                    string randomShelter = SpeedrunRandomStart(self.manager.rainWorld.progression.miscProgressionData.currentlySelectedSinglePlayerSlugcat);
                    raceModeConfig.startingRoom.value = randomShelter;
                    raceModeConfig.startingRoom.description = $"Room where the player will spawn and time will begin ({RaceModeConfig.startingRoomValue})";
                }

                if (raceModeConfig.endingRoom != null)
                {
                    string randomEnd = SpeedrunRandomEnd(self.manager.rainWorld.progression.miscProgressionData.currentlySelectedSinglePlayerSlugcat);
                    raceModeConfig.endingRoom.value = randomEnd;
                    raceModeConfig.endingRoom.description = $"Room where the player will spawn and time will begin ({RaceModeConfig.endingRoomValue})";
                }
            }
        }

        private static void Map_ClearSprites(On.HUD.Map.orig_ClearSprites orig, Map self)
        {
            if (mapDeathsLabel.TryGetValue(self, out DeathsAndEndLabel existing))
            {
                existing.ClearSprites();
            }
            orig(self);
        }

        private static void Map_Draw(On.HUD.Map.orig_Draw orig, Map self, float timeStacker)
        {
            orig(self, timeStacker);
            if (mapDeathsLabel.TryGetValue(self, out DeathsAndEndLabel existing))
            {
                existing.Draw(timeStacker, Mathf.Lerp(self.lastFade, self.fade, timeStacker));
            }
        }

        private static void Map_Update(On.HUD.Map.orig_Update orig, Map self)
        {
            orig(self);
            if (self.mapLoaded)
            {
                if (self.hud?.owner is Player p && RaceModeConfig.raceMode)
                {
                    DeathsAndEndLabel label = mapDeathsLabel.GetValue(self, map => new DeathsAndEndLabel(map)); // getorcreatevalue requires empty constructor so I use getvalue here and pass the map since deathlabel needs it
                    label.Update();
                }
            }
        }

        private static void Menu_ctor(On.Menu.Menu.orig_ctor orig, Menu.Menu self, ProcessManager manager, ProcessManager.ProcessID ID)
        {
            orig(self, manager, ID);
            if (self is SlugcatSelectMenu)
            {
                self.infoLabel.y = Mathf.Max(0.01f + manager.rainWorld.options.SafeScreenOffset.y, 8.01f);
            }
        }

        private static void RoomCamera_MoveCamera_Room_int(On.RoomCamera.orig_MoveCamera_Room_int orig, RoomCamera self, Room newRoom, int camPos)
        {
            orig(self, newRoom, camPos);
            if (RaceModeConfig.raceMode && RaceModeConfig.endingRoomValue != null && newRoom?.abstractRoom?.name?.ToUpperInvariant() == RaceModeConfig.endingRoomValue)
            {
                RainWorld.lockGameTimer = true;
                if (self?.hud?.parts != null)
                {
                    foreach (HudPart part in self.hud.parts)
                    {
                        if (part is SpeedRunTimer s)
                        {
                            s.remainVisibleCounter = 200;
                        }
                    }
                }
            }
        }

        private static string SaveState_GetStoryDenPosition(On.SaveState.orig_GetStoryDenPosition orig, SlugcatStats.Name slugcat, out bool isVanilla)
        {
            string origRet = orig(slugcat, out isVanilla);
            if (RaceModeConfig.raceMode && !RaceModeConfig.startingRoomValue.IsNullOrWhiteSpace() && RainWorld.roomNameToIndex.ContainsKey(RaceModeConfig.startingRoomValue))
            {
                Plugin.Logger.LogInfo("Setting start room");
                RaceModeConfig.startRoomSet = true;
                return RaceModeConfig.startingRoomValue!;
            }
            return origRet;
        }

        private static bool SlugcatSelectMenu_GetChecked(On.Menu.SlugcatSelectMenu.orig_GetChecked orig, SlugcatSelectMenu self, CheckBox box)
        {
            if (box.IDString == "RACEMODE" && ssmRaceModeConfig.TryGetValue(self, out var raceModeConfig))
            {
                return RaceModeConfig.raceMode;
            }
            return orig(self, box);
        }

        private static void SlugcatSelectMenu_SetChecked(On.Menu.SlugcatSelectMenu.orig_SetChecked orig, SlugcatSelectMenu self, CheckBox box, bool c)
        {
            if (box.IDString != "RACEMODE")
            {
                orig(self, box, c);
                return;
            }
            if (ssmRaceModeConfig.TryGetValue(self, out var raceModeConfig))
            {
                RaceModeConfig.raceMode = c;
                raceModeConfig.UpdateRoomOptionVisiblity();
            }
        }

        private static void SlugcatSelectMenu_ctor(On.Menu.SlugcatSelectMenu.orig_ctor orig, SlugcatSelectMenu self, ProcessManager manager)
        {
            orig(self, manager);

            RaceModeConfig config = ssmRaceModeConfig.GetValue(self, menu => new RaceModeConfig(menu, menu.pages[0], new Vector2(menu.startButton.pos.x + 200f, 90f)));

            self.pages[0].subObjects.Add(config);
        }

        public static string SpeedrunRandomStart(SlugcatStats.Name slug)
        {
            Dictionary<string, int> dictionary = new Dictionary<string, int>();
            Dictionary<string, List<string>> dictionary2 = new Dictionary<string, List<string>>();
            List<string> list2 = SlugcatStats.SlugcatStoryRegions(slug);
            if (File.Exists(AssetManager.ResolveFilePath("speedrunrandomstarts.txt")))
            {
                string[] array = File.ReadAllLines(AssetManager.ResolveFilePath("speedrunrandomstarts.txt"));
                for (int i = 0; i < array.Length; i++)
                {
                    if (!array[i].StartsWith("//") && array[i].Length > 0)
                    {
                        string text = Regex.Split(array[i], "_")[0];
                        if (!dictionary2.ContainsKey(text))
                        {
                            dictionary2.Add(text, new List<string>());
                        }
                        if (list2.Contains(text))
                        {
                            dictionary2[text].Add(array[i]);
                        }
                        else if (ModManager.MSC && (slug == SlugcatStats.Name.White || slug == SlugcatStats.Name.Yellow))
                        {
                            if (text == "OE")
                            {
                                dictionary2[text].Add(array[i]);
                            }
                            if (text == "LC")
                            {
                                dictionary2[text].Add(array[i]);
                            }
                            if (text == "MS" && array[i] != "MS_S07")
                            {
                                dictionary2[text].Add(array[i]);
                            }
                        }
                        if (dictionary2[text].Contains(array[i]) && !dictionary.ContainsKey(text))
                        {
                            dictionary.Add(text, 1);
                        }
                    }
                }
                global::System.Random random = new global::System.Random();
                int num = dictionary.Values.Sum();
                int randomIndex = random.Next(0, num);
                string key = dictionary.First(delegate (KeyValuePair<string, int> x)
                {
                    randomIndex -= x.Value;
                    return randomIndex < 0;
                }).Key;
                int num2 = dictionary2.Values.Select((List<string> list) => list.Count).Sum();
                string text2 = dictionary2[key].ElementAt(global::UnityEngine.Random.Range(0, dictionary2[key].Count - 1));
                return text2;
            }
            return "SU_S01";
        }

        public static string SpeedrunRandomEnd(SlugcatStats.Name slug)
        {
            List<string> regions = SlugcatStats.SlugcatStoryRegions(slug);
            string region = regions[UnityEngine.Random.Range(0, regions.Count)];
            string roomsPath = AssetManager.ResolveDirectory("World" + Path.DirectorySeparatorChar + region + "-rooms");

            if (roomsPath == null) return "";

            string[] roomFiles = Directory.GetFiles(roomsPath, "*_settings.txt");

            if (roomFiles.Length == 0) return "";

            string randomFile = roomFiles[UnityEngine.Random.Range(0, roomFiles.Length)];
            string room = Path.GetFileNameWithoutExtension(randomFile);
            room = room.Substring(0, room.Length - "_settings".Length);

            return room;
        }
    }

    public class DeathsAndEndLabel
    {
        private Map owner;
        private FLabel deathsLabel;
        private FLabel endLabel;
        private float fade;
        private float lastFade;
        public int revealTimer;

        public DeathsAndEndLabel(Map owner)
        {
            this.owner = owner;
            this.deathsLabel = new FLabel(RWCustom.Custom.GetDisplayFont(), "");
            this.UpdateDeathsText();
            owner.container.AddChild(this.deathsLabel);
            this.deathsLabel.x = 1352.01f;
            this.deathsLabel.y = 754.1f;
            this.deathsLabel.alignment = FLabelAlignment.Right;
            this.deathsLabel.color = global::Menu.Menu.MenuRGB(global::Menu.Menu.MenuColors.MediumGrey);
            this.deathsLabel.alpha = 0f;

            this.endLabel = new FLabel(RWCustom.Custom.GetDisplayFont(), RaceModeConfig.endingRoomValue);
            owner.container.AddChild(this.endLabel);
            this.endLabel.x = 1352.01f;
            this.endLabel.y = 730.1f;
            this.endLabel.alignment = FLabelAlignment.Right;
            this.endLabel.color = global::Menu.Menu.MenuRGB(global::Menu.Menu.MenuColors.MediumGrey);
            this.endLabel.alpha = 0f;
        }

        public void UpdateDeathsText()
        {
            if (this.owner?.hud?.owner is Player p)
            {
                int num = p.abstractCreature.world.game.GetStorySession.saveState.deathPersistentSaveData.deaths;
                this.deathsLabel.text = this.owner.hud.rainWorld.inGameTranslator.Translate("Deaths") + ": " + num.ToString();
            }
        }

        public void Update()
        {
            this.lastFade = this.fade;
            this.revealTimer = RWCustom.Custom.IntClamp(this.revealTimer + ((this.owner.fade > 0.9f) ? 1 : ((this.owner.fade < 0.05f) ? (-20) : (-1))), 0, 120);
            this.fade = RWCustom.Custom.LerpAndTick(this.fade, (this.revealTimer > 30) ? 1f : 0f, 0.04f, 0.016666668f);
        }

        public void Draw(float timeStacker, float useAlpha)
        {
            this.deathsLabel.alpha = RWCustom.Custom.SCurve(useAlpha * Mathf.Lerp(this.lastFade, this.fade, timeStacker), 0.7f);
            this.endLabel.alpha = RWCustom.Custom.SCurve(useAlpha * Mathf.Lerp(this.lastFade, this.fade, timeStacker), 0.7f);
        }

        public void ClearSprites()
        {
            this.deathsLabel.RemoveFromContainer();
            this.endLabel.RemoveFromContainer();
        }
    }

    public class RaceModeConfig : PositionedMenuObject
    {
        public CheckBox? raceModeToggle;
        public OpTextBox? startingRoom;
        public MenuLabel? startingRoomLabel;
        public OpTextBox? endingRoom;
        public MenuLabel? endingRoomLabel;

        public static bool raceMode;
        public static bool startRoomSet = false;
        public static string? startingRoomValue;
        public static string? endingRoomValue;

        public MenuTabWrapper? tabWrapper;

        public RaceModeConfig(SlugcatSelectMenu menu, MenuObject owner, Vector2 pos) : base(menu, owner, pos)
        {
            tabWrapper = new MenuTabWrapper(menu, this);
            raceMode = true;
            subObjects.Add(tabWrapper);

            Vector2 raceModePos = Vector2.zero;
            raceModeToggle = new CheckBox(menu, this, menu, raceModePos, 65f, menu.Translate("Race mode"), "RACEMODE");
            subObjects.Add(raceModeToggle);

            string randomShelter = UI.SpeedrunRandomStart(menu.manager.rainWorld.progression.miscProgressionData.currentlySelectedSinglePlayerSlugcat).ToUpperInvariant();
            string randomEnd = UI.SpeedrunRandomEnd(menu.manager.rainWorld.progression.miscProgressionData.currentlySelectedSinglePlayerSlugcat).ToUpperInvariant();

            Vector2 startingRoomOffset = new Vector2(30f, 0f);
            startingRoom = new OpTextBox(new Configurable<string>(randomShelter), startingRoomOffset, 75f) { maxLength = 20 };
            startingRoomValue = randomShelter;
            startingRoom.alignment = FLabelAlignment.Center;
            startingRoom.allowSpace = false;
            startingRoom.description = $"Room where the player will spawn and time will begin ({startingRoomValue})";
            UIelementWrapper startingRoomWrapper = new UIelementWrapper(tabWrapper, startingRoom);
            startingRoom.OnValueUpdate += StartingRoom_OnValueUpdate;

            startingRoomLabel = new MenuLabel(menu, this, "Start room", startingRoomOffset + new Vector2(37f, 40f), Vector2.zero, false);
            subObjects.Add(startingRoomLabel);

            Vector2 endingRoomOffset = new Vector2(110f, 0f);
            endingRoom = new OpTextBox(new Configurable<string>(randomEnd), endingRoomOffset, 75f) { maxLength = 20 }; // hardcode for longest room name (ms_bitterunderground)
            endingRoomValue = randomEnd;
            endingRoom.alignment = FLabelAlignment.Center;
            endingRoom.allowSpace = false;
            endingRoom.description = $"Room where time will end ({endingRoomValue})";
            UIelementWrapper endingRoomWrapper = new UIelementWrapper(tabWrapper, endingRoom);
            endingRoom.OnValueUpdate += EndingRoom_OnValueUpdate;

            endingRoomLabel = new MenuLabel(menu, this, "End room", endingRoomOffset + new Vector2(37f, 40f), Vector2.zero, false);
            subObjects.Add(endingRoomLabel);

            UpdateRoomOptionVisiblity();
        }

        private void EndingRoom_OnValueUpdate(UIconfig config, string value, string oldValue)
        {
            if (config is OpTextBox otb)
            {
                otb.value = value.ToUpperInvariant();
                endingRoomValue = value.ToUpperInvariant();
                if (otb.value.Length > Mathf.FloorToInt((otb.size.x - 20f) / LabelTest.CharMean(false))) // in case text gets big, left align so it doesn't impede starting room
                {
                    otb.alignment = FLabelAlignment.Left;
                }
                else
                {
                    otb.alignment = FLabelAlignment.Center;
                }
                otb.description = $"Room where time will end ({endingRoomValue})";
            }
        }

        private void StartingRoom_OnValueUpdate(UIconfig config, string value, string oldValue)
        {
            if (config is OpTextBox otb)
            {
                otb.value = value.ToUpperInvariant();
                startingRoomValue = value.ToUpperInvariant();
                if (otb.value.Length > Mathf.FloorToInt((otb.size.x - 20f) / LabelTest.CharMean(false))) // unlikely to ever be long enough for this to matter but might as well
                {
                    otb.alignment = FLabelAlignment.Left;
                }
                else
                {
                    otb.alignment = FLabelAlignment.Center;
                }
                otb.description = $"Room where the player will spawn and time will begin ({startingRoomValue})";
            }
        }

        public void UpdateRoomOptionVisiblity()
        {
            if (startingRoom is not null && endingRoom is not null)
            {
                startingRoom.greyedOut = endingRoom.greyedOut = !raceMode;
            }
        }
    }
}
