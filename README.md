# Habit Tracker

A single-user desktop habit tracker built with **WPF on .NET 8** and backed by **MySQL 8**.

It tracks two kinds of item:

- **Dailies** — habits that repeat every day. Each one has a streak.
- **Tasks / Objectives** — one-off items that never repeat. Once done, they stay done and never
  come back as pending. Tasks have no streak.

Completion is binary for both: done or not done. There are no numeric targets or partial credit.

There is no login and no user table. Everything in the database belongs to the one person running
the app.

---

## The streak rules

A daily's streak is the number of consecutive days it has been completed. **Missing a single day
breaks the streak and resets it to zero.**

The visual state of a daily is driven by how many *consecutive days immediately before today* went
uncompleted:

| Consecutive missed days | State |
|---|---|
| 0 | On track — normal appearance |
| 1 | Streak broken, **amber** — at risk, but not red |
| 2 or more | **Red** |

Two details that are easy to get wrong and are covered by tests:

- **The indication is strictly per item.** A missed daily tints only its own row. Other habits never
  change because a different habit was missed.
- **A streak does not collapse just because today is unfinished.** Open the app in the morning
  before doing anything and yesterday's streak is still displayed. It only breaks once a full day
  has genuinely passed with nothing recorded.

### Backdating

Any past day can be recorded as complete through a daily's **History** dialog, with no cutoff
window. Because the streak is derived rather than stored, filling a gap **repairs a streak that had
already broken**, and removing a backfilled day recomputes it back down.

A completion can never be recorded before the day the daily was created, or for a future day.

### Why a fixed UTC+8 offset

"One day" is resolved against a **fixed UTC+8 offset** — not a named timezone, and not the machine's
local clock. Every date boundary, missed-day count and streak calculation uses that single offset,
so the app behaves identically regardless of the PC's region settings or clock changes.

A fixed offset is used deliberately instead of a timezone name: UTC+8 observes no daylight saving,
so a day is always exactly 24 hours and no instant can land on an ambiguous or repeated local time.

---

## How the streak is derived

This is the part worth understanding. **There is no streak counter in the database.** Nothing is
incremented on completion or decremented on a miss. A streak is recomputed from the completion
history every time it is displayed, by `HabitTracker.Core/Domain/StreakCalculator.cs`:

```csharp
// Anchor on today if it is complete, otherwise yesterday; anything older means the streak is broken.
DateOnly? anchor = completions.Contains(today) ? today
                 : completions.Contains(today.AddDays(-1)) ? today.AddDays(-1)
                 : null;
if (anchor is null) return 0;

// Walk backwards for as long as the days are consecutive.
int streak = 0;
for (var day = anchor.Value; completions.Contains(day); day = day.AddDays(-1))
    streak++;
```

Missed days are counted separately, walking back from yesterday and **stopping at the item's
creation day**, so a daily is never treated as missed for days before it existed:

```csharp
int missed = 0;
for (var day = today.AddDays(-1); day >= createdOn && !completions.Contains(day); day = day.AddDays(-1))
    missed++;
```

Deriving instead of storing gives three things for free:

1. **Backdating repairs streaks automatically** — insert a date, recompute, done. No special case.
2. **A displayed streak can never disagree with the history.** If they somehow diverge, the history
   wins, because the history is the only thing being read.
3. **No scheduled job is needed.** Missed-day state is computed on read, so streaks are still
   correct after the app has been closed for a week.

The calculator is a pure function: it takes the completion set, the creation day and `today`, and
returns a result. It touches no database and no clock, which is why the 29 Core tests run in about
50 milliseconds — and why the repository layer can be replaced by an in-memory fake for the 77
view-model and brush tests without either group needing MySQL.

---

## Database schema

**`Items`**

| Column | Type | Notes |
|---|---|---|
| `Id` | `int` PK, auto-increment | |
| `Name` | `varchar(255)` not null | duplicates are allowed and tracked independently |
| `Kind` | `tinyint unsigned` | `0` = Daily, `1` = Task |
| `CreatedOn` | `date` | UTC+8 calendar day; bounds how far back missed days count |
| `CompletedOn` | `date` null | Tasks only — the day the objective was completed |

