// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @Deckerz

using Lumina.Excel.Sheets;
using Questionable.AutoGen.Analysis;
using Questionable.AutoGen.Lua;
using Questionable.Controller.Steps.Common;
using Questionable.Model.Questing;
using Quest = Lumina.Excel.Sheets.Quest;

namespace Questionable.AutoGen.Generation;

/// <summary>
///     Produces a first-draft questpath for a quest by combining the <c>Quest</c> sheet
///     (issuer, todo locations, script symbol table) with what the quest's compiled Lua script says about
///     which actor belongs to which sequence.
///     <para>
///         The output is a starting point for a human path author, not a finished path: positions come from
///         <c>Level</c> rows rather than from walking the route, and anything the sheets and script cannot
///         express (dialogue choices, duty specifics, quest-variable gating, whether flying is unlocked yet)
///         is left out. Every step carries a <c>$</c> dev comment recording where it was derived from.
///     </para>
/// </summary>
public sealed class QuestPathAutoGenerator(QuestGameData gameData, string author)
{
    private readonly Dictionary<QuestStep, string> _provenance = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    ///     <c>ENEMY*</c> symbols the script never mentions in a sequence branch. Hunt objectives ("kill ten
    ///     little ladybugs") often work this way: the enemy is declared but the Lua has nothing to say about it.
    ///     Kept so the first sequence that only has a search area to offer can claim them.
    /// </summary>
    private readonly List<string> _unattributedEnemies = [];

    public QuestPathResult Generate(Quest quest)
    {
        _provenance.Clear();

        QuestSymbols symbols = QuestSymbols.From(quest);
        LuaProto? script = gameData.LoadScript(quest);
        QuestScriptAnalysis analysis = script != null
            ? QuestScriptAnalysis.Analyze(script, symbols)
            : QuestScriptAnalysis.Empty();

        List<string> notes = [];
        if (!analysis.HasScript)
            notes.Add("No Lua script found; sequence targets come from the Quest sheet alone.");

        HashSet<string> attributed =
        [
            .. analysis.Hints.Values.SelectMany(x => x.EnemyPairs).Select(x => x.Enemy)
        ];
        _unattributedEnemies.Clear();
        _unattributedEnemies.AddRange(symbols.All.Keys
            .Where(QuestSymbols.IsEnemySymbol)
            .Where(x => !attributed.Contains(x))
            .Order(StringComparer.Ordinal));

        List<QuestSequence> sequences = [BuildAcceptSequence(quest)];
        foreach (byte sequence in CollectSequences(quest, symbols, analysis))
        {
            QuestSequence? seq = BuildSequence(quest, symbols, analysis, sequence);
            if (seq != null)
                sequences.Add(seq);
        }
        sequences.Add(BuildCompleteSequence(quest, symbols, analysis));
        ApplyEventItemUses(quest, symbols, analysis, sequences);

        if (ApplyTravel(sequences))
        {
            notes.Add("Some zones had no reachable aetheryte or aethernet shard; add those shortcuts by hand.");
        }

        int emptySequences = sequences.Count(x => x.Steps.Count == 0);
        if (emptySequences > 0)
            notes.Add($"{emptySequences} sequence(s) had no derivable target and were written without steps.");

        QuestRoot root = new()
        {
            Author = [author],
            QuestSequence = sequences
        };

        return new QuestPathResult(quest, root, notes, new Dictionary<QuestStep, string>(_provenance,
            ReferenceEqualityComparer.Instance));
    }

    /// <summary>Sequences between accept and turn-in, from the script's <c>SEQ_*</c> symbols and the todo list.</summary>
    private static List<byte> CollectSequences(Quest quest, QuestSymbols symbols, QuestScriptAnalysis analysis)
    {
        SortedSet<byte> sequences = [];

        foreach (string symbol in symbols.All.Keys)
        {
            if (symbols.SequenceValue(symbol) is { } value and > 0 and < 255)
                sequences.Add(value);
        }

        foreach (var todo in quest.TodoParams)
        {
            if (todo.ToDoCompleteSeq is > 0 and < 255)
                sequences.Add(todo.ToDoCompleteSeq);
        }

        foreach (byte sequence in analysis.Hints.Keys)
        {
            if (sequence is > 0 and < 255)
                sequences.Add(sequence);
        }

        return [.. sequences];
    }

    private QuestSequence BuildAcceptSequence(Quest quest)
    {
        QuestSequence sequence = new() { Sequence = 0 };

        Level? level = gameData.FindLevel(quest.IssuerLocation.RowId);
        if (level == null)
            return sequence;

        QuestStep step = new(
            EInteractionType.AcceptQuest,
            quest.IssuerStart.RowId != 0 ? quest.IssuerStart.RowId : null,
            StepPosition(level.Value),
            level.Value.Territory.RowId);

        Note(step, $"auto: issuer {Describe(quest.IssuerStart.RowId)}");
        sequence.Steps.Add(step);
        return sequence;
    }

