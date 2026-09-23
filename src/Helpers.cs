using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Watcher;
using static SpeedrunRaceMode.CWTs;

namespace SpeedrunRaceMode
{
    public static class Helpers
    {
        public static ConditionalWeakTable<Player, PlayerData> playerDataTable = new();
        public static void ApplyHooks()
        {
            On.Creature.SpitOutOfShortCut += Creature_SpitOutOfShortCut; // track node player should come from
        }

        private static void Creature_SpitOutOfShortCut(On.Creature.orig_SpitOutOfShortCut orig, Creature self, RWCustom.IntVector2 pos, Room newRoom, bool spitOutAllSticks)
        {
            orig(self, pos, newRoom, spitOutAllSticks);
            if (self is Player p && p.AI == null && newRoom.shortcutData(pos).shortCutType == ShortcutData.Type.RoomExit)
            {
                PlayerData data = playerDataTable.GetOrCreateValue(p);
                data.destNode = newRoom.shortcutData(pos).destNode; // when player is spit out of shortcut, this destNode will be the node in the room they came from
            }
        }

        public static bool SaveCheck(Player p) // only pass if player is not a pup
        {
            if (p != null && p.AI == null)
            {
                bool globalDeathRain = false;
                bool roomElectricDeath = false;
                bool roomFlooding = false;
                bool playerRainDeath = false;
                bool playerHypothermiaDeath = false;
                bool isLCFinalRoom = false;
                bool isHoldingMercy = false;
                if (p.room != null)
                {
                    Room room = p.room;
                    globalDeathRain = room.roomRain?.globalRain?.deathRain != null;
                    roomElectricDeath = room.updateList.Any(uad => uad is ElectricDeath ed && ed.Intensity > 0.5f && (!ModManager.PrecycleModule || ed.cycle.preTimer <= 0));
                    roomFlooding = room.waterObject != null && room.roomRain != null && room.roomRain.FloodLevel > room.waterObject.originalWaterLevel;

                    playerRainDeath = p.rainDeath > 0f;
                    playerHypothermiaDeath = p.Hypothermia >= 1f;

                    isLCFinalRoom = RaceModeConfig.raceMode && ModManager.MSC && p.slugcatStats.name == MoreSlugcats.MoreSlugcatsEnums.SlugcatStatsName.Artificer && p.room.abstractRoom.name.ToUpperInvariant() == "LC_FINAL";

                    isHoldingMercy = Input.GetKey(Plugin.PluginInstance.srmOptions.mercyButton.Value);

                    Plugin.Logger.LogInfo("Global deathrain: " + globalDeathRain);
                    Plugin.Logger.LogInfo("electricDeath: " + roomElectricDeath);
                    Plugin.Logger.LogInfo("roomFlooding: " + roomFlooding);
                    Plugin.Logger.LogInfo("playerRainDeath: " + playerRainDeath);
                    Plugin.Logger.LogInfo("playerHypothermiaDeath: " + playerHypothermiaDeath);
                    Plugin.Logger.LogInfo("isLCFinalRoom: " + isLCFinalRoom);
                    Plugin.Logger.LogInfo("isHoldingMercy: " + isHoldingMercy);
                }
                bool anyDeath = isHoldingMercy || playerRainDeath || playerHypothermiaDeath || (globalDeathRain && (roomElectricDeath || roomFlooding)) || isLCFinalRoom;
                Plugin.Logger.LogInfo("Any death: " + anyDeath);
                return !anyDeath; // kill player if they die with a raindeath over 0 or if hypothermia death or when there is a global deathrain and the room has electricdeath or is flooding or it's LC final in racemode as Arti
            }
            Plugin.Logger.LogInfo("Player is null somehow");
            return false;
        }

        public static void ResetPlayerState(Player p)
        {
            p.lastStun = 0;
            p.stun = 0;
            p.saintWeakness = 0;
            p.exhausted = false;
            p.airInLungs = 1f;
            p.injectedPoison = 0f;
            p.leechedOut = false;
            p.CollideWithTerrain = true;
            p.collisionLayer = 1;
            p.rippleDeathTime = 0;
            p.rippleDeathIntensity = 0f;

            p.pyroJumpCooldown = 0f;
            p.pyroJumpCounter = 0;

            p.playerState.permanentDamageTracking = 0f;

            if (p.room?.updateList != null)
            {
                List<CreatureSpasmer> playerSpasmers = p.room.updateList.OfType<CreatureSpasmer>().Where(spasmer => spasmer.crit is Player player && player.AI == null).ToList();
                List<PoisonInjecter> poisonInjecters = p.room.updateList.OfType<PoisonInjecter>().Where(poisoner => poisoner.crit is Player player && player.AI == null).ToList();

                foreach (var spasmer in playerSpasmers)
                {
                    Plugin.Logger.LogInfo("Removing spasmer");
                    p.room.RemoveObject(spasmer);
                }
                foreach (var poisonInjecter in poisonInjecters)
                {
                    Plugin.Logger.LogInfo("Removing poisonInjecter");
                    p.room.RemoveObject(poisonInjecter);
                }
            }
        }

