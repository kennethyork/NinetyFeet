using System.Collections.Generic;
using System.Linq;
using Godot;
using SandlotSlugfest.Core;
using SandlotSlugfest.Data;

namespace SandlotSlugfest.Season;

/// <summary>Who is writing to you.</summary>
public enum Sender { Owner, Scouting, Pitching, Hitting, Bench, Press, League }

/// <summary>One message in the club's inbox.</summary>
public sealed class Message
{
    public Sender From;
    public string Subject = "";
    public string Body = "";
    public int Day;
    public int Year;
    public bool Read;

    /// <summary>Set when the message is about somebody in particular.</summary>
    public string About = "";

    public string FromName => From switch
    {
        Sender.Owner => "The Owner",
        Sender.Scouting => "Scouting Director",
        Sender.Pitching => "Pitching Coach",
        Sender.Hitting => "Hitting Coach",
        Sender.Bench => "Bench Coach",
        Sender.Press => "Beat Writer",
        _ => "League Office",
    };
}

/// <summary>
/// The club's correspondence.
///
/// A news feed tells you what happened. Correspondence tells you what somebody thinks about it,
/// and that is a different and more useful thing: the pitching coach saying an arm has thrown 340
/// pitches in nine days is information you cannot get from a box score, and the owner telling you
/// in April what he expects by September is the only way a season has stakes before it ends.
///
/// Everyone who writes here already exists in the game and is reporting on real state — the coach
/// you hired reads your actual workloads, the scouting director's confidence in a prospect is the
/// same fog-of-war the farm screen uses, and the owner's patience is your actual budget and your
/// actual record. Nothing is invented to have something to say.
/// </summary>
public static class Inbox
{
    /// <summary>Most recent first. Kept across seasons — this is a record, not a ticker.</summary>
    public static readonly List<Message> Messages = new();

    /// <summary>How many are unread, for the badge on the season screen.</summary>
    public static int Unread => Messages.Count(m => !m.Read);

    public static void Clear() => Messages.Clear();

    public static void Post(Sender from, string subject, string body, int day, int year,
        string about = "")
    {
        // Never say the same thing twice in a season. A coach who repeats himself every day is
        // noise, and noise is how an inbox stops being read.
        if (Messages.Any(m => m.Year == year && m.From == from && m.Subject == subject)) return;

        Messages.Insert(0, new Message
        {
            From = from, Subject = subject, Body = body, Day = day, Year = year, About = about,
        });

        // A long dynasty would otherwise carry thousands.
        if (Messages.Count > 200) Messages.RemoveRange(200, Messages.Count - 200);
    }

    public static void MarkAllRead()
    {
        foreach (var m in Messages) m.Read = true;
    }

    // -----------------------------------------------------------------------
    // What people write about
    // -----------------------------------------------------------------------

    /// <summary>The owner's expectations, set before a ball is thrown.</summary>
    public static void OpeningDay(SeasonState season)
    {
        int club = season.UserTeamId;
        var books = season.Books(club);
        int payroll = Contracts.Payroll(season.RosterFor(club));

        // What he wants depends on what he is paying for. A club spending near the tax line is
        // expected to win; one running a bottom-five payroll is expected to develop.
        bool spending = payroll > Finances.BaselineBudget * 1.1f;
        int target = spending ? 92 : payroll > Finances.BaselineBudget * 0.85f ? 84 : 76;

        Post(Sender.Owner, $"Year {season.Year}: what I expect",
            $"We are carrying {Contracts.Text(payroll)} of payroll against a budget of " +
            $"{Contracts.Text(books.Budget)}.\n\n" +
            (spending
                ? $"That is a contender's payroll and I want a contender. {target} wins, and I " +
                  "would like to see October."
                : payroll > Finances.BaselineBudget * 0.85f
                    ? $"A fair budget for a fair club. {target} wins keeps everybody happy."
                    : $"We are not spending much, so I am not going to pretend to expect much — " +
                      $"{target} wins and some young players worth watching.") +
            "\n\nDo not embarrass us.",
            season.CurrentDay, season.Year);
    }