**`Completions`** (dailies only)

| Column | Type | Notes |
|---|---|---|
| `Id` | `int` PK, auto-increment | |
| `ItemId` | `int` FK → `Items.Id` | `ON DELETE CASCADE` |
| `Date` | `date` | UTC+8 calendar day |

Two constraints do real work here:

- A **unique index on `(ItemId, Date)`** makes marking the same day twice impossible at the database
  level, not merely in application code. A double-click can never advance a streak twice.
- **`ON DELETE CASCADE`** means deleting a daily takes its completion history with it, so no
  orphaned rows are left behind.

A task's completion lives on its own `Items` row as `CompletedOn`; tasks never get `Completions` rows.

---

## Backup and restore

**Export** and **Import** are the first two controls in the header, at the top left of the window.

Export writes the entire saved state — every item, its creation day, and every completed day — to a
JSON file you choose. Import reads one back and **replaces everything currently stored**, after
showing how many items and completed days are on each side. Nothing is merged and there is no undo,
so export the current state before importing something you might want to compare against.

```json
{
  "app": "Habit Tracker",
  "version": 1,
  "exportedOn": "2026-09-24",
  "items": [
    { "name": "Meditate", "kind": "Daily", "createdOn": "2026-09-19",
      "completions": ["2026-09-21", "2026-09-22", "2026-09-23"] },
    { "name": "Renew passport", "kind": "Task", "createdOn": "2026-09-01",
      "completedOn": "2026-09-05" }
  ]
}
```

Dates are plain UTC+8 calendar days and `kind` is spelled out, so the file is readable and safe to
edit by hand. Ids are deliberately absent: they belong to whichever database wrote them, so a
restored item always takes a fresh one. Because streaks are derived rather than stored, a restored
daily shows its real streak the moment it is read — the file only has to carry the days.

A file is importable in full or not at all. Import refuses, and changes nothing, when the `app`
marker or `version` is missing or unknown, when the JSON is malformed, when an entry in `items` is
empty, or when any item has no name, an unknown kind, a date in the future, a completion earlier than
the day the item was created, or a task carrying daily completions.

That strictness applies to each item's keys as well as to their values. A member name the format does
not define is refused rather than skipped, and so is an item that leaves out `name`, `kind` or
`createdOn`. Both would otherwise fall back to a C# default and restore an item that looks plausible:
a misspelt `"knd"`, or a dropped `kind`, would quietly turn a task into a daily with no history. Case
is not a problem — `"Name"`, `"name"` and `"NAME"` are the same key — so a file re-serialised by
another tool still imports, and a `null` day list reads as an empty one, exactly as leaving
`completions` out does.

Every problem is named at once, because the file is usually repaired with one edit rather than
several attempts. The swap itself is a single MySQL transaction, so a failure halfway through leaves
the previous data where it was.

---

## Project layout

```
HabitTracker.sln
├─ HabitTracker.Core    net8.0 class library
│  ├─ Domain/           Item, Completion, ItemKind, ItemFlag, Utc8Clock, StreakCalculator
│  ├─ Data/             HabitDbContext, HabitRepository, EnvLoader, DbContextFactory
│  └─ Backup/           BackupFile — the JSON export format, its parser and its validation
├─ HabitTracker.App     net8.0-windows WPF
│  ├─ ViewModels/       MainViewModel, ItemViewModel, HistoryViewModel, column filters
│  ├─ Views/            MainWindow, HistoryDialog
│  └─ Converters/       flag → card tint and accent, action-square state, tab enum match
├─ HabitTracker.Tests   net8.0 xUnit — StreakCalculator and Utc8Clock
└─ HabitTracker.App.Tests  net8.0-windows xUnit — the view models, against an in-memory repository
```

