using System.Text;

namespace SRXPanel.Services.Developer;

/// <summary>
/// A parsed five-field cron expression (minute hour day-of-month month day-of-week).
/// Supports <c>*</c>, <c>*/step</c>, <c>a-b</c>, <c>a-b/step</c>, comma lists, three-letter
/// month/day names and the usual <c>@hourly</c>-style macros. Sunday is both 0 and 7.
/// </summary>
public sealed class CronExpression
{
    private readonly bool[] _minutes = new bool[60];
    private readonly bool[] _hours = new bool[24];
    private readonly bool[] _daysOfMonth = new bool[32]; // 1..31
    private readonly bool[] _months = new bool[13];      // 1..12
    private readonly bool[] _daysOfWeek = new bool[7];   // 0..6, Sunday = 0

    private bool _domRestricted;
    private bool _dowRestricted;

    public string Expression { get; private set; } = string.Empty;

    private CronExpression() { }

    private static readonly string[] MonthNames =
        { "jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec" };

    private static readonly string[] DayNames = { "sun", "mon", "tue", "wed", "thu", "fri", "sat" };

    private static readonly string[] DayFullNames =
        { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };

    private static readonly string[] MonthFullNames =
    {
        "", "January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December"
    };

    /// <summary>Named presets offered in the UI.</summary>
    public static readonly (string Label, string Expression)[] Presets =
    {
        ("Every minute", "* * * * *"),
        ("Every 5 minutes", "*/5 * * * *"),
        ("Every 15 minutes", "*/15 * * * *"),
        ("Every 30 minutes", "*/30 * * * *"),
        ("Hourly", "0 * * * *"),
        ("Twice daily", "0 0,12 * * *"),
        ("Daily (midnight)", "0 0 * * *"),
        ("Daily (3:00 AM)", "0 3 * * *"),
        ("Weekly (Sunday)", "0 0 * * 0"),
        ("Monthly (1st)", "0 0 1 * *"),
        ("Yearly (Jan 1st)", "0 0 1 1 *")
    };

    public static bool TryParse(string? expression, out CronExpression? schedule, out string error)
    {
        schedule = null;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(expression))
        {
            error = "Enter a cron expression.";
            return false;
        }

        var expr = expression.Trim();
        expr = expr.ToLowerInvariant() switch
        {
            "@yearly" or "@annually" => "0 0 1 1 *",
            "@monthly" => "0 0 1 * *",
            "@weekly" => "0 0 * * 0",
            "@daily" or "@midnight" => "0 0 * * *",
            "@hourly" => "0 * * * *",
            _ => expr
        };