    /// <summary>
    ///     Target selection, most trustworthy source first:
    ///     a todo location that names an object is the journal's own marker for the sequence; failing that the
    ///     script's actor for the sequence; failing that a bare todo position with nothing to interact with.
    /// </summary>
    private QuestSequence? BuildSequence(Quest quest, QuestSymbols symbols, QuestScriptAnalysis analysis, byte number)
    {
        QuestSequence sequence = new() { Sequence = number };

        List<Level> todoLevels = TodoLevels(quest, number);
        List<Level> withObject = [.. todoLevels.Where(x => x.Object.RowId != 0)];
        analysis.Hints.TryGetValue(number, out SequenceHint? hint);

        // Only trust the todo objects when they account for the whole list. A sequence that is mostly bare
        // search areas with one named NPC among them is pointing at the areas; the NPC is a bystander who
        // happens to stand there, and interacting with them is not the objective.
        if (withObject.Count > 0 && withObject.Count == todoLevels.Count)
        {
            // A sequence's todo list can name the same object more than once; interacting with it twice is not
            // what that means, so one step per object is enough.
            HashSet<uint> alreadyStepped = [];
            foreach (Level level in withObject)
            {
                uint dataId = level.Object.RowId;
                if (!alreadyStepped.Add(dataId))
                    continue;

                QuestStep step = new(EInteractionType.Interact, dataId, StepPosition(level), level.Territory.RowId);
                Note(step, $"auto: todo location -> {Describe(dataId)}");
                sequence.Steps.Add(step);
            }
        }
        else if (hint != null && TodoQty(quest, number) > 1 && todoLevels.Count > 1 &&
                 hint.CandidateSymbols.Count > 1)
        {
            // "Speak with all three": the todo entries are bare map ranges with nothing to interact with, and
            // the actor for each one has to be matched to it by position.
            AddStepsPerTodoArea(symbols, sequence, hint, todoLevels);
        }

        // When the script names enemies with no actor in front of them, the fight is what the sequence is for.
        // The quest giver stays targetable throughout and gets listed for that reason alone, so interacting with
        // them mid-hunt is a step the player never performs. Any other actor is still worth emitting.
        bool objectiveIsCombat = hint != null && hint.EnemyPairs.Any(x => x.Actor == null);

        // With a fight to be had, the sequence's actor is where it happens rather than someone to talk to; the
        // combat step below anchors itself to them. Emitting a separate interaction here produces a step the
        // player never performs - the NPC just says a line and the objective is still the fight.
        if (sequence.Steps.Count == 0 && !objectiveIsCombat && hint is { TargetSymbols.Count: > 0 })
        {
            uint? preferredTerritory = todoLevels.Count > 0 ? todoLevels[0].Territory.RowId : null;
            Vector3? near = todoLevels.Count > 0 ? Position(todoLevels[0]) : null;
            bool ambiguous = hint.TargetSymbols.Count > 1;

            foreach (string symbol in hint.TargetSymbols)
            {
                if (BuildStepFromSymbol(symbols, symbol, preferredTerritory, ambiguous, near) is { } step)
                    sequence.Steps.Add(step);
            }
        }

        // Applied last, and to the sequence as a whole, so every enemy the script names is covered no matter
        // which of the branches above produced the steps.
        // Emote first: a todo line that spells out "/dance" is more specific evidence than merely carrying an
        // item, so it claims the step before the item-use pass looks at what is left.
        ApplyEmote(quest, sequence, number);

        if (hint != null)
        {
            ApplyCombat(symbols, sequence, hint, todoLevels, TodoQty(quest, number));
            AnnotateTrade(symbols, sequence, hint);
        }

        // A sequence with only a search area and nothing to talk to is where an unclaimed hunt objective belongs.
        if (sequence.Steps.Count == 0 && todoLevels.Count > 0 && AddUnattributedCombat(symbols, sequence, todoLevels, TodoQty(quest, number)))
            return sequence;

        // Last resort before giving up on a target: pair the search areas with whatever actors the script
        // named for this sequence. The branch above runs this only for the "speak with all three" shape; here
        // the constraints are dropped, because a step pointing at the right NPC beats a bare position.
        // Not when the sequence is a fight, though - the combat step already anchors itself to the actor, and
        // adding an interaction on the same NPC in front of it is a step the player never performs.
        if (sequence.Steps.Count == 0 && !objectiveIsCombat && hint != null && todoLevels.Count > 0)
            AddStepsPerTodoArea(symbols, sequence, hint, todoLevels);

        if (sequence.Steps.Count == 0)
        {
            foreach (Level level in todoLevels)
            {
                QuestStep step = new(EInteractionType.WalkTo, dataId: null, StepPosition(level), level.Territory.RowId);
                Note(step, "auto: todo location with no object - check what actually happens here");
                sequence.Steps.Add(step);
            }
        }
        if (sequence.Steps.Count == 0)
            return null;

        return sequence;
    }

