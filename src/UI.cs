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
using RWCustom;

namespace SpeedrunRaceMode
{
    public static class UI
    {
        public static ConditionalWeakTable<SlugcatSelectMenu, RaceModeConfig> ssmRaceModeConfig = new();
        public static ConditionalWeakTable<HUD.Map, DeathsAndEndLabel> mapDeathsLabel = new();

        public static void ApplyHooks()
        {
            On.Menu.SlugcatSelectMenu.ctor += SlugcatSelectMenu_ctor; // add racemode config
            On.Menu.SlugcatSelectMenu.SetChecked += SlugcatSelectMenu_SetChecked; // properly handle race mode checkbox
            On.Menu.SlugcatSelectMenu.GetChecked += SlugcatSelectMenu_GetChecked; // properly handle race mode checkbox
            On.SaveState.GetStoryDenPosition += SaveState_GetStoryDenPosition; // set den position on new save
            On.RoomCamera.MoveCamera_Room_int += RoomCamera_MoveCamera_Room_int; // end timer when reaching ending room
            On.Menu.Menu.ctor += Menu_ctor; // move infolabel (ui element descriptions) down on slugcat select menu so it doesn't interfere with holdbutton
            On.Menu.SlugcatSelectMenu.UpdateSelectedSlugcatInMiscProg += SlugcatSelectMenu_UpdateSelectedSlugcatInMiscProg; // reroll race mode stuffs on slugcat change in select menu

            On.HUD.Map.Update += Map_Update; // add or update death and end label
            On.HUD.Map.Draw += Map_Draw; // draw death and end label
            On.HUD.Map.ClearSprites += Map_ClearSprites; // clear death and end label
        }

