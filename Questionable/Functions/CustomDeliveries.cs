using Dalamud.Hooking;
using Dalamud.Game;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using GameAchievement = FFXIVClientStructs.FFXIV.Client.Game.UI.Achievement;
using UIState = FFXIVClientStructs.FFXIV.Client.Game.UI.UIState;
using GameFramework = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework;
namespace Questionable.Functions;

// The request, bonus and achievement maths are from Satisfy by veyn, used with permission.
[RegisterSingleton]
internal sealed unsafe class CustomDeliveries : IDisposable
{
    private readonly IDataManager _dataManager;
    private readonly IClientState _clientState;
    private readonly IFramework _framework;
    private readonly ILogger<CustomDeliveries> _logger;
    private readonly Hook<GameAchievement.Delegates.ReceiveAchievementProgress> _achievementHook;

    // Achievement progress isn't sent while delivering, so keep the count from before this week's deliveries.
    private readonly Dictionary<uint, (uint Start, uint Max)> _achievements = [];
    private Dictionary<uint, uint>? _achievementIds;
    private readonly HashSet<uint> _asked = [];

    public CustomDeliveries(IDataManager dataManager, IClientState clientState, IFramework framework,
        IGameInteropProvider gameInteropProvider, ILogger<CustomDeliveries> logger)
    {
        _dataManager = dataManager;
        _clientState = clientState;
        _framework = framework;
        _logger = logger;
        _achievementHook = gameInteropProvider.HookFromAddress<GameAchievement.Delegates.ReceiveAchievementProgress>(
            GameAchievement.Addresses.ReceiveAchievementProgress.Value, OnAchievementProgress);
        _achievementHook.Enable();
        _clientState.Logout += OnLogout;
    }

    public void Dispose()
    {
        _clientState.Logout -= OnLogout;
        IpcInvoke.TryOnFrameworkThread(_framework, () => _achievementHook.Dispose(), _logger);
    }

    // The 12 weekly deliveries are shared by every NPC.
    public static int WeeklyLeft => SatisfactionSupplyManager.Instance()->GetRemainingAllowances();

    internal static int TurnInsLeft(int left, int current, int needed, int perTurnIn) =>
        needed > current && perTurnIn > 0
            ? Math.Min(left, (int)Math.Ceiling((needed - current) / (float)perTurnIn))
            : left;

    // Also asks the server for achievement progress it doesn't have yet, one per call.
    public List<DeliveryNpc> GetNpcs()
    {
        SatisfactionSupplyManager* inst = SatisfactionSupplyManager.Instance();
        SubrowExcelSheet<SatisfactionSupply> supplies = _dataManager.GetSubrowExcelSheet<SatisfactionSupply>();
        ExcelSheet<SatisfactionSupplyReward> rewards = _dataManager.GetExcelSheet<SatisfactionSupplyReward>();
        int bonusRow = inst->BonusGuaranteeRowId != 0xFF ? inst->BonusGuaranteeRowId : BonusGuaranteeRow(inst);
        SatisfactionBonusGuarantee? bonus = bonusRow >= 0
            ? _dataManager.GetExcelSheet<SatisfactionBonusGuarantee>().GetRowOrDefault((uint)bonusRow)
            : null;

        List<DeliveryNpc> result = [];
        bool loading = false;
        foreach (SatisfactionNpc row in _dataManager.GetExcelSheet<SatisfactionNpc>().Where(x => x.RowId > 0 && x.Npc.RowId > 0))
        {
            int index = (int)row.RowId - 1;
            if (index >= inst->SatisfactionRanks.Length)
                continue;

            int rank = inst->SatisfactionRanks[index];
            DeliveryRequest[] requests = [];
            if (rank > 0)
            {
                uint supplyIndex = (uint)row.SatisfactionNpcParams[rank].SupplyIndex;
                SubrowCollection<SatisfactionSupply> subrows = supplies.GetRow(supplyIndex);
                uint[] picked = RequestedItems(subrows, supplyIndex, inst->SupplySeed);
                byte id = (byte)row.RowId;
                bool[] forced = rank == 5 && bonus is { } b
                    ? [b.BonusDoH.Contains(id), b.BonusDoL.Contains(id), b.BonusFisher.Contains(id)]
                    : [false, false, false];
                requests = new DeliveryRequest[picked.Length];
                for (int slot = 0; slot < picked.Length; slot++)
                {
                    if (picked[slot] >= subrows.Count)
                        continue;
                    SatisfactionSupply supply = subrows[(int)picked[slot]];
                    // A guaranteed bonus swaps in the bonus item of the same slot.
                    if (forced[slot] && !supply.IsBonus)
                        supply = subrows.FirstOrDefault(x => x.Slot == supply.Slot && x.IsBonus, supply);
                    requests[slot] = new(supply.Item.RowId, forced[slot] || supply.IsBonus,
                        rewards.GetRowOrDefault(supply.Reward.RowId)?.SatisfactionHigh ?? 0);
                }
            }

            int used = inst->UsedAllowances[index];
            uint achievementId = AchievementIds().GetValueOrDefault(row.RowId);
            if (!_achievements.TryGetValue(achievementId, out var achievement) && achievementId != 0 && rank > 0)
            {
                if (!_asked.Contains(achievementId) && RequestAchievement(achievementId))
                    _asked.Add(achievementId);
                loading |= !_asked.Contains(achievementId) || IsRequesting;
            }

            result.Add(new(row.RowId, row.Npc.Value.Singular.ToString(),
                QuestManager.IsQuestComplete(row.QuestRequired.RowId), rank, inst->Satisfaction[index],
                row.SatisfactionNpcParams[rank].SatisfactionRequired, used, row.DeliveriesPerWeek,
                Math.Min(achievement.Start + (uint)used, achievement.Max), achievement.Max, requests));
        }

        Loading = loading;
        return result;
    }

