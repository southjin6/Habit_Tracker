using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using HabitTracker.Core.Domain;

namespace HabitTracker.Core.Backup;

/// <summary>
/// One item as stored in a backup file. Deliberately has no <c>Id</c>: ids belong to one MySQL
/// database and mean nothing to another file or install, so a restored item always takes a fresh one.
///
/// These three are required in the JSON rather than merely non-null: a missing key would otherwise
/// fall back to the C# default and quietly turn an item into a daily with no creation day, which is
/// indistinguishable from a file that meant it.
/// </summary>
public sealed class BackupItem
{
    [JsonRequired]
    public string Name { get; set; } = string.Empty;

    [JsonRequired]
    public ItemKind Kind { get; set; }

    [JsonRequired]
    public DateOnly CreatedOn { get; set; }

    /// <summary>Tasks only: the day the objective was completed.</summary>
    public DateOnly? CompletedOn { get; set; }

    private List<DateOnly> _completions = new();

    /// <summary>
    /// Dailies only: every day it was completed. A <c>null</c> in the file reads as no days at all,
    /// which is what omitting the key already means — the format cannot express "history that went
    /// missing", so an empty list is the only honest reading. Keeping the property non-null is also
    /// what lets validation walk it without asking.
    /// </summary>
    public List<DateOnly> Completions
    {
        get => _completions;
        set => _completions = value ?? new();
    }
}

/// <summary>
/// The whole saved state, as read from and written to a JSON file. The file is meant to be readable
/// and hand-editable, hence the app marker, indented output, and kind names spelled out as words.
/// </summary>
public sealed class BackupFile
{
    /// <summary>The only layout this build can read. Bumping it must keep older files rejected, not guessed.</summary>
    public const int SupportedVersion = 1;

    /// <summary>Guards against pointing Import at some unrelated JSON file.</summary>
    public const string AppMarker = "Habit Tracker";

    /// <summary>
    /// The earliest creation day a file may carry. A plausibility floor, not a storage one: this MySQL
    /// stores 0001-01-01 happily, but a mistyped year would then be accepted and paint a nonsense card
    /// (the missed-day walk counts one day at a time back to creation), and DateOnly.MinValue made that
    /// walk throw outright — which left every card in the window unable to load, not just that one.
    /// Nobody's habit predates 1900.
    /// </summary>
    public static readonly DateOnly EarliestTrackableDay = new(1900, 1, 1);

    public string App { get; set; } = AppMarker;

    public int Version { get; set; } = SupportedVersion;

    public DateOnly ExportedOn { get; set; }

    public List<BackupItem> Items { get; set; } = new();