    /// <summary>The daily sweep: anybody with something worth saying says it.</summary>
    public static void Daily(SeasonState season)
    {
        int club = season.UserTeamId;
        var roster = season.RosterFor(club);
        var rec = season.Book.Record(club);
        int played = rec.Wins + rec.Losses;

        // --- The pitching coach reads workloads nobody else can see. ---
        var tired = roster.Pitchers
            .Where(p => !p.IsInjured && p.RecentPitches >= 95 && p.RestDays <= 1)
            .OrderByDescending(p => p.RecentPitches)
            .FirstOrDefault();

        if (tired != null)
        {
            int skill = Coaches.SkillAt(club, CoachRole.Pitching);
            Post(Sender.Pitching, $"{tired.ShortName} is being run into the ground",
                $"{tired.Name} has {tired.RecentPitches} pitches on him in the last few days and " +
                $"{tired.RestDays} day{(tired.RestDays == 1 ? "" : "s")} of rest.\n\n" +
                (skill >= 7
                    ? "I have seen this end badly more than once. Give him three days or find " +
                      "somebody else for the seventh."
                    : "He says he is fine. They always say they are fine.") +
                "\n\nYour call, but it is on the record now.",
                season.CurrentDay, season.Year, tired.Name);
        }

        // --- The hitting coach, on a man who cannot handle one side. ---
        // He reads the splits, which is the only place a platoon problem is visible. Nobody could
        // write this before, because nothing recorded which hand was on the mound.
        foreach (var p in roster.BattingOrder)
        {
            var splits = season.Book.Splits;
            if (!splits.HasBatting(p)) continue;

            var good = splits.Batting(p).Peek(Stats.Split.VsRight);
            var bad = splits.Batting(p).Peek(Stats.Split.VsLeft);
            if (good == null || bad == null) continue;

            // Enough at-bats on both sides to mean something, and a gap worth acting on.
            if (good.AtBats < 40 || bad.AtBats < 25) continue;
            if (good.Average - bad.Average < 0.090f) continue;

            Post(Sender.Hitting, $"{p.ShortName} cannot see left-handers",
                $"{p.Name} is hitting {Stats.BattingLine.Rate(good.Average)} against right-handed " +
                $"pitching and {Stats.BattingLine.Rate(bad.Average)} against left, over " +
                $"{bad.AtBats} at-bats.\n\n" +
                "That is not a slump, it is a hole, and the other clubs have the same numbers we " +
                "do. Expect to see a left-hander every time he comes up late in a close one.\n\n" +
                "I can work on it. I would also take a right-handed bat on the bench.",
                season.CurrentDay, season.Year, p.Name);
            break;      // one of these a season is a note; five is noise
        }

        // --- The bench coach on the infirmary. ---
        var hurt = roster.Players.Where(p => p.IsInjured).ToList();
        if (hurt.Count >= 3)
        {
            Post(Sender.Bench, $"{hurt.Count} men down",
                "We are thin. " + string.Join(", ", hurt.Take(4).Select(p =>
                    $"{p.ShortName} ({p.Injury}, about {p.DaysOut})")) +
                (hurt.Count > 4 ? $", and {hurt.Count - 4} more." : ".") +
                "\n\nWe can cover it for now. If we lose one more up the middle we cannot.",
                season.CurrentDay, season.Year);
        }

        // --- The press notices runs of results. ---
        if (played >= 20)
        {
            float pct = rec.Wins / (float)played;
            if (pct >= 0.640f)
                Post(Sender.Press, $"{Teams.Get(club).Nickname} are the story of the season so far",
                    $"You are {rec.Wins}-{rec.Losses}. Nobody picked you for this.\n\n" +
                    "Care to say something about it, or shall I make something up?",
                    season.CurrentDay, season.Year);
            else if (pct <= 0.360f)
                Post(Sender.Press, "Questions are being asked",
                    $"{rec.Wins}-{rec.Losses}. The phone-ins have started and your name is coming " +
                    "up.\n\nI would rather print what you actually said.",
                    season.CurrentDay, season.Year);
        }

        // --- The owner, once, when the season has gone badly wrong. ---
        if (played >= 60)
        {
            float pct = rec.Wins / (float)played;
            if (pct <= 0.400f)
                Post(Sender.Owner, "This is not what we discussed",
                    $"{rec.Wins}-{rec.Losses} at {played} games.\n\n" +
                    "I am not going to tell you how to run the club. I am going to tell you that " +
                    "I am watching it, which I was not in April.",
                    season.CurrentDay, season.Year);
        }
    }