The domain and streak logic live in `HabitTracker.Core` rather than in the WPF project so they can
be unit-tested without referencing WPF or opening a database connection. The view models still need
a Windows target, which is why `HabitTracker.App.Tests` is `net8.0-windows`: it drives
`MainViewModel` and `ItemViewModel` directly through a fake `IHabitRepository`, so banner behaviour
and the per-item flags are checked without launching the UI or touching MySQL.

**Stack:** EF Core 8 with the Pomelo MySQL provider (code-first migrations), CommunityToolkit.Mvvm
for the MVVM layer, DotNetEnv for configuration, xUnit for tests.

---

## Prerequisites

- .NET SDK 8.0 or newer (a newer SDK builds the `net8.0` target fine)
- MySQL Server 8.x, running locally
- Windows, for the WPF UI

## Setup

**1. Create the database and application user**

Edit `setup.sql` and replace `CHANGE_ME` with a local password for the `habit_user` account. Then:

```bash
mysql -u <your-admin-user> -p < setup.sql
```

This creates the `habit_tracker` database and a dedicated `habit_user` restricted to it. Afterwards,
put `CHANGE_ME` back in `setup.sql`: the real password should live only in `.env`, which is
gitignored, not in a file that gets shared.

**2. Configure the connection string**

```bash
cp .env.example .env
```

Then edit `.env` so the password is the same value you put in `setup.sql`. `.env` is gitignored. The app
and the EF tooling both read the same `HABIT_CONNECTION` value, and both search upward from their
own directory to find `.env`, so it works whether you launch via `dotnet run` or by
double-clicking the exe. The search stops at the nearest `.env` that actually sets
`HABIT_CONNECTION`, so an unrelated `.env` in a subfolder is stepped over rather than mistaken for
yours. A `HABIT_CONNECTION` already present in the environment wins over any file, which is what
lets a script point a single run at another database — and means a stale exported value will shadow
the file.

**3. Build and run**

```bash
dotnet build
dotnet run --project HabitTracker.App
```

Migrations are applied automatically on startup, so the tables are created the first time the app
connects. `dotnet-ef` 8.0.13 is pinned in the local tool manifest, so to apply them by hand instead:

```bash
dotnet tool restore
dotnet ef database update --project HabitTracker.Core
```

## Tests

```bash
dotnet test
```

106 tests across the two projects. `HabitTracker.Tests` (Core only, `net8.0`) covers each acceptance
criterion: streak breaks on one missed day, red only at two, the morning-open case, the creation-day
boundary, backdating repairing and then recomputing down, and the UTC+8 midnight boundary.
`HabitTracker.App.Tests` (`net8.0-windows`) covers what lives in the WPF layer: the status banner
(an error from an offline database must not outlive the outage; a failed reload after adding or
toggling must be reported rather than a clean success), the same rule inside the History dialog —
including an outage while it is loading, which used to end the process — the per-item flags and
wording, tasks never going red regardless of age, the amber-versus-red brush values themselves, and
export/import — the round trip through a real file, and every rejection listed above proving that a
refused file leaves the stored data untouched.

Each of those tests was checked by mutation — reintroducing the original bug makes exactly the
matching tests fail, so the suite is known to be capable of failing rather than merely green.

Four flows stay outside automated coverage because they open a modal no test host can click: the
delete confirmation, the History dialog, and the two file pickers plus the import confirmation. Those
dialogs only pick a path, pick a date and answer yes or no — the work behind them runs through the
History view model's load and mutate methods, and through `ExportAsync`, `PreviewImportAsync` and
`CommitImportAsync`, all of which the tests drive directly.

---

## Files

| File | Purpose |
|---|---|
| `setup.sql` | Creates the database and the dedicated MySQL user; ships with a `CHANGE_ME` placeholder instead of a password |
| `.env.example` | Committed template for the connection string |
| `.env` | Your real connection string — gitignored, never committed |
| `.gitignore` | Excludes `.env`, `bin/`, `obj/` and IDE files |
| `dotnet-tools.json` | Pins the `dotnet-ef` version used for the migration above |
