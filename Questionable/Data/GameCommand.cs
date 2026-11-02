namespace Questionable.Data;

/// <summary>
/// Command ids for <see cref="FFXIVClientStructs.FFXIV.Client.Game.GameMain.ExecuteCommand"/>
/// </summary>
// https://github.com/AtmoOmen/OmenTools/blob/main/Infos/ExecuteCommandFlag.cs
// https://github.com/Jaksuhn/clib/blob/main/Enums/CommandFlag.cs
public enum GameCommand
{
    None = 0,

    /// <summary>
    /// Abandon quest
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Quest ID (not RowID)</para>
    /// </remarks>
    AbandonQuest = 800,

    /// <summary>
    /// Send solo quest battle request
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Difficulty (0 - Normal, 1 - Easy, 2 - Very Easy)</para>
    /// </remarks>
    StartSoloQuestBattle = 823,

    /// <summary>
    /// New Game+ mode
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: New Game+ chapter index in QuestRedoChapter.csv (0 - Exit New Game+)</para>
    /// </remarks>
    QuestRedo = 824,
}