        public static void FreePlayer(Player p)
        {
            if (p.grabbedBy.Count > 0)
            {
                foreach (Creature.Grasp grasp in p.grabbedBy.ToList()) // copy because .Release() modifies collection
                {
                    Plugin.Logger.LogInfo("Releasing grasp");
                    grasp.Release();
                }
            }
        }

        public static void MarkPlayerToSave(Player p)
        {
            PlayerData data = playerDataTable.GetOrCreateValue(p);
            data.saveMeNextTick = true;
        }

        public static void SendPlayerToShorcut(Player p)
        {
            if (playerDataTable.TryGetValue(p, out CWTs.PlayerData playerData) && playerData.destNode != -1)
            {
                Room room = p.room;
                FreePlayer(p);
                ResetPlayerState(p);
                if (room.game?.GetStorySession?.saveState?.deathPersistentSaveData != null)
                {
                    room.game.GetStorySession.saveState.deathPersistentSaveData.deaths++;
                    room.game.GetStorySession.saveState.deathPersistentSaveData.AddDeathPosition(room.abstractRoom.index, p.mainBodyChunk.pos);
                    if (room?.game?.cameras[0]?.hud?.map != null && UI.mapDeathsLabel.TryGetValue(room.game.cameras[0].hud.map, out DeathsAndEndLabel label))
                    {
                        label.UpdateDeathsText();
                    }
                }


                p.enteringShortCut = null;
                p.bodyMode = Player.BodyModeIndex.CorridorClimb;
                p.canJump = 0; // needed for buffers
                List<AbstractPhysicalObject> allConnectedObjects = p.abstractCreature.GetAllConnectedObjects();
                for (int i = 0; i < allConnectedObjects.Count; i++)
                {
                    if (allConnectedObjects[i].realizedObject != null)
                    {
                        if (allConnectedObjects[i].realizedObject is Creature creature)
                        {
                            creature.inShortcut = true;
                            creature.inShortcutVessel = p.inShortcutVessel;
                        }
                        room.RemoveObject(allConnectedObjects[i].realizedObject);
                    }
                }
                ShortcutHandler.ShortCutVessel playerVessel = new ShortcutHandler.ShortCutVessel(new RWCustom.IntVector2(0, 0), p, room.abstractRoom, 0);
                playerVessel.entranceNode = playerData.destNode; // entrance node is set to the node the player entered in the previous room. Shortcuthandler picks up which node they should re-enter room from
                room.game.shortcuts.betweenRoomsWaitingLobby.Add(playerVessel);
                room.PlaySound(SoundID.UI_Multiplayer_Player_Revive);
            }
            else
            {
                if (p.room != null && p.karmaFlowerGrowPos.HasValue)
                {
                    Plugin.Logger.LogInfo("No shortcut found! Hardsetting to karmaflowerpos");
                    Helpers.GoodHardSet(p, p.room.MiddleOfTile(p.karmaFlowerGrowPos.Value)); // ideally use player karma flower pos
                }
                else
                {
                    Plugin.Logger.LogInfo("No shortcut found! Hardsetting to middle of first screen");
                    Helpers.GoodHardSet(p, new Vector2(638f, 381f)); // backup (middle of screen)
                }
            }
        }

        public static void GoodHardSet(Player p, Vector2 pos)
        {
            List<Vector2> relativeChunkPositions = new List<Vector2>();
            for (int i = 0; i < p.bodyChunks.Length; i++)
            {
                if (i == 0)
                {
                    relativeChunkPositions.Add(p.bodyChunks[i].pos);
                }
                else
                {
                    relativeChunkPositions.Add(p.bodyChunks[i].pos - relativeChunkPositions[0]);
                }
            }
            p.SuperHardSetPosition(pos + new Vector2(0f, 20f));
            foreach (BodyChunk c in p.bodyChunks)
            {
                c.vel = Vector2.zero;
            }

            for (int i = 0; i < p.bodyChunks.Length; i++)
            {
                if (i != 0)
                {
                    p.bodyChunks[i].pos = p.bodyChunks[0].pos + relativeChunkPositions[i];
                }
            }
        }
    }
}
