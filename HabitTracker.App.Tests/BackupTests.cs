using System.IO;
using HabitTracker.App.ViewModels;
using HabitTracker.Core.Backup;
using HabitTracker.Core.Data;
using HabitTracker.Core.Domain;

namespace HabitTracker.App.Tests;

/// <summary>
/// The JSON backup: what a file may contain, and what happens when one is restored over a database
/// that already has data. Import replaces everything, so these tests pin both the guarantee (a good
/// file restores exactly what it held) and the protection (a bad file changes nothing at all).
/// </summary>
public class BackupTests
{
    private static readonly DateOnly Today = FakeHabitRepository.Today;

    private static DateOnly Day(int daysAgo) => FakeHabitRepository.Day(daysAgo);

    // ----------------------------------------------------------------------- export

    [Fact]
    public async Task Export_CapturesEveryItemWithItsOwnCompletionDays()
    {
        var repository = new FakeHabitRepository();
        repository.SeedDaily(Day(9), Day(2), Day(1), Day(0));
        repository.SeedTask(Day(1));

        var path = TempJson.Path;
        try
        {
            await ViewModel(repository).ExportAsync(path);

            var backup = BackupFile.TryParse(await File.ReadAllTextAsync(path)).Backup!;
            Assert.Equal(2, backup.Items.Count);
            Assert.Equal(ItemKind.Daily, backup.Items[0].Kind);
            Assert.Equal(new[] { Day(2), Day(1), Day(0) }, backup.Items[0].Completions);
            Assert.Equal(Day(1), backup.Items[1].CompletedOn);
            Assert.Empty(backup.Items[1].Completions);
        }
        finally
        {
            TempJson.Cleanup(path);
        }
    }

    [Fact]
    public async Task Export_WithAnUnreachableDatabase_ReportsItAndWritesNoFile()
    {
        var repository = new FakeHabitRepository { FailReads = true };
        repository.SeedDaily(Day(1), Day(1));
        var path = TempJson.Path;

        var vm = ViewModel(repository);
        await vm.ExportAsync(path);

        Assert.Contains("Could not write the export", vm.StatusMessage);
        Assert.False(File.Exists(path));
    }

    // ---------------------------------------------------------------------- round trip

    [Fact]
    public async Task RoundTrip_RestoresASecondDatabaseToExactlyWhatTheFileHeld()
    {
        var source = new FakeHabitRepository();
        source.SeedDaily(Day(9), Day(3), Day(2), Day(1));
        source.SeedTask(completedOn: null);

        var path = TempJson.Path;
        try
        {
            await File.WriteAllTextAsync(path, (await source.CreateBackupAsync()).ToJson());

            // A different database, holding different things.
            var target = new FakeHabitRepository();
            target.SeedDaily(Day(20), Day(20));
            target.SeedDaily(Day(1));
            var vm = ViewModel(target);
            await vm.RefreshAsync();

            var plan = await vm.PreviewImportAsync(path);
            Assert.NotNull(plan);
            await vm.CommitImportAsync(plan!);

            var restored = await target.GetItemsAsync();
            Assert.Equal(new[] { "Seeded daily", "Seeded task" }, restored.Select(i => i.Name));

            // Streaks are derived, so the restored daily shows its history without being touched:
            // yesterday complete, the day before that, and the day before that again.
            Assert.Equal("3-day streak", vm.Dailies[0].StatusText);
            Assert.Equal(ItemFlag.OnTrack, vm.Dailies[0].Flag);
            Assert.Equal("Pending", vm.Tasks[0].StatusText);
            Assert.Contains("Imported 2 items", vm.StatusMessage);
        }
        finally
        {
            TempJson.Cleanup(path);
        }
    }

