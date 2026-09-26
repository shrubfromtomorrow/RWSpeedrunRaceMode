using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
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

        /// <summary>
        /// Determine if player should be saved from death. Various checks for end of cycle vetos as well as mercy button or if the player is a pup
        /// </summary>
        /// <param name="p"></param>
        /// <returns></returns>
        public static bool SaveCheck(Player p)
        {
            if (p != null && !p.dead && p.AI == null)
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

        /// <summary>
        /// Reset the player state broadly
        /// </summary>
        /// <param name="p"></param>
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

        /// <summary>
        /// Free the player from grasps
        /// </summary>
        /// <param name="p"></param>
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

        /// <summary>
        /// Set player to be saved on the next tick (allow an update tick to run before sending to shortcut to avoid issues)
        /// </summary>
        /// <param name="p"></param>
        public static void MarkPlayerToSave(Player p)
        {
            PlayerData data = playerDataTable.GetOrCreateValue(p);
            data.saveMeNextTick = true;
        }

        /// <summary>
        /// Remove player from room, send them to their most recent shortcut entrance. If there is no shortcut entrance stored for the player, send them to their karma flower growth position, if that is not 
        /// present, send them to the middle of the first screen of the room
        /// </summary>
        /// <param name="p"></param>
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
                        room!.RemoveObject(allConnectedObjects[i].realizedObject);
                    }
                }
                ShortcutHandler.ShortCutVessel playerVessel = new ShortcutHandler.ShortCutVessel(new RWCustom.IntVector2(0, 0), p, room.abstractRoom, 0);
                playerVessel.entranceNode = playerData.destNode; // entrance node is set to the node the player entered in the previous room. Shortcuthandler picks up which node they should re-enter room from
                room.game!.shortcuts.betweenRoomsWaitingLobby.Add(playerVessel);
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

        /// <summary>
        /// Set player without setting all bodychunks to the same pos (avoid flinging)
        /// </summary>
        /// <param name="p"></param>
        /// <param name="pos"></param>
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

        /// <summary>
        /// Get available random starting shelters for race mode. Templated from ExpeditionGame.ExpeditionRandomStarts
        /// </summary>
        /// <param name="slug"></param>
        /// <returns></returns>
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
                            if (ModManager.MSC || (text != "CC_S06" && text != "CC_S07" && text != "GW_S09" && text != "SH_S11" && text != "SI_S06" && text != "SB_S10")) // DLC shelters in vanilla regions
                            {
                                dictionary2.Add(text, new List<string>());
                            }
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

        /// <summary>
        /// Get a random room from a random slugcat story region for race mode ending
        /// </summary>
        /// <param name="slug"></param>
        /// <returns></returns>
        public static string SpeedrunRandomEnd(SlugcatStats.Name slug)
        {
            List<string> regions = SlugcatStats.SlugcatStoryRegions(slug);
            string region = regions[UnityEngine.Random.Range(0, regions.Count)];
            string regionWorldPath = AssetManager.ResolveDirectory("World" + Path.DirectorySeparatorChar + region);

            if (regionWorldPath == null) return "";

            string mapFile = AssetManager.ResolveFilePath("World" + Path.DirectorySeparatorChar + region + Path.DirectorySeparatorChar + $"map_{region}-{slug.value.ToLowerInvariant()}.txt");

            if (!File.Exists(mapFile))
            {
                mapFile = AssetManager.ResolveFilePath("World" + Path.DirectorySeparatorChar + region + Path.DirectorySeparatorChar + $"map_{region}.txt");
                if (!File.Exists(mapFile))
                {
                    Plugin.Logger.LogInfo("Something went mega wrong for " + mapFile);
                    return "";
                }
            }

            List<string> rooms = new();

            foreach (string line in File.ReadAllLines(mapFile))
            {
                if (line.StartsWith("Connection:", StringComparison.OrdinalIgnoreCase))
                {
                    string[] parts = line.Substring("Connection:".Length).Split(',');

                    rooms.Add(parts[0].Trim());
                }
            }

            if (rooms.Count == 0) return "";

            return rooms[UnityEngine.Random.Range(0, rooms.Count)];
        }
    }
}
