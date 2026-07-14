using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using ECommons;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using Microsoft.Extensions.Logging;
using Questionable.Controller.Steps.Interactions;
using Questionable.Controller.Steps.Movement;
using Questionable.Domain;
using Questionable.External;
using Questionable.Functions;
using Questionable.Model.Questing;
using Serilog.Core;
using DEnums = Dalamud.Game.ClientState.Objects.Enums;

namespace Questionable.Controller.Steps.Common;

internal static class Mail
{
    internal sealed class Factory(MogmailIpc mogmailIpc, IObjectTable objectTable, Configuration configuration, ILogger<Mail.Factory> logger) : ITaskFactory
    {
        public IEnumerable<ITask> CreateAllTasks(Questionable.Domain.Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.InteractionType != EInteractionType.AcceptQuest)
                yield break;

            logger.LogDebug($"ClaimMail: {configuration.General.ClaimMail} / MogmailIpc.IsInstalled: {mogmailIpc.IsInstalled} / LetterCount: {MogmailIpc.LetterCount ?? 0}");
            if (!configuration.General.ClaimMail)
                yield break;
            if (!mogmailIpc.IsInstalled)
                yield break;
            if (MogmailIpc.LetterCount == 0)
                yield break;

            var objList = FindDeliveryMoogle(objectTable);
            if (objList.TryGetFirst(out IGameObject? moogle) && moogle != null)
            {
                logger.LogDebug("Moving to moogle");
                yield return new MoveTask(step, moogle.Position);
                yield return new ClaimMailTask(moogle);
            }
        }
    }

    internal sealed record ClaimMailTask(IGameObject GameObject) : ITask
    {
        public bool ShouldRedoOnInterrupt() => true;
        public override string ToString() => "ClaimMail()";
    }

    internal sealed class ClaimMailExecutor(ITargetManager targetManager, MogmailIpc mogmailIpc) : TaskExecutor<ClaimMailTask>
    {
        protected unsafe override bool Start()
        {
            targetManager.Target = null;
            targetManager.Target = Task.GameObject;
            var result = (long)TargetSystem.Instance()->InteractWithObject((GameObject*)Task.GameObject.Address, checkLineOfSight: false);
            mogmailIpc.ClaimAll();
            CloseMail();
            return true;
        }

        public override ETaskResult Update()
        {
            if (mogmailIpc.IsBusy)
                mogmailIpc.ClaimAll();
            return ETaskResult.StillRunning;
        }

        public override bool ShouldInterruptOnDamage() => false;
    }

    internal static IEnumerable<IGameObject> FindDeliveryMoogle(IObjectTable objectTable)
    {
        var sheet = Svc.Data.Excel.GetSheet<ENpcBase>();
        foreach (IGameObject? obj in objectTable)
        {
            if (sheet.TryGetRow(obj.BaseId, out var enpcsheet))
                if (enpcsheet.ENpcData.Any(x => x.RowId == 720898))
                    yield return obj;
        }
    }

    private static unsafe bool CloseMail()
    {
        AgentLetter* agentLetter = AgentLetter.Instance();
        AgentLetterView* agentLetterView = AgentLetterView.Instance();
        if (agentLetter == null)
            return false;
        if (agentLetterView != null && agentLetterView->IsAgentActive())
        {
            uint addonId = agentLetterView->GetAddonId();
            if (addonId != 0)
            {
                AtkUnitBase* addon = AtkStage.Instance()->RaptureAtkUnitManager->GetAddonById((ushort)addonId);
                if (addon != null)
                {
                    addon->FireCallbackInt(-1);
                }
            }
        }
        if (agentLetter != null && agentLetter->IsAgentActive())
        {
            uint addonId = agentLetter->GetAddonId();
            if (addonId != 0)
            {
                AtkUnitBase* addon = AtkStage.Instance()->RaptureAtkUnitManager->GetAddonById((ushort)addonId);
                if (addon != null)
                {
                    addon->FireCallbackInt(-1);
                }
            }
        }
        return true;
    }
}
