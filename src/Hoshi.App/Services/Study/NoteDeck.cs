using System.Text.Json;
using Hoshi.Sgf;
using Hoshi.Sgf.Joseki;
using Hoshi.Sgf.Study;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hoshi.App.Services.Study;

/// <summary>One note to review: the question it makes, in its game from the library.</summary>
public sealed record NoteReviewItem(string Id, ReplayEntry Entry, GameTree Tree, QuizQuestion Question);

/// <summary>
/// Spaced repetition of the player's own notes: every mistake, good move and key moment pinned in a game of the
/// library becomes a card ("what would you play here?"). Right answers push a card to a later day (Leitner boxes:
/// 10 min, 1, 3, 7, 14, 30 days); a miss brings it back tomorrow-ish. The progress lives in
/// <c>study/note-cards.json</c>, which travels in backups and sync like the joseki progress.
/// </summary>
public sealed class NoteDeck
{
    /// <summary>The categories that make cards: what the player wants to remember.</summary>
    public static readonly IReadOnlySet<PinCategory> Categories = new HashSet<PinCategory> { PinCategory.Mistake, PinCategory.GoodMove, PinCategory.KeyMoment };

    /// <summary>New cards a day, on top of the due ones: a review of a few minutes, not an hour.</summary>
    public const int NewPerSession = 10;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };

    private readonly string? _file;
    private readonly ILogger _logger;
    private readonly Lock _gate = new();
    private Dictionary<string, JosekiCard>? _cards;

    public NoteDeck(string? dataDirectory = null, ILogger<NoteDeck>? logger = null)
    {
        _file = dataDirectory is null ? null : Path.Combine(dataDirectory, "study", "note-cards.json");
        _logger = logger ?? NullLogger<NoteDeck>.Instance;
    }

    public IReadOnlyDictionary<string, JosekiCard> Cards
    {
        get
        {
            lock (_gate)
            {
                return new Dictionary<string, JosekiCard>(Loaded());
            }
        }
    }

    /// <summary>The card id of a question: the game, the move number and the pin (stable while the note stays).</summary>
    public static string IdOf(ReplayEntry entry, QuizQuestion question)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(question);
        return $"{entry.Id}#{MoveNumber(question.Pinned)}#{question.Pin.Id}";
    }

    /// <summary>Every card the library makes (reads each game's SGF: call it off the UI thread).</summary>
    public static IReadOnlyList<NoteReviewItem> Collect(IReplayStore store, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        var items = new List<NoteReviewItem>();
        foreach (ReplayEntry entry in store.List())
        {
            cancellationToken.ThrowIfCancellationRequested();
            GameTree tree;
            try
            {
                tree = store.Load(entry);
            }
            catch (Exception ex) when (ex is IOException or FormatException or UnauthorizedAccessException)
            {
                continue; // a game that cannot be read has no cards today
            }

            foreach (QuizQuestion q in StudyQuiz.Build(tree, new HashSet<PinCategory>(Categories)))
            {
                items.Add(new NoteReviewItem(IdOf(entry, q), entry, tree, q));
            }
        }

        return items;
    }

    /// <summary>
    /// Today's review: the due cards (most overdue first), then up to <see cref="NewPerSession"/> never reviewed
    /// ones (oldest games first, so the deck fills in the order the games were played).
    /// </summary>
    public IReadOnlyList<NoteReviewItem> Session(IReadOnlyList<NoteReviewItem> all, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(all);
        IReadOnlyDictionary<string, JosekiCard> cards = Cards;
        var due = all.Where(i => cards.TryGetValue(i.Id, out JosekiCard? c) && c.IsDue(now)).OrderBy(i => cards[i.Id].Due);
        var fresh = all.Where(i => !cards.ContainsKey(i.Id)).OrderBy(i => i.Entry.Played).Take(NewPerSession);
        return [.. due, .. fresh];
    }

    /// <summary>How many cards wait today (due plus the new ones a session would take).</summary>
    public int DueCount(IReadOnlyList<NoteReviewItem> all, DateTimeOffset now) => Session(all, now).Count;

    /// <summary>Moves the card by the answer and saves.</summary>
    public JosekiCard Grade(string id, bool right, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        lock (_gate)
        {
            Dictionary<string, JosekiCard> cards = Loaded();
            JosekiCard card = (cards.TryGetValue(id, out JosekiCard? c) ? c : JosekiCard.New(id)).Review(right, now);
            cards[id] = card;
            Save(cards);
            return card;
        }
    }

    private static int MoveNumber(GameNode node)
    {
        int n = 0;
        for (GameNode? at = node; at is not null; at = at.Parent)
        {
            n += at.HasMove ? 1 : 0;
        }

        return n;
    }

    private Dictionary<string, JosekiCard> Loaded()
    {
        if (_cards is not null)
        {
            return _cards;
        }

        _cards = [];
        if (_file is not null && File.Exists(_file))
        {
            try
            {
                foreach (JosekiCard card in JsonSerializer.Deserialize<List<JosekiCard>>(File.ReadAllText(_file), Json) ?? [])
                {
                    _cards[card.Id] = card;
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Could not read the note cards");
            }
        }

        return _cards;
    }

    private void Save(Dictionary<string, JosekiCard> cards)
    {
        if (_file is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            string temp = _file + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(cards.Values.ToList(), Json));
            File.Move(temp, _file, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not save the note cards");
        }
    }
}