    /// <summary>
    ///     Turns every enemy the script names for this sequence into combat. Each enemy is attached to the step
    ///     for the actor that spawns it; if no such step exists yet — a sequence like
    ///     <c>EOBJECT0 ENEMY0 EOBJECT1 ENEMY1</c> only produces one step on its own — the missing one is added,
    ///     in script order. Enemies with no actor in front of them become a standalone fight.
    /// </summary>
    private void ApplyCombat(
        QuestSymbols symbols,
        QuestSequence sequence,
        SequenceHint hint,
        List<Level> todoLevels,
        int requiredCount)
    {
        foreach (IGrouping<string?, (string? Actor, string Enemy)> group in hint.EnemyPairs.GroupBy(x => x.Actor,
                     StringComparer.Ordinal))
        {
            List<uint> enemies = [];
            Level? spawnLocation = null;
            foreach ((string? _, string enemy) in group)
            {
                (uint id, Level? level) = ResolveEnemy(symbols, enemy);
                if (id == 0)
                    continue;

                if (!enemies.Contains(id))
                    enemies.Add(id);
                spawnLocation ??= level;
            }

            if (enemies.Count == 0)
                continue;

            if (group.Key is not { } actorSymbol)
            {
                // No actor spawns these, but the sequence's actor is still where the fight is - hand-written
                // paths anchor the combat step to them rather than adding a step in front of it.
                Level? anchorLevel = null;
                uint anchorDataId = 0;
                if (hint.TargetSymbols.Count > 0 && hint.TargetSymbols[0] is { } anchorSymbol &&
                    symbols.DataId(anchorSymbol) is { } candidate && candidate != 0)
                {
                    anchorLevel = ResolveLevel(symbols, anchorSymbol, candidate, preferredTerritory: null);
                    if (anchorLevel != null)
                        anchorDataId = candidate;
                }

                AddStandaloneCombat(sequence, enemies, spawnLocation, todoLevels, symbols.SoleEventItem(),
                    requiredCount, anchorDataId, anchorLevel);
                continue;
            }

            if (symbols.DataId(actorSymbol) is not { } dataId || dataId == 0)
                continue;

            QuestStep? step = sequence.Steps.FirstOrDefault(x => x.DataId == dataId);
            if (step == null)
            {
                step = BuildStepFromSymbol(symbols, actorSymbol, preferredTerritory: null, ambiguous: false);
                if (step == null)
                    continue;

                sequence.Steps.Add(step);
            }

            step.InteractionType = EInteractionType.Combat;
            step.EnemySpawnType = EEnemySpawnType.AfterInteraction;

            // The schema allows exactly one of the two enemy lists. A step anchored by a standalone fight
            // already carries ComplexCombatData, so these enemies join it rather than opening a second list.
            if (step.ComplexCombatData.Count > 0)
            {
                foreach (uint enemy in enemies.Where(x => !step.ComplexCombatData.Exists(y => y.DataId == x)))
                    step.ComplexCombatData.Add(new ComplexCombatData { DataId = enemy });
            }
            else
            {
                step.KillEnemyDataIds = enemies;
            }

            string existing = _provenance.TryGetValue(step, out string? note) ? note : $"auto: lua {actorSymbol}";
            _provenance[step] = $"{existing}; spawns enemies {string.Join(", ", enemies)}";
        }
    }

    /// <summary>
    ///     Emits one step per todo area, pairing each with the candidate actor standing closest to it. Keeps the
    ///     journal's own ordering, which is the order the sequence expects them to be visited in.
    /// </summary>
    /// <summary>
    ///     How far an actor's own position may sit from a search area before the area is treated as the better
    ///     answer. Zones are large; a placed actor this far from the journal's marker is not the thing marked.
    /// </summary>
    private const float MaximumAreaMatchDistance = 200f;

