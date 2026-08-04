namespace SandlotSlugfest.Season;

/// <summary>One line in the league news feed — an injury, a transaction, a milestone.</summary>
public struct NewsItem
{
    public int Day;
    public string Headline;
    public string Detail;
    public int TeamId;      // -1 for league-wide
}
