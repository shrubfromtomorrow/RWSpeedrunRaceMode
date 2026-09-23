using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using UnityEngine;
using static MonoMod.InlineRT.MonoModRule;
using static SpeedrunRaceMode.Helpers;

namespace SpeedrunRaceMode
{
    public static class DeathSave
    {
        public static void ApplyHooks()
        {
            // Causes of death. General prevention + specific instances
            On.Player.Die += Player_Die; // General
            On.Creature.Violence += Creature_Violence; // General that also prevents stun
            On.Player.Destroy += Player_Destroy; // General for those methods that kill and then destroy the player
            On.Player.Update += Player_Update; // Danger grasps
            IL.Creature.Update += Creature_Update_DeathPlane; // Death plane
            IL.Creature.SuckedIntoShortCut += Creature_SuckedIntoShortCut; // Den suck
            On.WormGrass.WormGrassPatch.InteractWithCreature += WormGrassPatch_InteractWithCreature; // Wormgrass
            IL.BigEel.JawsSnap += BigEel_JawsSnap; // Leviathan
            On.DaddyLongLegs.Eat += DaddyLongLegs_Eat; // Dll
            On.DaddyCorruption.Update += DaddyCorruption_Update; // protorot
            On.Watcher.Loach.Eat += Loach_Eat; // loach

            IL.LocustSystem.Swarm.Update += Swarm_Update; // Locusts disband and unleech
            On.BigNeedleWorm.AttachToChunk += BigNeedleWorm_AttachToChunk; // Detach noodlefly
            // Cosmetic hooks, not saving player
            On.Spear.HitSomething += Spear_HitSomething; // Make spears drop where player gets hit
            IL.Lizard.Bite += Lizard_Bite; // Prevent red lizards from forcing item drops
        }

        private static void Lizard_Bite(ILContext il)
        {
            try
            {
                ILCursor c = new ILCursor(il);
                if (!c.TryGotoNext(MoveType.After, x => x.MatchLdsfld(typeof(CreatureTemplate.Type), nameof(CreatureTemplate.Type.RedLizard))))
                {
                    Plugin.Logger.LogInfo("Lizard_Bite failed to match CreatureTemplate.Type.RedLizard");
                    return;
                }
                c.Index++;

                c.Emit(OpCodes.Ldarg_1);
                c.EmitDelegate<Func<bool, BodyChunk, bool>>((origRet, chunk) =>
                {
                    if (!origRet) return origRet;
                    if (chunk.owner is Player p && p.AI == null)
                    {
                        return false;
                    }
                    return origRet;
                });

            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"Lizard_Bite threw an exception: {ex}");
            }
        }

        private static bool Spear_HitSomething(On.Spear.orig_HitSomething orig, Spear self, SharedPhysics.CollisionResult result, bool eu)
        {
            Vector2 pos = self.firstChunk.pos;
            bool origRet = orig(self, result, eu);

            if (result.obj is Player)
            {
                self.PulledOutOfStuckObject();
                self.ChangeMode(Weapon.Mode.Free);
                self.firstChunk.HardSetPosition(pos);
                self.firstChunk.vel = Vector2.zero;
                self.SetRandomSpin();
            }
            return origRet;
        }


        private static void DaddyCorruption_Update(On.DaddyCorruption.orig_Update orig, DaddyCorruption self, bool eu)
        {
            for (int i = self.eatCreatures.Count - 1; i >= 0; i--)
            {
                if (self.eatCreatures[i].creature is Player p && Helpers.SaveCheck(p) && self.eatCreatures[i].wait > 12000f)
                {
                    foreach (DaddyCorruption.Bulb bulb2 in self.allBulbs)
                    {
                        if (bulb2.leg?.grabChunk?.owner is Player p2 && Helpers.SaveCheck(p))
                        {
                            bulb2.leg.grabChunk = null;
                        }
                    }
                    self.eatCreatures.RemoveAt(i);
                    Plugin.Logger.LogInfo("Saving player from daddycorruption");
                    Helpers.MarkPlayerToSave(p);
                }
            }
            orig(self, eu);
        }