    private void AddStepsPerTodoArea(
        QuestSymbols symbols,
        QuestSequence sequence,
        SequenceHint hint,
        List<Level> todoLevels)
    {
        List<(string Symbol, uint DataId, Level? Level)> actors = [];
        foreach (string symbol in hint.CandidateSymbols)
        {
            if (symbols.DataId(symbol) is not { } dataId || dataId == 0)
                continue;

            actors.Add((symbol, dataId, ResolveLevel(symbols, symbol, dataId, preferredTerritory: null)));
        }

        if (actors.Count < todoLevels.Count)
            return;

        HashSet<uint> used = [];
        foreach (Level todo in todoLevels)
        {
            Vector3 area = Position(todo);
            (string Symbol, uint DataId, Vector3 Position)? best = null;
            float bestDistance = float.MaxValue;

            foreach (var actor in actors)
            {
                if (used.Contains(actor.DataId))
                    continue;

                foreach (Vector3 candidate in PlacementsIn(actor, todo.Territory.RowId))
                {
                    // An actor standing hundreds of yalms from the marker is not what the marker points at; it
                    // is placed somewhere else entirely and the area is the better position to use.
                    float distance = Vector3.Distance(candidate, area);
                    if (distance >= bestDistance || distance > MaximumAreaMatchDistance)
                        continue;

                    bestDistance = distance;
                    best = (actor.Symbol, actor.DataId, candidate);
                }
            }

            if (best is { } match)
            {
                used.Add(match.DataId);
                QuestStep step = new(EInteractionType.Interact, match.DataId, match.Position,
                    todo.Territory.RowId);
                Note(step, $"auto: todo area {bestDistance:F0}y from lua {match.Symbol} -> {Describe(match.DataId)}");
                sequence.Steps.Add(step);
                continue;
            }

            // Nothing placed nearby: fall back to the next unused actor and let the area stand in for its
            // position, which is the best the data offers once neither source knows where the actor is.
            if (actors.FirstOrDefault(x => !used.Contains(x.DataId) && x.DataId != 0) is not { DataId: not 0 } spare)
                continue;

            used.Add(spare.DataId);
            bool placed = spare.Level is not null;
            QuestStep fallback = new(
                EInteractionType.Interact,
                spare.DataId,
                placed ? StepPosition(spare.Level!.Value) : StepPosition(todo),
                placed ? spare.Level!.Value.Territory.RowId : todo.Territory.RowId);

            Note(fallback, placed
                ? $"auto: todo area, nothing nearby, lua {spare.Symbol} -> {Describe(spare.DataId)}"
                : $"auto: todo area, lua {spare.Symbol} -> {Describe(spare.DataId)} has no fixed position");
            sequence.Steps.Add(fallback);
        }
    }

    /// <summary>
    ///     Every spot in the territory an actor could be standing. A <c>Level</c> row is the actor's own
    ///     placement and settles it; without one - which is the case for most quest-only actors, the bolting
    ///     dodos of "capture the bolting dodos" among them - the zone's layer data is where the game itself
    ///     puts them, and an actor laid out once per quest phase gets a spot for each.
    /// </summary>
    private IEnumerable<Vector3> PlacementsIn((string Symbol, uint DataId, Level? Level) actor, uint territoryId)
    {
        if (actor.Level is { } level)
        {
            if (level.Territory.RowId == territoryId)
                yield return Position(level);

            yield break;
        }

        foreach (Vector3 position in gameData.LayerPositions(actor.DataId, territoryId))
            yield return position;
    }

    /// <summary>
    ///     The layer placement of an object closest to <paramref name="near" />, or the first one when there is
    ///     nothing to measure against. An actor laid out once per quest phase has several, and the journal's
    ///     marker is the only hint as to which phase this sequence is.
    /// </summary>
    private Vector3? NearestPlacement(uint dataId, uint territoryId, Vector3? near)
    {
        IReadOnlyList<Vector3> placements = gameData.LayerPositions(dataId, territoryId);
        if (placements.Count == 0)
            return null;

        if (near is not { } target)
            return placements[0];

        return placements.OrderBy(x => Vector3.Distance(x, target)).First();
    }

    /// <summary>
    ///     Turns plain interactions into item uses where the script says the player is carrying an event item
    ///     and is able to use it. Accepting, turning in and fighting are left alone — you do not use a key on a
    ///     quest giver or a boar.
    /// </summary>
    private void ApplyEventItemUses(
        Quest quest,
        QuestSymbols symbols,
        QuestScriptAnalysis analysis,
        List<QuestSequence> sequences)
    {
        Dictionary<byte, QuestSequence> bySequence = sequences.ToDictionary(x => x.Sequence);

        foreach (string symbol in analysis.Hints.Values.SelectMany(x => x.HeldItemSymbols).Distinct(StringComparer.Ordinal))
        {
            if (symbols.Value(symbol) is not { } itemId || itemId == 0)
                continue;

            // IsEventItemUsable names the sequence the item may actually be used in. That is exact, so when it
            // is present it settles the question - the item is not used anywhere else, even if a later
            // sequence still carries it or an earlier one has something interactable.
            List<SequenceHint> usableAt =
            [
                .. analysis.Hints.Values
                    .Where(x => x.UsableItemSymbols.Contains(symbol, StringComparer.Ordinal))
                    .OrderByDescending(x => x.Sequence)
            ];

            bool applied = false;
            foreach (SequenceHint usable in usableAt)
            {
                if (bySequence.TryGetValue(usable.Sequence, out QuestSequence? usableSequence) &&
                    UseItemOn(usableSequence, symbol, itemId))
                {
                    applied = true;
                    break;
                }
            }

            // Only when none of those sequences had anything to use it on does the carried-item heuristic get
            // a turn; the exact answer is preferred, but silence is worse than a good guess.
            if (applied)
                continue;

            if (analysis.UsesEventItems)
            {
                // An item is picked up in one sequence and used in another, and it is held for the whole span
                // between. Using it consumes it, so the latest sequence still holding it is where it gets used -
                // anything before that is just carrying it. Sequences that ended up with nothing to use it on
                // are skipped, which walks the search back to the last one that does.
                IEnumerable<SequenceHint> holders = analysis.Hints.Values
                    .Where(x => x.HeldItemSymbols.Contains(symbol, StringComparer.Ordinal) && !x.IsNpcTrade)
                    .OrderByDescending(x => x.Sequence);

                foreach (SequenceHint holder in holders)
                {
                    if (bySequence.TryGetValue(holder.Sequence, out QuestSequence? sequence) &&
                        UseItemOn(sequence, symbol, itemId))
                    {
                        break;
                    }
                }

                continue;
            }

            // Some scripts never declare IsEventItemUsable even though the quest plainly uses the item. The
            // journal still says so - "Use the lice comb on the shaggy sheep" - so fall back to the todo line
            // that tells you to use it. Only an explicit instruction counts: lines that merely name the item
            // while handing it over or picking it up are not item uses.
            if (SequenceUsingItem(quest, itemId) is { } named &&
                bySequence.TryGetValue(named, out QuestSequence? namedSequence))
            {
                UseItemOn(namedSequence, symbol, itemId);
            }
        }
    }

