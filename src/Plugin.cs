using System.Collections.Generic;
using System.Security.Permissions;
using BepInEx;
using BepInEx.Logging;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using RWCustom;
using UnityEngine;

// Allows access to private members
#pragma warning disable CS0618
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]
#pragma warning restore CS0618

namespace SpeedrunRaceMode;

[BepInPlugin(ID, NAME, VERSION)]
sealed class Plugin : BaseUnityPlugin
{
    public const string VERSION = "1.0.0";
    public const string ID = "shrub.speedrunracemode";
    public const string NAME = "Speedrun Race Mode";

    public new static ManualLogSource Logger = null!;
    private static bool IsInit;
    public static Plugin PluginInstance = null!;

    public Options srmOptions = null!;

    public void OnEnable()
    {
        Logger = base.Logger;
        PluginInstance = this;
        srmOptions = new Options();
        On.RainWorld.OnModsInit += OnModsInit;
    }

    private void OnModsInit(On.RainWorld.orig_OnModsInit orig, RainWorld self)
    {
        orig(self);

        if (IsInit) return;
        IsInit = true;

        DeathSave.ApplyHooks();
        UI.ApplyHooks();
        Helpers.ApplyHooks();
        SaveCleaning.ApplyHooks();
        MachineConnector.SetRegisteredOI(ID, PluginInstance.srmOptions);
    }

    public void Update()
    {
        //if (Input.anyKeyDown && Input.GetKeyDown(KeyCode.Alpha7))
        //{
        //    RainWorldGame? game = Custom.rainWorld?.processManager?.currentMainLoop as RainWorldGame;
        //    if (game?.FirstAlivePlayer?.realizedCreature is Player p)
        //    {
        //        //Plugin.Logger.LogInfo("Killing you");
        //        //Helpers.SavePlayerShortcut(p);
        //        //game.FirstAlivePlayer.realizedCreature.Die();
        //    }
        //}
    }
}