    /// <summary>
    /// The keys are matched case-insensitively, which is what the guards in <see cref="TryParse"/>
    /// already do and what a hand-edited file needs. An unrecognised member name is an error rather
    /// than something to ignore: silently dropping <c>"knd"</c> or <c>"Completions"</c> would restore
    /// a task as a daily, or an item with no history, and look like a successful import.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() },
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>
    /// Parses a file the user pointed Import at. Returns a readable reason instead of throwing,
    /// because a hand-edited or unrelated JSON file is an expected case here.
    ///
    /// The marker and version are read from the raw document rather than from the deserialized
    /// object: both properties have defaults in C#, so a file that omits them would otherwise look
    /// like a valid version-1 backup of zero items instead of what it is — a file this build cannot
    /// identify.
    /// </summary>
    public static TryParseResult TryParse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return TryParseResult.Fail("The file is empty.");
        }

        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(json);
            root = document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            return TryParseResult.Fail($"That is not valid JSON{Position(ex)}: {Sentence(ex.Message)}");
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return TryParseResult.Fail("The file is not a JSON object, so it is not a Habit Tracker backup.");
        }

        // The kind is checked before the text is read: GetString throws on anything that is not a
        // string, and a marker of the wrong type is a file this build cannot identify, not a crash.
        if (!TryGet(root, AppMarkerProperty, out var appElement) ||
            appElement.ValueKind != JsonValueKind.String ||
            appElement.GetString() is not { } app ||
            string.IsNullOrWhiteSpace(app))
        {
            return TryParseResult.Fail("The file has no \"app\" marker, so this is not a Habit Tracker backup.");
        }

        if (!string.Equals(app, AppMarker, StringComparison.OrdinalIgnoreCase))
        {
            return TryParseResult.Fail($"This is not a Habit Tracker backup (it says it is a \"{app}\" file).");
        }

        if (!TryGet(root, VersionProperty, out var versionElement) ||
            versionElement.ValueKind != JsonValueKind.Number || !versionElement.TryGetInt32(out var version))
        {
            return TryParseResult.Fail("The file has no format version, so this build cannot tell how to read it.");
        }

        if (version != SupportedVersion)
        {
            return TryParseResult.Fail(
                $"The backup uses format version {version}; this build reads version {SupportedVersion} only.");
        }

        if (!TryGet(root, ItemsProperty, out var itemsElement) || itemsElement.ValueKind != JsonValueKind.Array)
        {
            return TryParseResult.Fail("The file has no \"items\" list to restore.");
        }

        BackupFile? backup;
        try
        {
            backup = JsonSerializer.Deserialize<BackupFile>(root.GetRawText(), JsonOptions);
        }
        catch (JsonException ex)
        {
            // No "not a backup" preamble: the marker and version are checked above, so by here the
            // file has already declared itself ours and what is left to say is which part of it
            // this build cannot read. Explain names the place itself.
            return TryParseResult.Fail(Explain(ex));
        }

        return backup is null
            ? TryParseResult.Fail("The file is empty.")
            : TryParseResult.Ok(backup);
    }

    /// <summary>
    /// Where the reader stopped. It counts lines and characters from zero and appends them in library
    /// notation ("LineNumber: 4 | BytePositionInLine: 30"), but these offsets are the one place they
    /// are real: this parse reads the file the user has open. See <see cref="Explain"/> for the
    /// deserializer's, which are not.
    /// </summary>
    private static string Position(JsonException ex) =>
        ex.LineNumber is { } line && ex.BytePositionInLine is { } column
            ? $" (line {line + 1}, character {column + 1})"
            : string.Empty;

    /// <summary>The reader's own sentence, without the location it appends, which is reported separately.</summary>
    private static string Sentence(string message)
    {
        var tail = message.IndexOf(" LineNumber: ", StringComparison.Ordinal);
        var sentence = (tail < 0 ? message : message[..tail]).Trim();

        // "Change the reader options" is advice for someone calling the library, not for someone
        // holding a file.
        return sentence.Contains("trailing comma", StringComparison.Ordinal)
            ? "a trailing comma is not allowed"
            : sentence;
    }

    /// <summary>
    /// Rewrites the deserializer's complaint so it can be acted on. Its own wording names .NET types
    /// ("System.Nullable`1[System.DateOnly]"), and its location tail counts lines and bytes into the
    /// compact text this build re-serialised from the parsed document rather than into the file on
    /// screen, where the line number is always 0 and the offset matches nothing the user can look at.
    /// What it does know is the JSON path, which is turned into the position of an item instead.
    /// </summary>
    private static string Explain(JsonException problem)
    {
        var path = (problem.Path ?? string.Empty)
            .TrimStart('$', '.')
            .Split('.', StringSplitOptions.RemoveEmptyEntries);

        // Two of the shapes name the member they object to, so those names are kept rather than being
        // re-derived from the path.
        if (problem.Message.Contains("could not be mapped to any .NET member", StringComparison.Ordinal))
        {
            var member = path.Length > 0 ? Trim(path[^1]) : "?";
            var container = path.Length > 1 ? path[..^1] : Array.Empty<string>();
            return $"{Where(container)} has a member the format does not define: \"{member}\".";
        }

        if (problem.Message.Contains("was missing required properties", StringComparison.Ordinal))
        {
            var missing = problem.Message[(problem.Message.LastIndexOf(": ", StringComparison.Ordinal) + 2)..];
            return $"{Where(path)} is missing \"{missing.Replace(", ", "\", \"")}\".";
        }

        return $"{Where(path)} {Expected(problem.Message)}.";
    }

    /// <summary>What should have been there, in this format's words rather than the .NET type's name.</summary>
    private static string Expected(string message) => message switch
    {
        _ when message.Contains("List`1[System.DateOnly]", StringComparison.Ordinal) => "is not a list of days",
        _ when message.Contains("System.DateOnly", StringComparison.Ordinal) => "holds something that is not a calendar day (yyyy-MM-dd)",
        _ when message.Contains("BackupItem", StringComparison.Ordinal) => "is not an object with a name, kind and creation day",
        _ when message.Contains("ItemKind", StringComparison.Ordinal) => "is not Daily or Task",
        _ when message.Contains("System.String", StringComparison.Ordinal) => "is not text",
        _ => "holds something this build cannot read",
    };

    /// <summary>
    /// Where a failing value sits, as the position of an item: someone repairing the file counts
    /// entries rather than reading the JSON path the deserializer reports.
    /// </summary>
    private static string Where(IReadOnlyList<string> path)
    {
        if (path.Count == 0)
        {
            return "the file";
        }

        if (Index(path[0]) is not { } position)
        {
            return $"the file's \"{Trim(path[0])}\"";
        }

        var item = $"item {position + 1}";
        return path.Count > 1 ? $"{item}'s \"{Trim(path[1])}\"" : item;
    }

    /// <summary>The number in a path segment like "items[3]", or null when it is a bare name.</summary>
    private static int? Index(string segment)
    {
        var open = segment.IndexOf('[');
        return open >= 0 && segment.EndsWith(']') &&
               int.TryParse(segment.AsSpan(open + 1, segment.Length - open - 2), CultureInfo.InvariantCulture, out var index)
            ? index
            : null;
    }

    /// <summary>A path segment without its index: "completions[2]" as "completions".</summary>
    private static string Trim(string segment)
    {
        var open = segment.IndexOf('[');
        return open < 0 ? segment : segment[..open];
    }

    private const string AppMarkerProperty = "app";
    private const string VersionProperty = "version";
    private const string ItemsProperty = "items";

    /// <summary>Looks a property up without caring about case, since the file is meant to be hand-editable.</summary>
    private static bool TryGet(JsonElement obj, string name, out JsonElement value)
    {
        foreach (var property in obj.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Every reason the file cannot be restored, checked against <paramref name="today"/> in UTC+8.
    /// All of them are reported rather than the first, because the fix is usually the same edit for
    /// each one and a file is replaced as a whole — a partial import is not possible.
    ///
    /// Never throws for a file that parsed: anything unusable in it is a problem, not an exception,
    /// because the callers run on the UI thread and one of them is an <c>async void</c> click handler.
    /// </summary>
    public IReadOnlyList<string> Validate(DateOnly today)
    {
        var problems = new List<string>();

        for (var position = 1; position <= Items.Count; position++)
        {
            // A JSON null is legal for a reference type, so an entry like this reaches validation
            // rather than being refused by the deserializer the way "items": [1] is. Skipping it is
            // not an option: the file would then restore fewer items than it lists and still report
            // success.
            if (Items[position - 1] is not { } item)
            {
                problems.Add($"item {position} is empty.");
                continue;
            }

            var label = string.IsNullOrWhiteSpace(item.Name) ? $"item {position}" : $"\"{item.Name}\"";

            if (string.IsNullOrWhiteSpace(item.Name))
            {
                problems.Add($"{label} has no name.");
            }
            else if (item.Name.Length > Item.NameMaxLength)
            {
                problems.Add($"{label} is {item.Name.Length} characters; the limit is {Item.NameMaxLength}.");
            }

            if (!Enum.IsDefined(item.Kind))
            {
                problems.Add($"{label} has an unknown kind \"{item.Kind}\"; expected Daily or Task.");
                continue;
            }

            if (item.CreatedOn > today)
            {
                problems.Add($"{label} was created on {item.CreatedOn:yyyy-MM-dd}, which is in the future.");
            }
            else if (item.CreatedOn < EarliestTrackableDay)
            {
                // Bounds every other date in the item too: completedOn is checked against createdOn
                // below, and so is each completion day.
                problems.Add(
                    $"{label} was created on {item.CreatedOn:yyyy-MM-dd}, which is before the earliest day " +
                    $"this tracker records ({EarliestTrackableDay:yyyy-MM-dd}).");
            }

            if (item.Kind == ItemKind.Task)
            {
                if (item.Completions.Count > 0)
                {
                    problems.Add($"{label} is a task, so it cannot have daily completions.");
                }

                if (item.CompletedOn is { } done)
                {
                    if (done > today)
                    {
                        problems.Add($"{label} was completed on {done:yyyy-MM-dd}, which is in the future.");
                    }
                    else if (done < item.CreatedOn)
                    {
                        problems.Add(
                            $"{label} was completed on {done:yyyy-MM-dd}, before it was created on {item.CreatedOn:yyyy-MM-dd}.");
                    }
                }

                continue;
            }

            if (item.CompletedOn is not null)
            {
                problems.Add($"{label} is a daily, so it cannot carry a single completion date.");
            }

            foreach (var date in item.Completions.Where(d => d > today))
            {
                problems.Add($"{label} is marked complete on {date:yyyy-MM-dd}, which is in the future.");
            }

            foreach (var date in item.Completions.Where(d => d < item.CreatedOn))
            {
                problems.Add(
                    $"{label} is marked complete on {date:yyyy-MM-dd}, before it was created on {item.CreatedOn:yyyy-MM-dd}.");
            }
        }

        return problems;
    }
}

/// <summary>Either a parsed <see cref="BackupFile"/> or the one reason it could not be read.</summary>
public sealed class TryParseResult
{
    private TryParseResult(BackupFile? backup, string? failure)
    {
        Backup = backup;
        Failure = failure;
    }

    public BackupFile? Backup { get; }

    public string? Failure { get; }

    public bool Succeeded => Backup is not null;

    public static TryParseResult Ok(BackupFile backup) => new(backup, null);

    public static TryParseResult Fail(string reason) => new(null, reason);
}