    /// <summary>
    ///     The sequence whose todo line tells you to <em>use</em> this event item. Requires both the item's name
    ///     and a "use" instruction, so "Obtain a fresh bunch of gysahl greens" does not count as using them.
    /// </summary>
    private byte? SequenceUsingItem(Quest quest, uint itemId)
    {
        string name = gameData.EventItemName(itemId);
        if (name.Length == 0)
            return null;

        IReadOnlyList<string> todoTexts = gameData.TodoTexts(quest);
        for (int i = 0; i < quest.TodoParams.Count && i < todoTexts.Count; i++)
        {
            string text = todoTexts[i];
            if (quest.TodoParams[i].ToDoCompleteSeq is > 0 and < 255 &&
                text.Contains(name, StringComparison.OrdinalIgnoreCase) &&
                text.Contains("use ", StringComparison.OrdinalIgnoreCase))
            {
                return quest.TodoParams[i].ToDoCompleteSeq;
            }
        }

        return null;
    }

    /// <summary>Converts a sequence's steps to use the item, if any of them can be. False when none apply.</summary>
    private bool UseItemOn(QuestSequence sequence, string symbol, uint itemId)
    {
        bool applied = false;

        foreach (QuestStep step in sequence.Steps)
        {
            switch (step.InteractionType)
            {
                case EInteractionType.Interact:
                    step.InteractionType = EInteractionType.UseItem;
                    step.ItemId = itemId;
                    break;

                // Interacting is not what starts this fight - using the item on the target is.
                case EInteractionType.Combat when step.EnemySpawnType == EEnemySpawnType.AfterInteraction:
                    step.EnemySpawnType = EEnemySpawnType.AfterItemUse;
                    step.ItemId = itemId;
                    break;

                default:
                    continue;
            }

            applied = true;
            if (_provenance.TryGetValue(step, out string? note))
                _provenance[step] = $"{note}; use item {symbol}={itemId} on it";
        }

        return applied;
    }

    /// <summary>Records an item handover in the step comments; the step itself stays a plain interaction.</summary>
    private void AnnotateTrade(QuestSymbols symbols, QuestSequence sequence, SequenceHint hint)
    {
        if (!hint.IsNpcTrade)
            return;

        string items = string.Join(", ", hint.TradeItemSymbols.Select(x => $"{x}={symbols.Value(x)}"));
        string suffix = items.Length > 0 ? $"; hands over {items}" : "; npc trade";

        foreach (QuestStep step in sequence.Steps)
        {
            if (_provenance.TryGetValue(step, out string? note))
                _provenance[step] = note + suffix;
        }
    }

    /// <summary>
    ///     An <c>ENEMY*</c> symbol is stored one of two ways: as a <c>Level</c> row placing a spawned enemy in
    ///     the world, or — for "kill ten of these" objectives — as the enemy id itself, with no location.
    /// </summary>
    private (uint Id, Level? Level) ResolveEnemy(QuestSymbols symbols, string symbol)
    {
        if (symbols.EnemyLevelRow(symbol) is not { } value || value == 0)
            return (0, null);

        // Type 9 is BNpcBase; anything else in the Level sheet is not an enemy placement.
        if (gameData.FindLevel(value) is { Type: 9, Object.RowId: > 0 } level)
            return (level.Object.RowId, level);

        return (value, null);
    }