    [Fact]
    public async Task Import_DuplicateDaysInTheFile_CollapseIntoOneCompletion()
    {
        var backup = new BackupFile
        {
            ExportedOn = Today,
            Items =
            {
                new BackupItem
                {
                    Name = "Stretch",
                    Kind = ItemKind.Daily,
                    CreatedOn = Day(5),
                    Completions = new List<DateOnly> { Day(1), Day(1), Day(1) },
                },
            },
        };

        var repository = new FakeHabitRepository();
        var vm = ViewModel(repository);
        var path = await WriteBackupFileAsync(backup);
        try
        {
            var plan = await vm.PreviewImportAsync(path);
            Assert.NotNull(plan);
            await vm.CommitImportAsync(plan!);

            var items = await repository.GetItemsAsync();
            Assert.Single(repository.DatesFor(items[0].Id));
            Assert.Equal("1-day streak", vm.Dailies[0].StatusText);
        }
        finally
        {
            TempJson.Cleanup(path);
        }
    }

    [Fact]
    public async Task Import_AnEmptyBackup_IsAcceptedAndClearsEverything()
    {
        // The file is the truth, so a backup taken from an empty database really is a restore to
        // empty. The confirmation names both counts before this is reachable in the UI.
        var repository = new FakeHabitRepository();
        repository.SeedDaily(Day(1), Day(1));
        var vm = ViewModel(repository);
        await vm.RefreshAsync();

        var path = await WriteBackupFileAsync(new BackupFile { ExportedOn = Today });
        try
        {
            var plan = await vm.PreviewImportAsync(path);
            Assert.NotNull(plan);
            Assert.Equal(1, plan!.CurrentItems);

            await vm.CommitImportAsync(plan);

            Assert.Empty(vm.Dailies);
            Assert.Empty(vm.Tasks);
            Assert.Contains("Imported 0 items", vm.StatusMessage);
        }
        finally
        {
            TempJson.Cleanup(path);
        }
    }

    [Fact]
    public async Task Import_AFileWhoseKeysAreReCased_RestoresItInsteadOfEmptyingTheDatabase()
    {
        // A hand-written or re-cased file. The guards that read the raw document match keys
        // case-insensitively, so the deserializer has to as well: when it did not, every property was
        // dropped, the file validated as holding zero problems, and confirming the "with 0 items"
        // prompt emptied both tables.
        var repository = new FakeHabitRepository();
        repository.SeedDaily(Day(2), Day(2));
        var vm = ViewModel(repository);
        await vm.RefreshAsync();

        var json = $$"""
            {
              "App": "Habit Tracker",
              "Version": 1,
              "ExportedOn": "{{Day(0):yyyy-MM-dd}}",
              "Items": [
                { "Name": "Meditate", "Kind": "Daily", "CreatedOn": "{{Day(9):yyyy-MM-dd}}",
                  "CompletedOn": null,
                  "Completions": ["{{Day(2):yyyy-MM-dd}}", "{{Day(1):yyyy-MM-dd}}"] },
                { "Name": "Renew passport", "Kind": "Task", "CreatedOn": "{{Day(4):yyyy-MM-dd}}",
                  "CompletedOn": "{{Day(3):yyyy-MM-dd}}", "Completions": [] }
              ]
            }
            """;

        var path = TempJson.Path;
        try
        {
            await File.WriteAllTextAsync(path, json);

            var plan = await vm.PreviewImportAsync(path);
            Assert.NotNull(plan);
            Assert.Equal(2, plan!.Backup.Items.Count);
            Assert.Equal(ItemKind.Daily, plan.Backup.Items[0].Kind);
            Assert.Equal(ItemKind.Task, plan.Backup.Items[1].Kind);

            await vm.CommitImportAsync(plan);

            // The task stayed a task and the daily kept its history, rather than both arriving as
            // dailies with nothing recorded.
            var stored = await repository.GetItemsAsync();
            Assert.Equal(new[] { "Meditate", "Renew passport" }, stored.Select(i => i.Name));
            Assert.Equal(Day(3), stored[1].CompletedOn);

            var completions = await repository.GetCompletionDatesAsync();
            Assert.Equal(new[] { Day(1), Day(2) }, completions[stored[0].Id].OrderByDescending(d => d));
            Assert.Contains("Imported 2 items", vm.StatusMessage);
        }
        finally
        {
            TempJson.Cleanup(path);
        }
    }