        var fields = expr.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 5)
        {
            error = $"A cron expression needs exactly 5 fields (minute hour day month weekday) — got {fields.Length}.";
            return false;
        }

        var result = new CronExpression { Expression = expression.Trim() };

        if (!Fill(fields[0], 0, 59, result._minutes, null, out var restricted, out error)) { error = $"Minute: {error}"; return false; }
        if (!Fill(fields[1], 0, 23, result._hours, null, out restricted, out error)) { error = $"Hour: {error}"; return false; }

        if (!Fill(fields[2], 1, 31, result._daysOfMonth, null, out restricted, out error)) { error = $"Day of month: {error}"; return false; }
        result._domRestricted = restricted;

        if (!Fill(fields[3], 1, 12, result._months, MonthNames, out restricted, out error)) { error = $"Month: {error}"; return false; }

        if (!Fill(fields[4], 0, 7, result._daysOfWeek, DayNames, out restricted, out error, wrapSunday: true)) { error = $"Day of week: {error}"; return false; }
        result._dowRestricted = restricted;

        schedule = result;
        return true;
    }

    /// <summary>Fills a bit table from one cron field. <paramref name="restricted"/> is false for a bare "*".</summary>
    private static bool Fill(string field, int min, int max, bool[] table, string[]? names,
        out bool restricted, out string error, bool wrapSunday = false)
    {
        restricted = field != "*";
        error = string.Empty;

        foreach (var part in field.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var step = 1;
            var range = part;

            var slash = part.IndexOf('/');
            if (slash >= 0)
            {
                range = part[..slash];
                if (!int.TryParse(part[(slash + 1)..], out step) || step < 1)
                {
                    error = $"'{part}' has an invalid step.";
                    return false;
                }
            }

            int from, to;
            if (range is "*" or "")
            {
                from = min;
                to = max;
            }
            else
            {
                var dash = range.IndexOf('-');
                if (dash > 0)
                {
                    if (!TryValue(range[..dash], min, max, names, out from) ||
                        !TryValue(range[(dash + 1)..], min, max, names, out to))
                    {
                        error = $"'{range}' is not a valid range of {min}-{max}.";
                        return false;
                    }
                }
                else
                {
                    if (!TryValue(range, min, max, names, out from))
                    {
                        error = $"'{range}' is not a value between {min} and {max}.";
                        return false;
                    }
                    to = slash >= 0 ? max : from;
                }
            }

            if (from > to)
            {
                error = $"'{range}' starts after it ends.";
                return false;
            }

            for (var v = from; v <= to; v += step)
            {
                var index = wrapSunday && v == 7 ? 0 : v;
                if (index >= table.Length) continue;
                table[index] = true;
            }
        }

        return true;
    }

    private static bool TryValue(string token, int min, int max, string[]? names, out int value)
    {
        token = token.Trim().ToLowerInvariant();

        if (names != null)
        {
            var named = Array.IndexOf(names, token);
            if (named >= 0)
            {
                // Month names are 1-based, day names 0-based.
                value = names == MonthNames ? named + 1 : named;
                return true;
            }
        }

        return int.TryParse(token, out value) && value >= min && value <= max;
    }

    private bool Matches(DateTime t)
    {
        if (!_minutes[t.Minute] || !_hours[t.Hour] || !_months[t.Month]) return false;

        var dom = _daysOfMonth[t.Day];
        var dow = _daysOfWeek[(int)t.DayOfWeek];

        // Standard cron: when both day fields are restricted, either one matching is enough.
        if (_domRestricted && _dowRestricted) return dom || dow;
        return dom && dow;
    }

    /// <summary>True when the day/month/weekday fields can never match this calendar day.</summary>
    private bool DayPossible(DateTime t)
    {
        if (!_months[t.Month]) return false;
        var dom = _daysOfMonth[t.Day];
        var dow = _daysOfWeek[(int)t.DayOfWeek];
        if (_domRestricted && _dowRestricted) return dom || dow;
        return dom && dow;
    }

    /// <summary>The first firing time strictly after <paramref name="after"/>, or null if none within four years.</summary>
    public DateTime? Next(DateTime after)
    {
        var t = new DateTime(after.Year, after.Month, after.Day, after.Hour, after.Minute, 0, after.Kind).AddMinutes(1);
        var limit = after.AddYears(4);

        while (t < limit)
        {
            if (!DayPossible(t))
            {
                // Skip the whole day rather than 1,440 individual minutes.
                t = t.Date.AddDays(1);
                continue;
            }
            if (Matches(t)) return t;
            t = t.AddMinutes(1);
        }
        return null;
    }

    /// <summary>A plain-English description, e.g. "Runs every day at 3:00 AM".</summary>
    public string Describe()
    {
        var minutes = Selected(_minutes, 0, 59);
        var hours = Selected(_hours, 0, 23);
        var domList = Selected(_daysOfMonth, 1, 31);
        var dowList = Selected(_daysOfWeek, 0, 6);
        var monthList = Selected(_months, 1, 12);

        var everyMinute = minutes.Count == 60;
        var everyHour = hours.Count == 24;
        var exactTime = minutes.Count == 1 && hours.Count == 1;

        // Which day does it run on? Empty string means "every day".
        string days;
        if (_dowRestricted && _domRestricted)
            days = $"on the {Join(domList.Select(Ordinal))} and on {Join(dowList.Select(d => DayFullNames[d]))}";
        else if (_dowRestricted)
            days = dowList.Count == 7 ? "" : $"every {Join(dowList.Select(d => DayFullNames[d]))}";
        else if (_domRestricted)
            days = $"on the {Join(domList.Select(Ordinal))} of the month";
        else
            days = "every day";

        var sb = new StringBuilder("Runs ");

        if (everyMinute && everyHour)
            sb.Append("every minute");
        else if (StepOf(minutes, 60) is int minStep && everyHour)
            sb.Append(minStep == 1 ? "every minute" : $"every {minStep} minutes");
        else if (everyMinute)
            sb.Append($"every minute during {HourPhrase(hours)}");
        else if (everyHour && minutes.Count == 1)
            sb.Append(minutes[0] == 0 ? "every hour on the hour" : $"every hour at {minutes[0]:00} minutes past");
        else if (minutes.Count == 1 && StepOf(hours, 24) is int hourStep && hourStep > 1)
            sb.Append($"every {hourStep} hours at {minutes[0]:00} minutes past");
        else if (exactTime)
            sb.Append($"{(days == "every day" ? "every day" : days)} at {Clock(hours[0], minutes[0])}");
        else if (minutes.Count == 1)
            sb.Append($"at {Join(hours.Select(h => Clock(h, minutes[0])))}");
        else
            sb.Append($"at minute {string.Join(",", minutes)} past {HourPhrase(hours)}");

        // The exact-time branch already stated the day; every other branch still needs it.
        if (!exactTime && days != "" && days != "every day")
            sb.Append($" {days}");

        if (monthList.Count < 12)
            sb.Append($" in {Join(monthList.Select(m => MonthFullNames[m]))}");

        return sb.ToString();
    }

    private static string Join(IEnumerable<string> parts)
    {
        var list = parts.ToList();
        if (list.Count == 0) return "";
        if (list.Count == 1) return list[0];
        return string.Join(", ", list.Take(list.Count - 1)) + " and " + list[^1];
    }

    private static List<int> Selected(bool[] table, int min, int max)
    {
        var list = new List<int>();
        for (var i = min; i <= max && i < table.Length; i++)
            if (table[i]) list.Add(i);
        return list;
    }

    /// <summary>Detects an evenly spaced selection starting at 0 (i.e. a "*/n" field) and returns n.</summary>
    private static int? StepOf(List<int> values, int period)
    {
        if (values.Count < 2 || values[0] != 0) return null;
        var step = values[1] - values[0];
        if (step <= 0 || period % step != 0 || values.Count != period / step) return null;
        for (var i = 1; i < values.Count; i++)
            if (values[i] - values[i - 1] != step) return null;
        return step;
    }

    private static string HourPhrase(List<int> hours) =>
        hours.Count == 24 ? "every hour" : $"hour {string.Join(",", hours)}";

    /// <summary>Renders "3:00 AM" for the UI's readable-time preview.</summary>
    public static string ClockOf(int hour, int minute) => Clock(hour, minute);

    private static string Clock(int hour, int minute)
    {
        var suffix = hour < 12 ? "AM" : "PM";
        var h = hour % 12;
        if (h == 0) h = 12;
        return $"{h}:{minute:00} {suffix}";
    }

    private static string Ordinal(int n)
    {
        if (n is >= 11 and <= 13) return $"{n}th";
        return (n % 10) switch { 1 => $"{n}st", 2 => $"{n}nd", 3 => $"{n}rd", _ => $"{n}th" };
    }

    /// <summary>Builds an expression from the visual builder inputs.</summary>
    public static string Build(string every, int interval, int hour, int minute, int dayOfWeek, int dayOfMonth) => every switch
    {
        "minute" => interval <= 1 ? "* * * * *" : $"*/{Math.Clamp(interval, 1, 59)} * * * *",
        "hour" => interval <= 1 ? $"{minute} * * * *" : $"{minute} */{Math.Clamp(interval, 1, 23)} * * *",
        "day" => interval <= 1 ? $"{minute} {hour} * * *" : $"{minute} {hour} */{Math.Clamp(interval, 1, 31)} * *",
        "week" => $"{minute} {hour} * * {Math.Clamp(dayOfWeek, 0, 6)}",
        "month" => $"{minute} {hour} {Math.Clamp(dayOfMonth, 1, 31)} */{Math.Clamp(interval, 1, 12)} *",
        _ => "0 3 * * *"
    };
}
