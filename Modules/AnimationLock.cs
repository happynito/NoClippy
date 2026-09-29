#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Dalamud.Bindings.ImGui;
using static NoClippy.NoClippy;

namespace NoClippy
{
    public partial class Configuration
    {
        public bool EnableAnimLockComp = true;
        public bool EnableLogging = false;
        public bool EnableDryRun = false;
        public Dictionary<uint, float> AnimationLocks = new();
        public ulong TotalActionsReduced = 0ul;
        public double TotalAnimationLockReduction = 0d;
    }
}

namespace NoClippy.Modules
{
    public unsafe class AnimationLock : Module
    {
        public override bool IsEnabled
        {
            get => Config.EnableAnimLockComp;
            set => Config.EnableAnimLockComp = value;
        }

        public override int DrawOrder => 1;

        private const float SimulatedRtt = 0.000f; // 0 ms
        private readonly Dictionary<ushort, float> appliedAnimationLocks = new();

        private delegate bool UseActionDelegate(
            ActionManager* actionManager,
            ActionType actionType,
            uint actionID,
            ulong targetID,
            uint extraParam,
            ActionManager.UseActionMode queueState,
            uint comboRouteID,
            bool* outOptAreaTargeted
        );

        private Hook<UseActionDelegate>? useActionHook;

        private delegate bool UseActionLocationDelegate(
            ActionManager* manager,
            ActionType type,
            uint actionID,
            ulong targetID,
            Vector3* location,
            uint extraParam,
            byte a7
        );

        private Hook<UseActionLocationDelegate>? useActionLocationHook;

        public bool IsDryRunEnabled => Config.EnableDryRun;

        public override void Enable()
        {
            useActionHook ??= DalamudApi.GameInteropProvider.HookFromAddress<UseActionDelegate>(
                (nint)ActionManager.MemberFunctionPointers.UseAction,
                UseActionDetour
            );

            useActionLocationHook ??= DalamudApi.GameInteropProvider.HookFromAddress<UseActionLocationDelegate>(
                (nint)ActionManager.MemberFunctionPointers.UseActionLocation,
                UseActionLocationDetour
            );

            useActionHook.Enable();
            useActionLocationHook.Enable();
        }

        public override void Disable()
        {
            useActionHook?.Dispose();
            useActionHook = null;

            useActionLocationHook?.Dispose();
            useActionLocationHook = null;
        }

        private bool UseActionDetour(
            ActionManager* actionManager,
            ActionType actionType,
            uint actionID,
            ulong targetID,
            uint extraParam,
            ActionManager.UseActionMode queueState,
            uint comboRouteID,
            bool* outOptAreaTargeted
        )
        {
            try
            {
                if (!IsDryRunEnabled && Game.actionManager != null)
                {
                    Game.actionManager->animationLock = 0.0f;
                    appliedAnimationLocks[Game.actionManager->currentSequence] = 0.0f;
                }

                if (Config.EnableLogging)
                {
                    var prefix = IsDryRunEnabled ? "[DRY RUN] " : "";
                    DalamudApi.LogDebug($"{prefix}[NoClippy] UseAction voor {actionType} ID {actionID}. Lock gereset naar 0.");
                }
            }
            catch (Exception ex)
            {
                DalamudApi.LogError($"Fout in UseActionDetour: {ex.Message}");
            }

            return useActionHook!.Original(actionManager, actionType, actionID, targetID, extraParam, queueState, comboRouteID, outOptAreaTargeted);
        }

        private bool UseActionLocationDetour(
            ActionManager* manager,
            ActionType type,
            uint actionID,
            ulong targetID,
            Vector3* location,
            uint extraParam,
            byte a7
        )
        {
            try
            {
                if (!IsDryRunEnabled && Game.actionManager != null)
                {
                    Game.actionManager->animationLock = 0.0f;
                }

                if (Config.EnableLogging)
                {
                    var prefix = IsDryRunEnabled ? "[DRY RUN] " : "";
                    DalamudApi.LogDebug($"{prefix}[NoClippy] UseActionLocation voor ID {actionID}.");
                }
            }
            catch (Exception ex)
            {
                DalamudApi.LogError($"Fout in UseActionLocationDetour: {ex.Message}");
            }

            return useActionLocationHook!.Original(manager, type, actionID, targetID, location, extraParam, a7);
        }

        public override void DrawConfig()
        {
            if (ImGui.Checkbox("Enable Animation Lock Reduction", ref Config.EnableAnimLockComp))
                Config.Save();
            PluginUI.SetItemTooltip("Modifies the way the game handles animation lock," +
                "\nsimulating 5 ms ping.");

            if (Config.EnableAnimLockComp)
            {
                ImGui.Columns(2, "AnimlockColumns", false);

                if (ImGui.Checkbox("Enable Logging", ref Config.EnableLogging))
                    Config.Save();

                ImGui.NextColumn();

                var dryRun = IsDryRunEnabled;
                if (ImGui.Checkbox("Dry Run", ref dryRun))
                {
                    Config.EnableDryRun = dryRun;
                    Config.Save();
                }
                PluginUI.SetItemTooltip("The plugin will still log and perform calculations, but no in-game values will be overwritten.");
            }

            ImGui.Columns(1);
            ImGui.TextUnformatted($"Reduced a total time of {TimeSpan.FromSeconds(Config.TotalAnimationLockReduction):d\\:hh\\:mm\\:ss} from {Config.TotalActionsReduced} actions");
        }
    }
}