    [Theory]
    [InlineData(ItemKind.Daily)]
    [InlineData(ItemKind.Task)]
    public async Task Import_AnItemWhoseCompletionsAreNull_IsRestoredWithNoHistory(ItemKind kind)
    {
        // A null day list and an absent one mean the same thing, so the null is read as empty rather
        // than refused. Both branches of the validator used to look at the list without asking — the
        // task branch on Count, the daily branch inside Where — and each threw out of the preview
        // instead of reporting anything.
        var repository = new FakeHabitRepository();
        var vm = ViewModel(repository);
        await vm.RefreshAsync();

        var json = $$"""
            {
              "app": "Habit Tracker",
              "version": 1,
              "exportedOn": "{{Day(0):yyyy-MM-dd}}",
              "items": [
                { "name": "Read", "kind": "{{kind}}", "createdOn": "{{Day(4):yyyy-MM-dd}}",
                  "completions": null }
              ]
            }
            """;

        var path = TempJson.Path;
        try
        {
            await File.WriteAllTextAsync(path, json);

            var plan = await vm.PreviewImportAsync(path);
            Assert.NotNull(plan);
            await vm.CommitImportAsync(plan!);

            var stored = await repository.GetItemsAsync();
            Assert.Single(stored);
            Assert.Equal(kind, stored[0].Kind);
            Assert.Empty(repository.DatesFor(stored[0].Id));
            Assert.Contains("Imported 1 item ", vm.StatusMessage);
        }
        finally
        {
            TempJson.Cleanup(path);
        }
    }

    // ----------------------------------------------------------------- rejection paths

