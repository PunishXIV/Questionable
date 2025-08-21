using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using Microsoft.Extensions.Logging;
using Questionable.Controller.Steps.Common;
using Questionable.Model;
using Questionable.Model.Questing;

namespace Questionable.Controller.Steps.Interactions;

internal static class Dive
{
    internal sealed class Factory : SimpleTaskFactory
    {
        public override ITask? CreateTask(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.InteractionType != EInteractionType.Dive)
                return null;

            return new Task();
        }
    }

    internal sealed class Task : ITask
    {

        public override string ToString() => "Dive";
    }

    internal sealed class DoDive(ICondition condition, ILogger<DoDive> logger)
        : AbstractDelayedTaskExecutor<Task>(TimeSpan.FromSeconds(5))
    {
        private readonly Queue<(uint Type, nint Key)> _keysToPress = [];
        private int _attempts;

        protected override bool StartInternal()
        {
            if (condition[ConditionFlag.Diving])
                return false;

            if (condition[ConditionFlag.Mounted] || condition[ConditionFlag.Swimming])
            {
                Descend();
                return true;
            }

            throw new TaskException("You aren't swimming, so we can't dive.");
        }

        public override unsafe ETaskResult Update()
        {
            if (_keysToPress.TryDequeue(out var definition))
            {
                if (definition.Type == 0)
                    return ETaskResult.StillRunning;

                logger.LogDebug("{Action} key {KeyCode:X2}",
                    definition.Type == NativeMethods.WM_KEYDOWN ? "Pressing" : "Releasing", definition.Key);
                NativeMethods.SendMessage((nint)Device.Instance()->hWnd, definition.Type, definition.Key, nint.Zero);
                return ETaskResult.StillRunning;
            }

            return base.Update();
        }

        public override bool ShouldInterruptOnDamage() => false;

        protected override ETaskResult UpdateInternal()
        {
            if (condition[ConditionFlag.Diving])
                return ETaskResult.TaskComplete;

            if (_attempts >= 3)
                throw new TaskException("Please dive manually.");

            Descend();
            _attempts++;
            return ETaskResult.StillRunning;
        }

        private unsafe void Descend()
        {
            var keybind = new UIInputData.Keybind();
            var keyName = Utf8String.FromString("MOVE_DESCENT");
            var inputData = UIInputData.Instance();
            inputData->GetKeybind(keyName, &keybind);

            logger.LogInformation("Dive keybind: {Key1} + {Modifier1}, {Key2} + {Modifier2}", keybind.Key,
                keybind.Modifier, keybind.AltKey, keybind.AltModifier);

            // find the shortest of the two key combinations to press
            List<List<nint>?> availableKeys =
                [GetKeysToPress(keybind.Key, keybind.Modifier), GetKeysToPress(keybind.AltKey, keybind.AltModifier)];
            List<nint>? realKeys = availableKeys.Where(x => x != null).Select(x => x!).MinBy(x => x.Count);
            if (realKeys == null || realKeys.Count == 0)
                throw new TaskException("No useable keybind found for diving");

            foreach (var key in realKeys)
            {
                _keysToPress.Enqueue((NativeMethods.WM_KEYDOWN, key));
                _keysToPress.Enqueue((0, 0));
                _keysToPress.Enqueue((0, 0));
            }

            for (int i = 0; i < 5; ++i)
                _keysToPress.Enqueue((0, 0)); // do nothing

            realKeys.Reverse();
            foreach (var key in realKeys)
                _keysToPress.Enqueue((NativeMethods.WM_KEYUP, key));
        }
    }

    private static List<nint>? GetKeysToPress(SeVirtualKey key, ModifierFlag modifier)
    {
        List<nint> keys = [];
        if (modifier.HasFlag(ModifierFlag.Ctrl))
            keys.Add(0x11); // VK_CONTROL
        if (modifier.HasFlag(ModifierFlag.Shift))
            keys.Add(0x10); // VK_SHIFT
        if (modifier.HasFlag(ModifierFlag.Alt))
            keys.Add(0x12); // VK_MENU

        nint mappedKey = (nint)key;
        if (mappedKey == 0)
            return null;

        keys.Add(mappedKey);
        return keys;
    }

    private static class NativeMethods
    {
        public const uint WM_KEYUP = 0x101;
        public const uint WM_KEYDOWN = 0x100;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, nint wParam, nint lParam);
    }
}
