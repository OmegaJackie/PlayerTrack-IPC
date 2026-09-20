using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using PlayerTrack.Models;

namespace PlayerTrack.Domain;

public static class PlayerSearchService
{
    private const string DefaultKey = "";
    private const string BioKey = "bio";

    private enum MatchMode
    {
        Exact,
        Contains,
        StartsWith,
    }

    /// <summary>
    /// A search key. Compile turns the raw token value into a per-player predicate once
    /// (splitting alternatives, parsing comparators, snapshotting time), so none of that
    /// work happens inside the hot per-player loop.
    /// </summary>
    private sealed record SearchHandler(
        Func<string, bool, SearchType, Func<Player, bool>> Compile,
        Func<Player, bool> HasValue,
        Func<string, bool>? IsValidValue = null);

    private static readonly SearchHandler NameHandler = new(
        (value, isQuoted, searchType) =>
        {
            var matchers = BuildMatchers(value, isQuoted, ModeFor(searchType));
            return player => !string.IsNullOrEmpty(player.Name) && MatchesAny(matchers, player.Name);
        },
        player => !string.IsNullOrEmpty(player.Name));

    private static readonly Dictionary<string, SearchHandler> Handlers = BuildHandlers();

    private static Dictionary<string, SearchHandler> BuildHandlers()
    {
        var handlers = new Dictionary<string, SearchHandler>(StringComparer.OrdinalIgnoreCase);

        void Add(SearchHandler handler, params string[] keys)
        {
            foreach (var key in keys)
                handlers[key] = handler;
        }

        Add(NameHandler, "name");
        Add(TextHandler(p => p.FreeCompany.Value), "fc", "freecompany");
        Add(TextHandler(p => p.Notes, MatchMode.Contains), "notes", "note");
        Add(TextHandler(p => p.RaceName()), "race");
        Add(TextHandler(p => p.TribeName()), "tribe", "clan");
        Add(TextHandler(p => p.GenderName()), "gender");
        Add(TextHandler(p => p.WorldName()), "world");
        Add(TextHandler(p => p.DataCenterName()), "dc", "datacenter");

        Add(CollectionHandler(p => p.AssignedTags.Select(tag => tag.Name), p => p.AssignedTags.Count > 0), "tags", "tag");
        Add(CollectionHandler(p => p.AssignedCategories.Select(category => category.Name), p => p.AssignedCategories.Count > 0), "category", "cat");
        Add(CollectionHandler(p => p.PreviousNames, p => p.PreviousNames.Length > 0), "prevname", "previousname", "oldname");
        Add(CollectionHandler(p => p.PreviousWorlds, p => p.PreviousWorlds.Length > 0), "prevworld", "previousworld", "oldworld");

        Add(NumberHandler(p => p.Id), "id");
        Add(NumberHandler(p => p.SeenCount), "seen");
        Add(AgeHandler(p => p.FirstSeen), "firstseen");
        Add(AgeHandler(p => p.LastSeen), "lastseen");
        Add(LodestoneHandler(), "lodestone");

        return handlers;
    }

    private static SearchHandler TextHandler(Func<Player, string> selector, MatchMode mode = MatchMode.Exact) =>
        new(
            (value, isQuoted, _) =>
            {
                var matchers = BuildMatchers(value, isQuoted, mode);
                return player =>
                {
                    var field = selector(player);
                    return !string.IsNullOrEmpty(field) && MatchesAny(matchers, field);
                };
            },
            player => !string.IsNullOrEmpty(selector(player)));

    private static SearchHandler CollectionHandler(Func<Player, IEnumerable<string>> selector, Func<Player, bool> hasValue) =>
        new(
            (value, isQuoted, _) =>
            {
                var matchers = BuildMatchers(value, isQuoted, MatchMode.Exact);
                return player =>
                {
                    foreach (var field in selector(player))
                        if (!string.IsNullOrEmpty(field) && MatchesAny(matchers, field))
                            return true;

                    return false;
                };
            },
            hasValue);

    private static SearchHandler NumberHandler(Func<Player, long> selector) =>
        new(
            (value, _, _) =>
            {
                if (!TryParseComparison(value, out var op, out var target))
                    return _ => false;

                return player => CompareNumber(selector(player), op, target);
            },
            player => selector(player) != 0,
            IsValidNumberValue);

    private static SearchHandler AgeHandler(Func<Player, long> selector) =>
        new(
            (value, _, _) =>
            {
                if (!TryParseAge(value, out var op, out var duration))
                    return _ => false;

                // Snapshot 'now' at compile time; filters are rebuilt on every search pass, so this stays fresh.
                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                return player =>
                {
                    var timestamp = selector(player);
                    return timestamp != 0 && CompareAge(now - timestamp, op, duration);
                };
            },
            player => selector(player) != 0,
            IsValidAgeValue);