    [Theory]
    [InlineData("{}", "not a Habit Tracker backup")]
    [InlineData("""{"app":"Some Other App","version":1,"items":[]}""", "not a Habit Tracker backup")]
    [InlineData("""{"app":"Habit Tracker","items":[]}""", "no format version")]
    [InlineData("""{"app":"Habit Tracker","version":"one","items":[]}""", "no format version")]
    [InlineData("""{"app":"Habit Tracker","version":7,"items":[]}""", "version 7")]
    [InlineData("""{"app":"Habit Tracker","version":1}""", "no \"items\" list")]
    [InlineData("[]", "not a JSON object")]
    [InlineData("this is not json at all", "not valid JSON")]
    // A member name the format does not define used to be ignored, so a misspelt "kind" quietly
    // restored a task as a daily; a missing one fell back to the same default.
    [InlineData("""{"app":"Habit Tracker","version":1,"items":[{"name":"Renew passport","knd":"Task","createdOn":"2026-09-01"}]}""", "knd")]
    [InlineData("""{"app":"Habit Tracker","version":1,"items":[{"name":"Renew passport","createdOn":"2026-09-01"}]}""", "Kind")]
    // An empty file, and a marker of the wrong type: reading a marker that is not text used to throw
    // out of the parser rather than being refused.
    [InlineData("", "file is empty")]
    [InlineData("   \n  ", "file is empty")]
    [InlineData("""{"app":{"name":"Habit Tracker"},"version":1,"items":[]}""", "not a Habit Tracker backup")]
    // The deserializer's own complaints, rewritten in the file's vocabulary. Each of these used to
    // report a .NET type name and a line and byte offset counted into text the user cannot see.
    [InlineData("""{"app":"Habit Tracker","version":1,"items":[1]}""", "item 1 is not an object")]
    [InlineData("""{"app":"Habit Tracker","version":1,"items":[{"name":"Read","kind":"Daily","createdOn":"yesterday"}]}""", "item 1's \"createdOn\" holds something that is not a calendar day")]
    [InlineData("""{"app":"Habit Tracker","version":1,"items":[{"name":"Read","kind":"Daily","createdOn":"2026-09-01","completions":["2026-09-02","nope"]}]}""", "not a calendar day")]
    [InlineData("""{"app":"Habit Tracker","version":1,"items":[{"name":"Read","kind":"Daily","createdOn":"2026-09-01","completions":{}}]}""", "is not a list of days")]
    [InlineData("""{"app":"Habit Tracker","version":1,"items":[{"name":"Read","kind":"Weekly","createdOn":"2026-09-01"}]}""", "is not Daily or Task")]
    [InlineData("""{"app":"Habit Tracker","version":1,"items":[{"name":5,"kind":"Daily","createdOn":"2026-09-01"}]}""", "is not text")]
    [InlineData("""{"app":"Habit Tracker","version":1,"items":[{"name":"Read"}]}""", "\"kind\", \"createdOn\"")]
    [InlineData("""{"app":"Habit Tracker","version":1,"exportedOn":"today","items":[]}""", "exportedOn")]
    [InlineData("""{"app":"Habit Tracker","version":1,"items":[],"extra":1}""", "member the format does not define: \"extra\"")]
    // The reader's position is kept because it is real — it points into the file the user has open —
    // but reported as a line and character rather than in library notation.
    [InlineData("{\n  \"app\": \"Habit Tracker\",\n  \"version\": 1,\n  \"items\": [\n    { \"name\": \"Read\", \"kind\": Daily, \"createdOn\": \"2026-09-01\" }\n  ]\n}", "line 5")]
    [InlineData("""{"app":"Habit Tracker","version":1,}""", "trailing comma is not allowed")]
    public async Task Import_AFileThatIsNotOurs_IsRejectedWithAReason(string contents, string expectedInMessage)
    {
        // Each guard gets its own case: a file that merely parses is not evidence that the others
        // still work, and these are the messages a user reads instead of a stack trace.
        var repository = new FakeHabitRepository();
        repository.SeedDaily(Day(1), Day(1));
        var vm = ViewModel(repository);
        await vm.RefreshAsync();

        var path = TempJson.Path;
        await File.WriteAllTextAsync(path, contents);
        try
        {
            Assert.Null(await vm.PreviewImportAsync(path));

            Assert.Contains(expectedInMessage, vm.StatusMessage, StringComparison.OrdinalIgnoreCase);

            // And it stays in the file's own vocabulary. A message quoting .NET type names, or offsets
            // counted into text the user cannot see, or advice written for someone calling the library
            // is a stack trace with the punctuation changed.
            Assert.DoesNotContain("System.", vm.StatusMessage, StringComparison.Ordinal);
            Assert.DoesNotContain("HabitTracker.Core", vm.StatusMessage, StringComparison.Ordinal);
            Assert.DoesNotContain("LineNumber", vm.StatusMessage, StringComparison.Ordinal);
            Assert.DoesNotContain("BytePositionInLine", vm.StatusMessage, StringComparison.Ordinal);
            Assert.DoesNotContain("reader options", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);

            // Nothing to say about what the file is: it declared itself ours above, so a preamble
            // about it would only push the part that helps — which item, which field — further out.
            Assert.DoesNotContain("not a backup this build understands", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);

            var untouched = await repository.GetItemsAsync();
            Assert.Single(untouched);
        }
        finally
        {
            TempJson.Cleanup(path);
        }
    }

    [Fact]
    public async Task Import_AFutureCompletion_IsRejectedAndChangesNothing()
    {
        var backup = BackupWith(new BackupItem
        {
            Name = "Read",
            Kind = ItemKind.Daily,
            CreatedOn = Day(3),
            Completions = new List<DateOnly> { Today.AddDays(1) },
        });

        await AssertRejectedLeavesDatabaseAlone(backup, "in the future");
    }

    [Fact]
    public async Task Import_ACompletionOlderThanTheItem_IsRejected()
    {
        // Rule 12 again, on the import side: a file must not be able to create history that predates
        // the daily, because missed days are counted from CreatedOn.
        var backup = BackupWith(new BackupItem
        {
            Name = "Read",
            Kind = ItemKind.Daily,
            CreatedOn = Day(3),
            Completions = new List<DateOnly> { Day(9) },
        });

        await AssertRejectedLeavesDatabaseAlone(backup, "before it was created");
    }