        private static void SlugcatSelectMenu_UpdateSelectedSlugcatInMiscProg(On.Menu.SlugcatSelectMenu.orig_UpdateSelectedSlugcatInMiscProg orig, SlugcatSelectMenu self)
        {
            orig(self);
            if (ssmRaceModeConfig.TryGetValue(self, out var raceModeConfig))
            {
                if (raceModeConfig.startingRoom != null)
                {
                    string randomShelter = Helpers.SpeedrunRandomStart(self.manager.rainWorld.progression.miscProgressionData.currentlySelectedSinglePlayerSlugcat);
                    raceModeConfig.startingRoom.value = randomShelter;
                    raceModeConfig.startingRoom.description = $"Room where the player will spawn and time will begin ({RaceModeConfig.startingRoomValue})";
                }

                if (raceModeConfig.endingRoom != null)
                {
                    string randomEnd = Helpers.SpeedrunRandomEnd(self.manager.rainWorld.progression.miscProgressionData.currentlySelectedSinglePlayerSlugcat);
                    raceModeConfig.endingRoom.value = randomEnd;
                    raceModeConfig.endingRoom.description = $"Room where the player will spawn and time will begin ({RaceModeConfig.endingRoomValue})";
                }

                if (raceModeConfig.clearTime != null)
                {
                    raceModeConfig.clearTime.greyedOut = !self.manager.rainWorld.progression.IsThereASavedGame(self.manager.rainWorld.progression.miscProgressionData.currentlySelectedSinglePlayerSlugcat);
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
        private CheckBox? raceModeToggle;
        public OpTextBox? startingRoom;
        private MenuLabel? startingRoomLabel;
        public OpTextBox? endingRoom;
        private MenuLabel? endingRoomLabel;

        public OpHoldButton? clearTime;

        private SlugcatSelectMenu? ssm;

        public static bool raceMode;
        public static bool startRoomSet = false;
        public static string? startingRoomValue;
        public static string? endingRoomValue;

        public MenuTabWrapper? tabWrapper;

        public RaceModeConfig(SlugcatSelectMenu menu, MenuObject owner, Vector2 pos) : base(menu, owner, pos)
        {
            ssm = menu;
            tabWrapper = new MenuTabWrapper(menu, this);
            raceMode = true;
            subObjects.Add(tabWrapper);

            Vector2 raceModePos = Vector2.zero;
            raceModeToggle = new CheckBox(menu, this, menu, raceModePos, 65f, menu.Translate("Race mode"), "RACEMODE");
            subObjects.Add(raceModeToggle);

            string randomShelter = Helpers.SpeedrunRandomStart(menu.manager.rainWorld.progression.miscProgressionData.currentlySelectedSinglePlayerSlugcat).ToUpperInvariant();
            string randomEnd = Helpers.SpeedrunRandomEnd(menu.manager.rainWorld.progression.miscProgressionData.currentlySelectedSinglePlayerSlugcat).ToUpperInvariant();

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

            clearTime = new OpHoldButton(new Vector2(-67f, 30f), new Vector2(90f, 28f), "Clear Time", 28f) { description = "Hold to set speedrun timer to 0" };
            UIelementWrapper clearTimeWrapper = new UIelementWrapper(tabWrapper, clearTime);
            clearTime.OnPressDone += ClearTime_OnPressDone;
            clearTime.greyedOut = !ssm.manager.rainWorld.progression.IsThereASavedGame(ssm.manager.rainWorld.progression.miscProgressionData.currentlySelectedSinglePlayerSlugcat);
            subObjects.Add(clearTimeWrapper);

            UpdateRoomOptionVisiblity();
        }

        private void ClearTime_OnPressDone(UIfocusable trigger)
        {
            if (ssm != null)
            {
                SlugcatStats.Name slugname = ssm.manager.rainWorld.progression.miscProgressionData.currentlySelectedSinglePlayerSlugcat;
                int index = ssm.slugcatColorOrder.IndexOf(slugname);

                if (ssm.slugcatPages[index] is SlugcatSelectMenu.SlugcatPageContinue spc)
                {
                    if (SpeedRunTimer.GetCampaignTimeTracker(slugname) != null)
                    {
                        SpeedRunTimer.CampaignTimeTracker tracker = new();
                        ssm.manager.rainWorld.progression.miscProgressionData.campaignTimers.Remove(slugname.value);
                        ssm.manager.rainWorld.progression.miscProgressionData.campaignTimers.Add(slugname.value, tracker);

                        string labelCurrent = spc.regionLabel.text;

                        string labelNew = Regex.Replace(labelCurrent, @"\([^)]*\)", $"({tracker.TotalFreeTimeSpan.GetIGTFormat(MMF.cfgSpeedrunTimer.Value || menu.manager.rainWorld.options.validation)})");

                        spc.regionLabel.text = labelNew;
                    }
                }
            }
        }

        public override void Singal(MenuObject sender, string message)
        {
            if (message == "CLEARTIME" && ssm != null)
            {
                SlugcatStats.Name slugname = ssm.manager.rainWorld.progression.miscProgressionData.currentlySelectedSinglePlayerSlugcat;
                int index = ssm.slugcatColorOrder.IndexOf(slugname);

                if (ssm.slugcatPages[index] is SlugcatSelectMenu.SlugcatPageContinue spc)
                {
                    if (SpeedRunTimer.GetCampaignTimeTracker(slugname) != null)
                    {
                        SpeedRunTimer.CampaignTimeTracker tracker = new();
                        ssm.manager.rainWorld.progression.miscProgressionData.campaignTimers.Remove(slugname.value);
                        ssm.manager.rainWorld.progression.miscProgressionData.campaignTimers.Add(slugname.value, tracker);

                        string labelCurrent = spc.regionLabel.text;

                        string labelNew = Regex.Replace(labelCurrent, @"\([^)]*\)", $"({tracker.TotalFreeTimeSpan.GetIGTFormat(MMF.cfgSpeedrunTimer.Value || menu.manager.rainWorld.options.validation)})");

                        spc.regionLabel.text = labelNew;
                    }
                }
            }
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