    /// <summary>
    ///     Claims the enemies the script never tied to a sequence, once, for the first sequence that has a
    ///     search area and nothing else to do in it.
    /// </summary>
    private bool AddUnattributedCombat(QuestSymbols symbols, QuestSequence sequence, List<Level> todoLevels, int requiredCount)
    {
        if (_unattributedEnemies.Count == 0)
            return false;

        List<uint> enemies = [];
        Level? spawnLocation = null;
        foreach (string symbol in _unattributedEnemies)
        {
            (uint id, Level? level) = ResolveEnemy(symbols, symbol);
            if (id == 0)
                continue;

            if (!enemies.Contains(id))
                enemies.Add(id);

            spawnLocation ??= level;
        }

        if (enemies.Count == 0)
            return false;

        _unattributedEnemies.Clear();
        AddStandaloneCombat(sequence, enemies, spawnLocation, todoLevels, symbols.SoleEventItem(), requiredCount);
        return sequence.Steps.Count > 0;
    }

    /// <summary>Combat with no interaction in front of it: go to the area and fight what is there.</summary>
    private void AddStandaloneCombat(
        QuestSequence sequence,
        List<uint> enemies,
        Level? spawnLocation,
        List<Level> todoLevels,
        uint? dropItemId,
        int requiredCount,
        uint anchorDataId = 0,
        Level? anchorLevel = null)
    {
        // A placed enemy fights where it stands; an id-only enemy is hunted wherever the journal points; and
        // failing both, next to whichever actor the sequence revolves around.
        Level? location = spawnLocation ?? (todoLevels.Count > 0 ? todoLevels[0] : anchorLevel);
        if (location is not { } spot)
            return;

        // The anchor may already have a step of its own from an earlier pass. Turning that one into the fight
        // keeps the sequence at one step per target instead of interacting with the NPC and then fighting at
        // the same spot.
        QuestStep? existing = anchorDataId != 0
            ? sequence.Steps.Find(x => x.DataId == anchorDataId)
            : null;

        QuestStep step = existing ?? new QuestStep(EInteractionType.Combat,
            anchorDataId != 0 ? anchorDataId : null, StepPosition(spot), spot.Territory.RowId);

        step.InteractionType = EInteractionType.Combat;
        step.EnemySpawnType = spawnLocation != null
            ? EEnemySpawnType.AutoOnEnterArea
            : EEnemySpawnType.OverworldEnemies;

        string provenance = spawnLocation != null
            ? $"auto: lua enemies spawn here -> {string.Join(", ", enemies)}"
            : $"auto: lua overworld enemies -> {string.Join(", ", enemies)}";

        // "Kill until it drops": the fight has to keep going until the item is in the bag. Without this the
        // plugin stops the moment combat ends, i.e. after a single kill. How many are needed comes from the
        // event item's stack size, which is the objective's own limit; the todo quantity counts kills, not
        // drops, and the two are not always the same.
        int needed = dropItemId is { } id ? gameData.EventItemStackSize(id) : 0;
        if (needed == 0)
            needed = requiredCount;

        if (spawnLocation == null && dropItemId is { } itemId && needed > 0)
        {
            step.ComplexCombatData =
            [
                .. enemies.Select(x => new ComplexCombatData
                {
                    DataId = x,
                    MinimumKillCount = (uint)needed,
                    RewardItemId = itemId,
                    RewardItemCount = needed
                })
            ];

            provenance += $"; until {needed}x item {itemId} drops";
        }
        else
        {
            step.KillEnemyDataIds = enemies;
        }

        if (existing != null)
        {
            // Keep the note that explains where the step came from, and say what it turned into.
            _provenance[step] = _provenance.TryGetValue(step, out string? note)
                ? $"{note}; {provenance}"
                : provenance;
            return;
        }

        Note(step, provenance);
        sequence.Steps.Add(step);
    }

    /// <summary>
    ///     Turns an interaction into an emote when the journal asks for one. "/dance for Aanu Vanu" is the only
    ///     place the game records which emote a quest wants — it is in neither the script nor the sheet columns.
    /// </summary>
    private void ApplyEmote(Quest quest, QuestSequence sequence, byte number)
    {
        if (sequence.Steps.Count == 0)
            return;

        IReadOnlyList<string> todoTexts = gameData.TodoTexts(quest);
        if (todoTexts.Count == 0)
            return;

        // The TODO_nn number lines up with the position in TodoParams, which is what names the sequence.
        for (int i = 0; i < quest.TodoParams.Count && i < todoTexts.Count; i++)
        {
            if (quest.TodoParams[i].ToDoCompleteSeq != number)
                continue;

            if (EmoteIn(todoTexts[i]) is not { } emote)
                continue;

            foreach (QuestStep step in sequence.Steps)
            {
                if (step.InteractionType != EInteractionType.Interact)
                    continue;

                step.InteractionType = EInteractionType.Emote;
                step.Emote = emote;

                if (_provenance.TryGetValue(step, out string? note))
                    _provenance[step] = $"{note}; todo says \"{todoTexts[i].Trim()}\"";
            }

            return;
        }
    }