    [Fact]
    public async Task Import_ATaskWithDailyCompletions_IsRejected()
    {
        var backup = BackupWith(new BackupItem
        {
            Name = "Book flights",
            Kind = ItemKind.Task,
            CreatedOn = Day(3),
            Completions = new List<DateOnly> { Day(1) },
        });

        await AssertRejectedLeavesDatabaseAlone(backup, "cannot have daily completions");
    }

    [Fact]
    public async Task Import_ADailyWithASingleCompletionDate_IsRejected()
    {
        var backup = BackupWith(new BackupItem
        {
            Name = "Read",
            Kind = ItemKind.Daily,
            CreatedOn = Day(3),
            CompletedOn = Day(1),
        });

        await AssertRejectedLeavesDatabaseAlone(backup, "cannot carry a single completion date");
    }

    [Fact]
    public async Task Import_AnItemCreatedInTheFuture_IsRejected()
    {
        var backup = BackupWith(new BackupItem { Name = "Someday", Kind = ItemKind.Daily, CreatedOn = Today.AddDays(2) });

        await AssertRejectedLeavesDatabaseAlone(backup, "which is in the future");
    }

    [Fact]
    public async Task Import_ATaskCompletedBeforeItWasCreated_IsRejected()
    {
        var backup = BackupWith(new BackupItem
        {
            Name = "Book flights",
            Kind = ItemKind.Task,
            CreatedOn = Day(2),
            CompletedOn = Day(6),
        });

        await AssertRejectedLeavesDatabaseAlone(backup, "before it was created");
    }

    [Fact]
    public async Task Import_ANamelessItem_IsRejected()
    {
        var backup = BackupWith(new BackupItem { Name = "   ", Kind = ItemKind.Daily, CreatedOn = Day(1) });

        await AssertRejectedLeavesDatabaseAlone(backup, "has no name");
    }

    [Fact]
    public async Task Import_ANullEntryInTheItemsList_IsRejectedAndChangesNothing()
    {
        // "items": [null] is legal JSON for a reference type, so unlike "items": [1] this entry
        // survives parsing and validation used to dereference it — throwing out of an async void
        // click handler and ending the process with no message at all. Skipping it instead would be
        // worse than the crash: the file would restore fewer items than it lists and still report a
        // successful import.
        var backup = new BackupFile { ExportedOn = Today };
        backup.Items.Add(null!);

        await AssertRejectedLeavesDatabaseAlone(backup, "item 1 is empty");
    }

    [Fact]
    public async Task Import_SaysHowManyOtherProblemsTheFileHas()
    {
        var backup = new BackupFile
        {
            ExportedOn = Today,
            Items =
            {
                new BackupItem { Name = "Future", Kind = ItemKind.Daily, CreatedOn = Day(1), Completions = new List<DateOnly> { Today.AddDays(2) } },
                new BackupItem { Name = "Also future", Kind = ItemKind.Daily, CreatedOn = Today.AddDays(3) },
                new BackupItem { Name = "Third", Kind = ItemKind.Daily, CreatedOn = Day(1), Completions = new List<DateOnly> { Today.AddDays(4) } },
            },
        };

        var repository = new FakeHabitRepository();
        var vm = ViewModel(repository);
        var path = await WriteBackupFileAsync(backup);
        try
        {
            Assert.Null(await vm.PreviewImportAsync(path));

            Assert.Contains("in the future", vm.StatusMessage);
            Assert.Contains("2 more problems", vm.StatusMessage);
        }
        finally
        {
            TempJson.Cleanup(path);
        }
    }