    private static SearchHandler LodestoneHandler() =>
        new(
            (value, isQuoted, _) =>
            {
                var alternatives = SplitAlternatives(value, isQuoted);
                var ids = new List<uint>();
                var statusMatchers = new List<Func<string, bool>>();
                foreach (var alt in alternatives)
                {
                    if (alt.Length > 0 && !alt.Contains('*') && uint.TryParse(alt, out var id))
                        ids.Add(id);
                    else
                        statusMatchers.Add(BuildMatcher(alt, MatchMode.Contains));
                }

                var idArray = ids.ToArray();
                var statusArray = statusMatchers.ToArray();
                return player =>
                {
                    foreach (var id in idArray)
                        if (player.LodestoneId == id)
                            return true;

                    if (statusArray.Length > 0)
                    {
                        var status = player.LodestoneStatus.ToString();
                        if (MatchesAny(statusArray, status))
                            return true;
                    }

                    return false;
                };
            },
            player => player.LodestoneId != 0);

    public static Func<Player, bool> GetSearchFilter(string searchString, SearchType searchType)
    {
        if (string.IsNullOrWhiteSpace(searchString))
            return _ => true;

        // Resolved lazily off the shared in-memory cache so the bio table is only touched when a bio: token exists.
        var bios = new Lazy<IReadOnlyDictionary<int, string>>(PlayerBioService.GetLatestBios);
        var filters = Tokenize(searchString).Select(token => CreateSingleFilter(token, searchType, bios)).ToList();
        return player => filters.All(f => f(player));
    }

    private static Func<Player, bool> CreateSingleFilter(string token, SearchType searchType, Lazy<IReadOnlyDictionary<int, string>> bios)
    {
        var (isNegated, key, value, isQuoted) = ParseSearchString(token);

        if (key.Equals(BioKey, StringComparison.OrdinalIgnoreCase))
            return BuildBioFilter(value, isQuoted, isNegated, bios);

        var handler = Handlers.GetValueOrDefault(key, NameHandler);
        Func<Player, bool> predicate;
        if (!isQuoted && value == "!")
        {
            var hasValue = handler.HasValue;
            predicate = player => !hasValue(player);
        }
        else
        {
            predicate = handler.Compile(value, isQuoted, searchType);
        }

        return isNegated ? player => !predicate(player) : predicate;
    }

    private static Func<Player, bool> BuildBioFilter(string value, bool isQuoted, bool isNegated, Lazy<IReadOnlyDictionary<int, string>> bios)
    {
        Func<Player, bool> predicate;
        if (!isQuoted && value == "!")
        {
            predicate = player => !(bios.Value.TryGetValue(player.Id, out var bio) && !string.IsNullOrEmpty(bio));
        }
        else
        {
            // Bios are free-form blobs, so wildcardless patterns match as contains rather than exact.
            var matchers = BuildMatchers(value, isQuoted, MatchMode.Contains);
            predicate = player => bios.Value.TryGetValue(player.Id, out var bio) && !string.IsNullOrEmpty(bio) && MatchesAny(matchers, bio);
        }

        return isNegated ? player => !predicate(player) : predicate;
    }

    private static (bool isNegated, string key, string value, bool isQuoted) ParseSearchString(string searchString)
    {
        var isGloballyNegated = searchString.StartsWith('!');
        var normalized = isGloballyNegated ? searchString[1..] : searchString;

        // Quoted tokens are always name searches; a colon inside quotes is literal text.
        if (!normalized.StartsWith('"'))
        {
            var parts = normalized.Split([':'], 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
            {
                var key = parts[0].ToLowerInvariant();
                var value = parts[1];
                var isValueNegated = value.StartsWith('!') && value.Length > 1; // a lone "!" is the has-no-value marker
                if (isValueNegated)
                    value = value[1..];

                var (unquoted, wasQuoted) = StripQuotes(value);
                return (isGloballyNegated ^ isValueNegated, key, unquoted, wasQuoted);
            }
        }

        var (defaultValue, defaultQuoted) = StripQuotes(normalized);
        return (isGloballyNegated, DefaultKey, defaultValue, defaultQuoted);
    }

    /// <summary>
    /// Splits on spaces while keeping double-quoted phrases together, e.g. notes:"red mage".
    /// </summary>
    private static List<string> Tokenize(string searchString)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        foreach (var c in searchString)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                current.Append(c);
            }
            else if (c == ' ' && !inQuotes)
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
            tokens.Add(current.ToString());