    /// <summary>Finds the first text command in a todo line that names an emote.</summary>
    private EEmote? EmoteIn(string todoText)
    {
        foreach (string word in todoText.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (word.Length < 2 || word[0] != '/')
                continue;

            if (gameData.EmoteFromCommand(word.TrimEnd('.', ',', '!', '?')) is { } emote)
                return emote;
        }

        return null;
    }

    /// <summary>Whether this is the NPC who hands the quest out or takes it back in.</summary>
    private static bool IsQuestGiver(Quest quest, uint? dataId) =>
        dataId is { } id && id != 0 && (id == quest.IssuerStart.RowId || id == quest.TargetEnd.RowId);

    private static int TodoQty(Quest quest, byte number)
    {
        foreach (var todo in quest.TodoParams)
        {
            if (todo.ToDoCompleteSeq == number)
                return todo.ToDoQty;
        }

        return 0;
    }

    private QuestStep? BuildStepFromSymbol(
        QuestSymbols symbols,
        string symbol,
        uint? preferredTerritory,
        bool ambiguous,
        Vector3? near = null)
    {
        if (symbols.DataId(symbol) is not { } dataId || dataId == 0)
            return null;

        Level? level = ResolveLevel(symbols, symbol, dataId, preferredTerritory);
        QuestStep step;
        string provenance = $"auto: lua {symbol} -> {Describe(dataId)}";

        if (level != null)
        {
            step = new QuestStep(EInteractionType.Interact, dataId, StepPosition(level.Value),
                level.Value.Territory.RowId);
        }
        else if (preferredTerritory is { } territory &&
                 NearestPlacement(dataId, territory, near) is { } placement)
        {
            // No Level row anywhere, but the zone the journal points at lays the actor out; that is a real
            // position rather than the guess a bare search area would give.
            step = new QuestStep(EInteractionType.Interact, dataId, placement, territory);
            provenance += "; positioned from layer data";
        }
        else
            return null;

        if (ambiguous)
            provenance += "; script lists several actors for this sequence, only one is likely correct";

        Note(step, provenance);
        return step;
    }

    /// <summary>
    ///     Where an actor actually stands. World <c>Level</c> rows are preferred over the script's <c>LOC_</c>
    ///     symbols, which frequently point at cutscene placements rather than the interactable NPC.
    /// </summary>
    private Level? ResolveLevel(QuestSymbols symbols, string symbol, uint dataId, uint? preferredTerritory)
    {
        List<Level> placements = [.. gameData.LevelsForObject(dataId)];

        if (preferredTerritory is { } territory)
        {
            Level? match = placements.Cast<Level?>().FirstOrDefault(x => x!.Value.Territory.RowId == territory);
            if (match != null)
                return match;
        }

        if (placements.Count > 0)
            return placements[0];

        return symbols.LevelRowsFor(symbol).Select(gameData.FindLevel).FirstOrDefault(x => x != null);
    }

    private List<Level> TodoLevels(Quest quest, byte number)
    {
        List<Level> levels = [];
        foreach (var todo in quest.TodoParams)
        {
            if (todo.ToDoCompleteSeq != number)
                continue;

            foreach (var locationRef in todo.ToDoLocation)
            {
                if (gameData.FindLevel(locationRef.RowId) is { } level)
                    levels.Add(level);
            }
        }

        return levels;
    }

    private QuestSequence BuildCompleteSequence(Quest quest, QuestSymbols symbols, QuestScriptAnalysis analysis)
    {
        QuestSequence sequence = new() { Sequence = 255 };

        uint dataId = quest.TargetEnd.RowId;
        Level? level = null;
        string provenance = $"auto: turn-in {Describe(dataId)}";

        // The journal's own turn-in marker, when the quest has one.
        Level? todoLevel = TodoLevels(quest, 255)
            .Cast<Level?>()
            .FirstOrDefault(x => x!.Value.Object.RowId != 0);

        if (todoLevel != null)
        {
            level = todoLevel;
            dataId = todoLevel.Value.Object.RowId;
            provenance = $"auto: todo turn-in -> {Describe(dataId)}";
        }
        else if (dataId != 0 && dataId == quest.IssuerStart.RowId)
        {
            // Turned in to whoever gave it out, so the issuer location is the right placement of several.
            level = gameData.FindLevel(quest.IssuerLocation.RowId);
            if (level != null)
                provenance = $"auto: turn-in back to issuer {Describe(dataId)}";
        }

        if (level == null && dataId != 0)
        {
            // SEQ_FINISH usually maps to the turn-in actor, which gives its LOC_ symbol as a last resort.
            string? symbol = analysis.Hints.TryGetValue(255, out SequenceHint? hint)
                ? hint.TargetSymbols.FirstOrDefault(x => symbols.DataId(x) == dataId)
                : null;

            level = ResolveLevel(symbols, symbol ?? string.Empty, dataId, preferredTerritory: null);
        }

        if (dataId == 0 || level == null)
            return sequence;

        QuestStep step = new(
            EInteractionType.CompleteQuest,
            dataId,
            StepPosition(level.Value),
            level.Value.Territory.RowId);

        Note(step, provenance);
        sequence.Steps.Add(step);
        return sequence;
    }