    [Fact]
    public async Task PreviewImport_AloneNeverTouchesTheStoredData()
    {
        // The view reads counts from this call to word its confirmation, so previewing must be
        // harmless: everything stays as it was until a commit is made.
        var repository = new FakeHabitRepository();
        repository.SeedDaily(Day(2), Day(2), Day(1));
        var vm = ViewModel(repository);
        await vm.RefreshAsync();
        var writesBefore = repository.Writes;

        var path = await WriteBackupFileAsync(await repository.CreateBackupAsync());
        try
        {
            var plan = await vm.PreviewImportAsync(path);

            Assert.NotNull(plan);
            Assert.Single(plan!.Backup.Items);
            Assert.Equal(1, plan.CurrentItems);
            Assert.Equal(2, plan.CurrentCompletions);
            Assert.Equal(writesBefore, repository.Writes);
            Assert.Equal("2-day streak", vm.Dailies[0].StatusText);
        }
        finally
        {
            TempJson.Cleanup(path);
        }
    }

    [Fact]
    public async Task PreviewImport_ReadsTheCountsFromMySQL_NotFromTheLoadedLists()
    {
        // The prompt words itself from this plan, and the loaded lists are only as fresh as the last
        // successful read. Counting from them claimed "0 items, 0 completed days" — nothing to lose —
        // while MySQL still held both items.
        var repository = new FakeHabitRepository();
        repository.SeedDaily(Day(3), Day(3), Day(2));
        repository.SeedDaily(Day(1), Day(1));

        var vm = ViewModel(repository);
        repository.FailReads = true;
        await vm.RefreshAsync();
        repository.FailReads = false;

        var path = await WriteBackupFileAsync(
            BackupWith(new BackupItem { Name = "Replacement", Kind = ItemKind.Daily, CreatedOn = Day(0) }));
        try
        {
            var plan = await vm.PreviewImportAsync(path);

            Assert.NotNull(plan);
            Assert.Equal(2, plan!.CurrentItems);
            Assert.Equal(3, plan.CurrentCompletions);

            // The screen was never reloaded, so the lists are still blank: the counts above cannot
            // have come from them.
            Assert.Empty(vm.Dailies);
        }
        finally
        {
            TempJson.Cleanup(path);
        }
    }

    [Fact]
    public async Task PreviewImport_WithAnUnreachableDatabase_RefusesToAskAndChangesNothing()
    {
        // The cache holds data here, so a count taken from it would have looked correct — the read
        // itself has to be the thing that fails, and the app must not ask about a replace it cannot
        // size. An unanswered prompt is not an option, but a misleading one is worse.
        var repository = new FakeHabitRepository();
        repository.SeedDaily(Day(2), Day(2), Day(1));
        var vm = ViewModel(repository);
        await vm.RefreshAsync();
        Assert.Single(vm.Dailies);

        var path = await WriteBackupFileAsync(
            BackupWith(new BackupItem { Name = "Replacement", Kind = ItemKind.Daily, CreatedOn = Day(0) }));
        try
        {
            var writesBefore = repository.Writes;
            repository.FailReads = true;

            var plan = await vm.PreviewImportAsync(path);

            Assert.Null(plan);
            Assert.Contains("Could not check what is stored in MySQL", vm.StatusMessage);
            Assert.Contains(repository.ReadFailure, vm.StatusMessage);
            Assert.Equal(writesBefore, repository.Writes);
        }
        finally
        {
            TempJson.Cleanup(path);
        }
    }

    [Fact]
    public async Task Import_WhenTheRepositoryThrows_ReportsNothingWasChanged()
    {
        // Pins the message and the view model's handling of a repository failure. The fake throws
        // before it clears anything, so this cannot pin ReplaceAllAsync's transaction — the fake is
        // atomic by construction. That rollback is proven against MySQL instead, by failing the second
        // statement after the first has already run inside the transaction.
        var repository = new FakeHabitRepository();
        repository.SeedDaily(Day(2), Day(2), Day(1));
        var vm = ViewModel(repository);
        await vm.RefreshAsync();

        var path = await WriteBackupFileAsync(BackupWith(new BackupItem { Name = "New only", Kind = ItemKind.Daily, CreatedOn = Day(1) }));
        try
        {
            var plan = await vm.PreviewImportAsync(path);
            Assert.NotNull(plan);

            repository.FailWrites = true;
            await vm.CommitImportAsync(plan!);

            Assert.Contains("nothing was changed", vm.StatusMessage);
            var stillThere = await repository.GetItemsAsync();
            Assert.Equal(new[] { "Seeded daily" }, stillThere.Select(i => i.Name));
        }
        finally
        {
            TempJson.Cleanup(path);
        }
    }