        return tokens;
    }

    private static (string value, bool wasQuoted) StripQuotes(string value)
    {
        if (value.Length >= 2 && value.StartsWith('"') && value.EndsWith('"'))
            return (value[1..^1], true);

        // Unterminated quote (still being typed): treat the rest as a literal value.
        if (value.StartsWith('"'))
            return (value[1..], true);

        return (value, false);
    }

    /// <summary>
    /// Splits a value into OR-alternatives on '|'. Quoted values are treated literally
    /// (no splitting), so pipes stored in text (e.g. merged notes) remain searchable.
    /// </summary>
    private static string[] SplitAlternatives(string value, bool isQuoted) =>
        isQuoted ? [value] : value.Split(['|'], StringSplitOptions.RemoveEmptyEntries);

    private static Func<string, bool>[] BuildMatchers(string value, bool isQuoted, MatchMode mode)
    {
        var alternatives = SplitAlternatives(value, isQuoted);
        var matchers = new Func<string, bool>[alternatives.Length];
        for (var i = 0; i < alternatives.Length; i++)
            matchers[i] = BuildMatcher(alternatives[i], mode);

        return matchers;
    }

    private static Func<string, bool> BuildMatcher(string pattern, MatchMode mode)
    {
        if (pattern.Length == 0)
            return _ => false;

        if (pattern.Contains('*'))
        {
            var segments = pattern.Split('*');
            return field => MatchWildcard(field, segments);
        }

        return mode switch
        {
            MatchMode.Contains => field => field.Contains(pattern, StringComparison.OrdinalIgnoreCase),
            MatchMode.StartsWith => field => field.StartsWith(pattern, StringComparison.OrdinalIgnoreCase),
            _ => field => field.Equals(pattern, StringComparison.OrdinalIgnoreCase),
        };
    }

    private static bool MatchesAny(Func<string, bool>[] matchers, string field)
    {
        for (var i = 0; i < matchers.Length; i++)
            if (matchers[i](field))
                return true;

        return false;
    }

    private static bool MatchWildcard(string field, string[] segments)
    {
        var pos = 0;
        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            if (segment.Length == 0)
                continue;

            if (i == 0)
            {
                if (!field.StartsWith(segment, StringComparison.OrdinalIgnoreCase))
                    return false;

                pos = segment.Length;
            }
            else if (i == segments.Length - 1)
            {
                return field.Length - segment.Length >= pos && field.EndsWith(segment, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                var index = field.IndexOf(segment, pos, StringComparison.OrdinalIgnoreCase);
                if (index == -1)
                    return false;

                pos = index + segment.Length;
            }
        }

        return true;
    }

    private static MatchMode ModeFor(SearchType searchType) => searchType switch
    {
        SearchType.StartsWith => MatchMode.StartsWith,
        SearchType.Exact => MatchMode.Exact,
        _ => MatchMode.Contains,
    };

    private static (string op, string operand) SplitComparator(string value)
    {
        if (value.StartsWith(">=", StringComparison.Ordinal) || value.StartsWith("<=", StringComparison.Ordinal))
            return (value[..2], value[2..]);

        if (value.StartsWith('>') || value.StartsWith('<') || value.StartsWith('='))
            return (value[..1], value[1..]);

        return (string.Empty, value);
    }

    private static bool TryParseComparison(string value, out string op, out long target)
    {
        (op, var operand) = SplitComparator(value);
        return long.TryParse(operand, out target);
    }

    private static bool CompareNumber(long actual, string op, long target) => op switch
    {
        ">" => actual > target,
        ">=" => actual >= target,
        "<" => actual < target,
        "<=" => actual <= target,
        _ => actual == target,
    };

    private static bool TryParseAge(string value, out string op, out long durationMs)
    {
        (op, var operand) = SplitComparator(value);
        return TryParseDuration(operand, out durationMs);
    }

    private static bool CompareAge(long age, string op, long duration) => op switch
    {
        ">" => age > duration,
        ">=" => age >= duration,
        "<=" => age <= duration,
        _ => age < duration, // "<", "=", or bare duration: within the given timespan
    };

    private static bool TryParseDuration(string value, out long durationMs)
    {
        durationMs = 0;
        if (value.Length < 2)
            return false;

        var unitMs = char.ToLowerInvariant(value[^1]) switch
        {
            'h' => 3600000L,
            'd' => 86400000L,
            'w' => 604800000L,
            'm' => 2592000000L,  // 30 days
            'y' => 31536000000L, // 365 days
            _ => 0L,
        };

        if (unitMs == 0 || !long.TryParse(value[..^1], out var amount) || amount < 0)
            return false;

        if (amount > long.MaxValue / unitMs) // guard against overflow on absurd inputs
            return false;

        durationMs = amount * unitMs;
        return true;
    }

    private static bool IsValidNumberValue(string value) => TryParseComparison(value, out _, out _);

    private static bool IsValidAgeValue(string value) => TryParseAge(value, out _, out _);

    public static bool IsValidSearch(string searchString)
    {
        if (string.IsNullOrWhiteSpace(searchString))
            return true;

        var tokens = Tokenize(searchString);
        return tokens.Count > 0 && tokens.All(IsValidToken);
    }

    private static bool IsValidToken(string token)
    {
        var (_, key, value, isQuoted) = ParseSearchString(token);

        if (key == DefaultKey)
        {
            // A stray colon that didn't form a key:value pair (e.g. "fc:", ":x") is malformed;
            // FFXIV names never contain a colon, so an unquoted ':' can only be an incomplete filter.
            if (!isQuoted && value.Contains(':'))
                return false;

            return value.Length > 0 && !value.Contains('!');
        }

        if (value.Length == 0)
            return false;

        if (key.Equals(BioKey, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!Handlers.TryGetValue(key, out var handler))
            return false; // unknown key

        if (!isQuoted && value == "!")
            return true; // has-no-value marker

        return handler.IsValidValue?.Invoke(value) ?? true;
    }
}