    /// <summary>
    ///     Adds travel shortcuts and marks steps in zones that allow flight.
    ///     <para>
    ///         Tracking resets at every sequence, because the plugin picks each sequence up from wherever the
    ///         player happens to be rather than continuing from the previous step. A shortcut that turns out to
    ///         be unnecessary is skipped at runtime by the distance checks in <c>UseAetheryteShortcut</c>, so
    ///         emitting one per sequence is safe and matches what hand-written paths do.
    ///     </para>
    /// </summary>
    private bool ApplyTravel(List<QuestSequence> sequences)
    {
        bool needsManualShortcut = false;
        QuestStep? previousStep = null;

        foreach (QuestSequence sequence in sequences)
        {
            uint previousTerritory = 0;

            foreach (QuestStep step in sequence.Steps)
            {
                if (step.TerritoryId != previousTerritory)
                {
                    previousTerritory = step.TerritoryId;

                    TravelShortcut? travel = step.Position is { } position
                        ? gameData.ResolveTravel(step.TerritoryId, position)
                        : null;

                    if (travel == null)
                    {
                        needsManualShortcut = true;
                    }
                    else
                    {
                        step.AetheryteShortcut = travel.Aetheryte;
                        step.AethernetShortcut = travel.Aethernet;

                        if (travel.Caveat != null)
                        {
                            needsManualShortcut = true;
                            if (_provenance.TryGetValue(step, out string? existing))
                                _provenance[step] = $"{existing}; travel: {travel.Caveat}";
                        }
                    }
                }

                // Decided after the shortcut, because where the step is travelled from depends on it.
                if (gameData.TerritorySupportsFlight(step.TerritoryId) && IsWorthFlying(step, previousStep))
                    step.Fly = true;

                previousStep = step;
            }
        }

        return needsManualShortcut;
    }

    private void Note(QuestStep step, string provenance) => _provenance[step] = provenance;

    /// <summary>
    ///     Height added to a position taken from a <c>Level</c> row that places no object.
    ///     <para>
    ///         Those rows mark a search area rather than a thing to stand on, and their Y often sits fractionally
    ///         under the floor. vnavmesh fails outright on a point below the mesh ("got 0 points").
    ///         <c>QuestRegistry.CreateQuestRoot</c> uses 30 for the same reason
    ///     </para>
    /// </summary>
    private const float AreaHeightPadding = 30f;

    /// <summary>Raw coordinates of a <c>Level</c> row, for measuring distances.</summary>
    /// <summary>
    ///     Below this, flying is not worth the mount-up. Setting <c>Fly</c> forces the plugin to mount whatever
    ///     the distance (<c>EMountIf.Always</c> in <c>MoveExecutor</c>); leaving it unset hands the decision to
    ///     <c>MountEvaluator</c>. Shares that evaluator's threshold so the two agree — a step left unflown
    ///     because it was short is then one the evaluator also declines to mount for.
    /// </summary>
    private const float MinimumFlightDistance = MountStep.MountDistance;

    /// <summary>
    ///     Whether the trip to this step is long enough to fly. The trip starts wherever the step's own travel
    ///     shortcut lands the player, or at the previous step when it stays in the same zone. When neither is
    ///     known the distance is unknowable, and flying is left on rather than guessed away.
    /// </summary>
    private bool IsWorthFlying(QuestStep step, QuestStep? previousStep)
    {
        if (step.Position is not { } destination)
            return true;

        Vector2? origin = null;
        if (step.AethernetShortcut is { } aethernet)
            origin = gameData.AetherytePosition(aethernet.To);
        else if (step.AetheryteShortcut is { } aetheryte)
            origin = gameData.AetherytePosition(aetheryte);
        else if (previousStep is { Position: { } previousPosition } &&
                 previousStep.TerritoryId == step.TerritoryId)
            origin = new Vector2(previousPosition.X, previousPosition.Z);

        if (origin is not { } from)
            return true;

        return Vector2.Distance(from, new Vector2(destination.X, destination.Z)) >= MinimumFlightDistance;
    }

    private static Vector3 Position(Level level) => new(level.X, level.Y, level.Z);

    /// <summary>Coordinates to put in a step, lifted clear of the floor when the row places no object.</summary>
    private static Vector3 StepPosition(Level level) =>
        level.Object.RowId == 0
            ? new Vector3(level.X, level.Y + AreaHeightPadding, level.Z)
            : new Vector3(level.X, level.Y, level.Z);

    private string Describe(uint dataId)
    {
        if (dataId == 0)
            return "0";

        string name = gameData.ObjectName(dataId);
        return name.Length > 0 ? $"{dataId} ({name})" : dataId.ToString(CultureInfo.InvariantCulture);
    }
}