    // ------------------------------------------------------------------------------ date floor

    [Theory]
    [InlineData("0001-01-01")]
    [InlineData("1899-12-31")]
    [InlineData("1001-01-01")]
    public async Task Import_ADailyCreatedBeforeTheFloor_IsRejectedAndChangesNothing(string createdOn)
    {
        // The middle row is the realistic one and the reason the floor is a plausibility rule rather
        // than a storage one: a year typed as 1001 instead of 2026 validated cleanly, MySQL stored it,
        // and the resulting card claimed hundreds of thousands of missed days — or, at MinValue, threw
        // out of the rebuild and left *every* card in the window unloadable.
        var backup = BackupWith(new BackupItem
        {
            Name = "Ancient",
            Kind = ItemKind.Daily,
            CreatedOn = DateOnly.Parse(createdOn),
        });

        await AssertRejectedLeavesDatabaseAlone(backup, createdOn);
    }

    [Fact]
    public async Task Import_ADailyCreatedExactlyOnTheFloor_IsAccepted()
    {
        var backup = BackupWith(new BackupItem
        {
            Name = "Old but real",
            Kind = ItemKind.Daily,
            CreatedOn = BackupFile.EarliestTrackableDay,
        });

        var repository = new FakeHabitRepository();
        var vm = ViewModel(repository);
        var path = await WriteBackupFileAsync(backup);
        try
        {
            var plan = await vm.PreviewImportAsync(path);

            Assert.NotNull(plan);
            await vm.CommitImportAsync(plan!);
            Assert.Equal("Old but real", Assert.Single(vm.Dailies).Name);
        }
        finally
        {
            TempJson.Cleanup(path);
        }
    }

    // ------------------------------------------------------------------------------ helpers

    private static BackupFile BackupWith(params BackupItem[] items)
    {
        var backup = new BackupFile { ExportedOn = Today };
        backup.Items.AddRange(items);
        return backup;
    }

    private static async Task AssertRejectedLeavesDatabaseAlone(BackupFile backup, string expectedInMessage)
    {
        var repository = new FakeHabitRepository();
        repository.SeedDaily(Day(2), Day(2), Day(1));
        var vm = ViewModel(repository);
        await vm.RefreshAsync();
        var writesBefore = repository.Writes;

        var path = await WriteBackupFileAsync(backup);
        try
        {
            Assert.Null(await vm.PreviewImportAsync(path));

            Assert.Contains(expectedInMessage, vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(writesBefore, repository.Writes);

            // The list on screen is still what the database holds.
            Assert.Equal("Seeded daily", vm.Dailies[0].Name);
        }
        finally
        {
            TempJson.Cleanup(path);
        }
    }

    private static async Task<string> WriteBackupFileAsync(BackupFile backup)
    {
        var path = TempJson.Path;
        await File.WriteAllTextAsync(path, backup.ToJson());
        return path;
    }

    private static MainViewModel ViewModel(FakeHabitRepository repository) => new(repository);

    /// <summary>
    /// Scratch files live beside the test assembly rather than in a shared temp folder, so a test run
    /// only writes inside its own build output and leaves nothing behind.
    /// </summary>
    private static class TempJson
    {
        public static string Path => System.IO.Path.Combine(
            AppContext.BaseDirectory,
            $"backup-{Guid.NewGuid():N}.json");

        public static void Cleanup(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // A leftover scratch file in the build output is noise, not a test failure.
            }
        }
    }
}
