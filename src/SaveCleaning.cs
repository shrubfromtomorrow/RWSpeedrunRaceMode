using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Expedition;
using MonoMod.Cil;
using Mono.Cecil.Cil;
using static MoreSlugcats.MSCRoomSpecificScript;
using static RoomSpecificScript;
using static Watcher.WatcherRoomSpecificScript;
using BepInEx;

namespace SpeedrunRaceMode
{
    public static class SaveCleaning
    {
        public static void ApplyHooks()
        {
            On.Room.Loaded += Room_Loaded; // clean tutorials and intros
            IL.Menu.SlugcatSelectMenu.StartGame += SlugcatSelectMenu_StartGame; // skip cutscenes
            On.StoryGameSession.ctor += StoryGameSession_ctor; // wintro skip
        }

        private static void StoryGameSession_ctor(On.StoryGameSession.orig_ctor orig, StoryGameSession self, SlugcatStats.Name saveStateNumber, RainWorldGame game)
        {
            orig(self, saveStateNumber, game);
            if (!ModManager.Watcher || saveStateNumber != Watcher.WatcherEnums.SlugcatStatsName.Watcher || !RaceModeConfig.raceMode || 
                !RaceModeConfig.startRoomSet) return; // only do intro skip stuff if unknown starting room

            if (self.game.manager.menuSetup.startGameCondition == ProcessManager.MenuSetup.StoryGameInitCondition.New)
            {
                // The vegetables
                self.saveState.deathPersistentSaveData.minimumRippleLevel = 1f;
                self.saveState.deathPersistentSaveData.rippleLevel = 1f;
                self.saveState.deathPersistentSaveData.maximumRippleLevel = 1f;

                self.saveState.miscWorldSaveData.hasSkippedFirstWarpFatigueTransfer = 1;
            }
        }

        private static void SlugcatSelectMenu_StartGame(MonoMod.Cil.ILContext il)
        {
            try
            {
                ILCursor c = new ILCursor(il);

                if (c.TryGotoNext(MoveType.After, x => x.MatchCallOrCallvirt(typeof(RWInput), nameof(RWInput.CheckSpecificButton))))
                {
                    ILLabel skipDelegate = c.DefineLabel();

                    c.Emit(OpCodes.Dup);

                    c.Emit(OpCodes.Brtrue_S, skipDelegate);

                    c.Emit(OpCodes.Pop);

                    c.EmitDelegate<Func<bool>>(() =>
                    {
                        return RaceModeConfig.raceMode;
                    });

                    c.MarkLabel(skipDelegate);
                }
                else
                {
                    Plugin.Logger.LogError("SlugcatSelectMenu_StartGame failed to match: " + il);
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"SlugcatSelectMenu_StartGame threw an exception: {ex}");
            }
        }

        private static void Room_Loaded(On.Room.orig_Loaded orig, Room self)
        {
            orig(self);
            if (RaceModeConfig.raceMode && self.updateList != null && self.updateList.Count > 0)
            {
                for (int i = 0; i < self.updateList.Count; i++)
                {
                    if (RaceModeUndesirableRoomScript(self.updateList[i]))
                    {
                        self.updateList[i].Destroy();
                    }
                }
            } 
        }

        public static bool RaceModeUndesirableRoomScript(UpdatableAndDeletable item) // most of these won't hit because of cycle 0 protection but better safe than sorry. Let intros run if empty or unknown starting room
        {
            return (item is SU_C04StartUp && RaceModeConfig.startRoomSet) || item is SU_A23FirstCycleMessage || (item is SU_A43SuperJumpOnly && RaceModeConfig.startRoomSet) || item is LF_A03 ||
                item is GW_C05ArtificerMessage || (item is SpearmasterGateLocation && RaceModeConfig.startRoomSet) || item is SU_SMIntroMessage || item is SU_A42Message || 
                (item is SI_SAINTINTRO_tut && !RaceModeConfig.startRoomSet) || item is SI_C02_tut || item is InvSpawnLocation || item is DS_RIVSTARTcutscene || (item is SH_GOR02 && RaceModeConfig.startRoomSet) || 
                (item is HI_W14 && RaceModeConfig.startRoomSet);
        }
    }
}