    /// <summary>The scouting director, on the men nobody has seen yet.</summary>
    public static void ScoutingReport(SeasonState season)
    {
        int club = season.UserTeamId;
        int sharpness = Coaches.SkillAt(club, CoachRole.Scouting);

        var best = Farm.AllOf(club)
            .OrderByDescending(p => Scouting.Estimate(p, Scouting.For(club, p)))
            .ThenByDescending(p => p.Overall)
            .FirstOrDefault();

        if (best == null) return;

        var level = Farm.LevelOf(club, best) ?? Farm.Level.HighA;
        string verdict = Scouting.Report(club, best);

        Post(Sender.Scouting, $"On {best.Name}",
            $"{best.Age}, {PlayerData.PositionLabel(best.Position)}, {Farm.Name(level)}.\n\n" +
            $"We have him as {verdict}.\n\n" +
            (sharpness >= 7
                ? "I am confident in that. We have had a lot of eyes on him."
                : sharpness > 0
                    ? "That is our read, for what it is worth. We are not a big department."
                    : "You have nobody running scouting, so that is one man's opinion and he is " +
                      "busy. Hire somebody."),
            season.CurrentDay, season.Year, best.Name);
    }

    /// <summary>The owner's verdict, once the year is done.</summary>
    public static void SeasonReview(SeasonState season, int wins, int losses, bool madePlayoffs,
        bool wonIt)
    {
        Post(Sender.Owner, $"Year {season.Year - 1}: my thoughts",
            $"{wins}-{losses}.\n\n" +
            (wonIt
                ? "We won it. I have nothing to add except that I would like to do it again."
                : madePlayoffs
                    ? "October, and out. That is a good season and I know it. Do not tell me it " +
                      "is a good season."
                    : wins >= losses
                        ? "Over .500 and home in September. I can live with one of those."
                        : "That was not good enough and we both know it.") +
            "\n\nGo and spend the winter well.",
            season.CurrentDay, season.Year);
    }

    // -----------------------------------------------------------------------
    // Saving
    // -----------------------------------------------------------------------

    public static (int[] From, string[] Subject, string[] Body, int[] Day, int[] Year,
        bool[] Read, string[] About) Export()
    {
        return (Messages.Select(m => (int)m.From).ToArray(),
                Messages.Select(m => m.Subject).ToArray(),
                Messages.Select(m => m.Body).ToArray(),
                Messages.Select(m => m.Day).ToArray(),
                Messages.Select(m => m.Year).ToArray(),
                Messages.Select(m => m.Read).ToArray(),
                Messages.Select(m => m.About).ToArray());
    }

    public static void Import(int[] from, string[] subject, string[] body, int[] day, int[] year,
        bool[] read, string[] about)
    {
        Messages.Clear();
        if (from == null || subject == null || body == null) return;

        for (int i = 0; i < from.Length && i < subject.Length && i < body.Length; i++)
            Messages.Add(new Message
            {
                From = (Sender)Mathf.Clamp(from[i], 0, 6),
                Subject = subject[i],
                Body = body[i],
                Day = day != null && i < day.Length ? day[i] : 0,
                Year = year != null && i < year.Length ? year[i] : 1,
                Read = read != null && i < read.Length && read[i],
                About = about != null && i < about.Length ? about[i] : "",
            });
    }
}