        private static void BigNeedleWorm_AttachToChunk(On.BigNeedleWorm.orig_AttachToChunk orig, BigNeedleWorm self, bool rot)
        {
            if (self.impaleChunk?.owner is Player p && Helpers.SaveCheck(p))
            {
                self.impaleChunk = null;
                return;
            }
            orig(self, rot);
        }

        private static void Swarm_Update(ILContext il)
        {
            try
            {
                ILCursor c = new ILCursor(il);
                if (!c.TryGotoNext(MoveType.After, x => x.MatchStfld(typeof(Creature), nameof(Creature.leechedOut))))
                {
                    Plugin.Logger.LogInfo("Swarm_Update failed to match Creature.leechedOut");
                    return;
                }
                c.Emit(OpCodes.Ldarg_0);
                c.EmitDelegate((LocustSystem.Swarm locustSystem) =>
                {
                    if (locustSystem.target is Player p && Helpers.SaveCheck(p))
                    {
                        p.leechedOut = false;
                        locustSystem.Disband();
                    }
                });
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"Swarm_Update threw an exception: {ex}");
            }
        }

        private static void Player_Destroy(On.Player.orig_Destroy orig, Player self)
        {
            if (Helpers.SaveCheck(self))
            {
                Plugin.Logger.LogInfo("Player destroyed");
                Helpers.MarkPlayerToSave(self);
                return;
            }
            orig(self);
        }

        private static void Loach_Eat(On.Watcher.Loach.orig_Eat orig, Watcher.Loach self, bool eu)
        {
            for (int i = self.eatObjects.Count - 1; i >= 0; i--)
            {
                if (self.eatObjects[i].chunk.owner is Player p && Helpers.SaveCheck(p))
                {
                    Plugin.Logger.LogInfo("Player about to be eaten by loach");
                    Helpers.MarkPlayerToSave(p);
                    self.AI.tracker.ForgetCreature(p.abstractCreature);
                    self.eatObjects.RemoveAt(i);
                    self.digestingCounter = 0;
                    for (int j = 0; j < self.tentacle.Length; j++)
                    {
                        self.tentacle[j].SwitchTask(Watcher.LoachTentacle.Task.SearchGround);
                    }
                    return;
                }
            }
            orig(self, eu);
        }

        private static void DaddyLongLegs_Eat(On.DaddyLongLegs.orig_Eat orig, DaddyLongLegs self, bool eu)
        {
            for (int i = self.eatObjects.Count - 1; i >= 0; i--)
            {
                if (self.eatObjects[i].chunk.owner is Player p && Helpers.SaveCheck(p))
                {
                    Plugin.Logger.LogInfo("Player about to be eaten by DLL");
                    Helpers.MarkPlayerToSave(p);
                    self.AI.tracker.ForgetCreature(p.abstractCreature);
                    self.eatObjects.RemoveAt(i);
                    self.Stun(40);
                    self.digestingCounter = 0;
                    for (int j = 0; j < self.tentacles.Length; j++)
                    {
                        self.tentacles[j].neededForLocomotion = true;
                        self.tentacles[j].SwitchTask(DaddyTentacle.Task.Locomotion);
                        self.tentacles[j].grabChunk = null;
                    }
                    return;
                }
            }
            orig(self, eu);
        }

        private static void BigEel_JawsSnap(ILContext il)
        {
            try
            {
                ILCursor c = new ILCursor(il);
                if (!c.TryGotoNext(MoveType.Before, x => x.MatchRet()))
                {
                    Plugin.Logger.LogInfo("BigEel_JawsSnap failed to match ret");
                    return;
                }
                ILLabel skip = c.MarkLabel();

                ILCursor d = new ILCursor(il);
                if (!d.TryGotoNext(MoveType.After, x => x.MatchCallOrCallvirt(typeof(Creature), nameof(Creature.Die))))
                {
                    Plugin.Logger.LogInfo("BigEel_JawsSnap failed to match creature.die");
                    return;
                }
                d.Emit(OpCodes.Ldarg_0);
                d.EmitDelegate<Func<BigEel, bool>>((bigSal) =>
                {
                    BigEel.ClampedObject obj = bigSal.clampedObjects[bigSal.clampedObjects.Count - 1];
                    if (obj?.chunk?.owner is Player)
                    {
                        bigSal.clampedObjects.RemoveAll(x => x?.chunk?.owner is Player p && Helpers.SaveCheck(p)); // for some fucking reason the player gets put on here more than once
                        return true;
                    }
                    return false;
                });
                d.Emit(OpCodes.Brtrue, skip);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"BigEel_JawsSnap threw an exception: {ex}");
            }
        }

        private static void WormGrassPatch_InteractWithCreature(On.WormGrass.WormGrassPatch.orig_InteractWithCreature orig, WormGrass.WormGrassPatch self, WormGrass.WormGrassPatch.CreatureAndPull creatureAndPull)
        {
            if (creatureAndPull.creature is Player p && creatureAndPull.bury > 0f && Helpers.SaveCheck(p))
            {
                Plugin.Logger.LogInfo("Player wormgrassed");
                creatureAndPull.bury = 0f;
                self.LoseGrip(creatureAndPull);
                Helpers.MarkPlayerToSave(p);
                return;
            }
            orig(self, creatureAndPull);
        }

        private static void Creature_Violence(On.Creature.orig_Violence orig, Creature self, BodyChunk source, Vector2? directionAndMomentum, BodyChunk hitChunk, PhysicalObject.Appendage.Pos hitAppendage, Creature.DamageType type, float damage, float stunBonus)
        {
            if (self is Player p && Helpers.SaveCheck(p) && damage >= p.Template.instantDeathDamageLimit)
            {
                Plugin.Logger.LogInfo("Player violenced");
                Helpers.MarkPlayerToSave(p);
                return;
            }
            orig(self, source, directionAndMomentum, hitChunk, hitAppendage, type, damage, stunBonus);
        }

        private static void Player_Update(On.Player.orig_Update orig, Player self, bool eu)
        {
            if (self.dangerGrasp != null && self.dangerGraspTime > 28 && Helpers.SaveCheck(self))
            {
                Plugin.Logger.LogInfo("Saving player from dangergrasp");
                Helpers.FreePlayer(self);
                Helpers.MarkPlayerToSave(self);
            }
            orig(self, eu);
            if (playerDataTable.TryGetValue(self, out CWTs.PlayerData playerData) && playerData.saveMeNextTick)
            {
                Helpers.SendPlayerToShorcut(self);
                playerData.saveMeNextTick = false;
            }
        }

        private static void Creature_SuckedIntoShortCut(ILContext il)
        {
            try
            {
                ILCursor c = new ILCursor(il);
                if (!c.TryGotoNext(MoveType.After, x => x.MatchCallOrCallvirt(typeof(Room).GetMethod(nameof(Room.RemoveObject)))))
                {
                    Plugin.Logger.LogInfo("Creature_SuckedIntoShortCut failed to match Room.RemoveObject");
                    return;
                }

                ILLabel skip = c.MarkLabel();

                ILCursor d = new ILCursor(il);

                int creatureLocal = -1;

                if (!d.TryGotoNext(MoveType.Before, x => x.MatchLdloc(out creatureLocal), x => x.MatchLdcI4(1)))
                {
                    Plugin.Logger.LogInfo("Creature_SuckedIntoShortCut failed to match ldloc and matchldci4");
                    return;
                }
                d.Emit(OpCodes.Ldloc, creatureLocal);
                d.Emit(OpCodes.Ldarg_0);
                d.EmitDelegate((Creature prey, Creature predator) =>
                {
                    if (prey is Player p && Helpers.SaveCheck(p)) // pups can die hooray
                    {
                        Plugin.Logger.LogInfo("Passed the savecheck");
                        AbstractCreature abstractPredator = predator.abstractCreature;
                        for (int i = abstractPredator.stuckObjects.Count - 1; i >= 0; i--) // this is done in reverse order because I stole it from AbstractCreature.IsEnteringDen
                        {
                            AbstractPhysicalObject.AbstractObjectStick predatorAOS = abstractPredator.stuckObjects[i]; // I don't know what any of the next few lines even means lmao
                            if (i < abstractPredator.stuckObjects.Count && predatorAOS is AbstractPhysicalObject.CreatureGripStick preyCGS && predatorAOS.A == abstractPredator)
                            {
                                if (predatorAOS.B is AbstractCreature preyAgain && preyAgain.realizedCreature is Player)
                                {
                                    Plugin.Logger.LogInfo("Saving player in Creature_SuckedIntoShortCut");
                                    abstractPredator.DropCarriedObject(preyCGS.grasp); // Drop the player (this doesn't interrupt their denning code so they denrest normally)
                                    Helpers.MarkPlayerToSave(p);
                                    return true;
                                }
                            }
                        }
                    }
                    return false;
                });
                d.Emit(OpCodes.Brtrue, skip); // Skip if the player is saved
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"Creature_SuckedIntoShortCut threw an exception: {ex}");
            }
        }

        private static void Player_Die(On.Player.orig_Die orig, Player self)
        {
            //StackTrace st = new StackTrace();
            //Plugin.Logger.LogInfo("Player died: " + st);
            if (Helpers.SaveCheck(self))
            {
                Helpers.MarkPlayerToSave(self);
                Plugin.Logger.LogInfo("Player died, saving");
                return;
            }
            Plugin.Logger.LogInfo("Player died, not saving");
            orig(self);
        }

        private static void Creature_Update_DeathPlane(MonoMod.Cil.ILContext il)
        {
            try
            {
                ILCursor c = new ILCursor(il);
                if (!c.TryGotoNext(MoveType.After, x => x.MatchCallOrCallvirt(typeof(Player), nameof(Player.PermaDie)))) // unique match in update right after jolly coop death call (dgaf about jolly)
                {
                    Plugin.Logger.LogInfo("Creature_Update_DeathPlane failed to match Player.PermaDie");
                    return;
                }
                if (!c.TryGotoNext(MoveType.Before, x => x.MatchCallOrCallvirt(typeof(Creature), nameof(Creature.Die)))) // go to before the actual die because MoveType.After on the jolly thing apparently is still in that scope
                {
                    Plugin.Logger.LogInfo("Creature_Update_DeathPlane failed to match Creature.Die");
                    return;
                }

                ILCursor end = new ILCursor(c);

                if (!end.TryGotoNext(MoveType.After, x => x.MatchCallOrCallvirt(typeof(AbstractWorldEntity), nameof(AbstractWorldEntity.Destroy)))) // use a second cursor to find the point after the death call and destruction
                {
                    Plugin.Logger.LogInfo("Creature_Update_DeathPlane failed to match AbstractWorldEntity.Destroy");
                    return;
                }

                ILLabel skipDeath = end.MarkLabel(); // mark the point after death and destruction

                c.EmitDelegate<Func<Creature, bool>>(creature => // consumes the already existing ldarg.0 (creature)
                {
                    if (creature is Player p && Helpers.SaveCheck(p))
                    {
                        Helpers.MarkPlayerToSave(p);
                        return true;
                    }
                    return false;
                });
                c.Emit(OpCodes.Brtrue, skipDeath); // skip to end cursor if delegate returns true
                c.Emit(OpCodes.Ldarg_0); // put the ldarg.0 (creature) back for normal killing if all returns false
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"Creature_Update_DeathPlane threw an exception: {ex}");
            }
        }
    }
}