    public bool Loading { get; private set; }

    private static bool IsRequesting =>
        UIState.Instance()->Achievement.ProgressRequestState == GameAchievement.AchievementState.Requested;

    private static bool RequestAchievement(uint id)
    {
        UIState* ui = UIState.Instance();
        if (!ui->PlayerState.IsLoaded || ui->Achievement.ProgressRequestState == GameAchievement.AchievementState.Requested)
            return false;
        ui->Achievement.RequestAchievementProgress(id);
        return true;
    }

    private void OnAchievementProgress(GameAchievement* self, uint id, uint current, uint max)
    {
        _achievementHook.Original(self, id, current, max);
        foreach ((uint npc, uint achievementId) in AchievementIds())
        {
            if (achievementId != id)
                continue;

            SatisfactionSupplyManager* inst = SatisfactionSupplyManager.Instance();
            int index = (int)npc - 1;
            uint used = index < inst->UsedAllowances.Length ? inst->UsedAllowances[index] : 0u;
            _achievements[id] = (current > used ? current - used : 0, max);
            return;
        }
    }

    // The sheets don't link an NPC to its 150 deliveries achievement; matched like Satisfy's clib does.
    private Dictionary<uint, uint> AchievementIds()
    {
        if (_achievementIds != null)
            return _achievementIds;

        ExcelSheet<ENpcResident> residents = _dataManager.GetExcelSheet<ENpcResident>(ClientLanguage.English);
        List<Achievement> achievements = _dataManager.GetExcelSheet<Achievement>(ClientLanguage.English)
            .Where(x => x.AchievementCategory.RowId == 22 && x.AchievementTarget.RowId == 46 && x.Data.Count > 0 && x.Data[0].RowId == 150)
            .ToList();
        _achievementIds = [];
        foreach (SatisfactionNpc row in _dataManager.GetExcelSheet<SatisfactionNpc>())
        {
            string name = residents.GetRowOrDefault(row.Npc.RowId)?.Singular.ToString() ?? "";
            if (row.RowId == 0 || name.Length == 0)
                continue;

            foreach (Achievement achievement in achievements)
            {
                if (achievement.Description.ToString().Contains(name, StringComparison.OrdinalIgnoreCase))
                {
                    _achievementIds[row.RowId] = achievement.RowId;
                    break;
                }
            }
        }

        return _achievementIds;
    }

    private void OnLogout(int type, int code)
    {
        _achievements.Clear();
        _asked.Clear();
    }

    // See SatisfactionSupplyManager.setCurrentNpc; the bonus rotates weekly.
    private int BonusGuaranteeRow(SatisfactionSupplyManager* inst)
    {
        GameFramework* framework = GameFramework.Instance();
        var proxy = framework->IsNetworkModuleInitialized ? framework->NetworkModuleProxy : null;
        var module = proxy != null ? proxy->NetworkModule : null;
        if (module == null)
            return -1;

        // GetCurrentDeviceTime; the offset isn't in ClientStructs yet.
        int timestamp = *(int*)((nint)module + 0xB54) + inst->TimeAdjustmentForBonusGuarantee;
        return (timestamp - 1657008000) / 604800 % _dataManager.GetExcelSheet<SatisfactionBonusGuarantee>().Count;
    }

    // See SatisfactionSupplyManager.onSatisfactionSupplyRead: picks one request per slot from the weekly seed.
    internal static uint[] RequestedItems(SubrowCollection<SatisfactionSupply> subrows, uint supplyIndex, uint seed)
    {
        uint h1 = (0x03CEA65Cu * supplyIndex) ^ (0x1A0DD20Eu * seed);
        uint h2 = (0xDF585D5Du * supplyIndex) ^ (0x3057656Eu * seed);
        uint h3 = (0xED69E442u * supplyIndex) ^ (0x2202EA5Au * seed);
        uint h4 = (0xAEFC3901u * supplyIndex) ^ (0xE70723F6u * seed);
        uint[] res = [0xFFFFFFFF, 0xFFFFFFFF, 0xFFFFFFFF];
        uint h5 = h1;
        for (int slot = 1; slot < 4; ++slot)
        {
            int sum = 0;
            for (int i = 0; i < subrows.Count; ++i)
                if (subrows[i].Slot == slot)
                    sum += subrows[i].ProbabilityPercent;

            uint temp = h5 ^ (h5 << 11);
            h1 = h3;
            h3 = h4;
            h5 = h2;
            h4 ^= temp ^ ((temp ^ (h4 >> 11)) >> 8);
            h2 = h1;
            if (sum == 0)
                continue;

            long roll = h4 % sum;
            for (int i = 0; i < subrows.Count; ++i)
            {
                if (subrows[i].Slot != slot)
                    continue;
                if (roll < subrows[i].ProbabilityPercent)
                {
                    res[slot - 1] = (uint)i;
                    break;
                }

                roll -= subrows[i].ProbabilityPercent;
            }
        }

        return res;
    }
}

internal readonly record struct DeliveryRequest(uint ItemId, bool Bonus, int Satisfaction);

internal sealed record DeliveryNpc(uint RowId, string Name, bool Unlocked, int Rank, int SatisfactionCur,
    int SatisfactionMax, int Used, int Max, uint AchievementCur, uint AchievementMax, DeliveryRequest[] Requests)
{
    public int TurnInsLeft(Configuration.EDeliveryItem item) =>
        CustomDeliveries.TurnInsLeft(Max - Used, SatisfactionCur, SatisfactionMax,
            Requests.Length > (int)item ? Requests[(int)item].Satisfaction : 0);
}
