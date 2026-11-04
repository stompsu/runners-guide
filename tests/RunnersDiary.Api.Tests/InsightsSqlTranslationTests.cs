using Microsoft.EntityFrameworkCore;
using RunnersDiary.Api.Endpoints;
using RunnersDiary.Infrastructure;

public sealed class InsightsSqlTranslationTests
{
    [Fact]
    public void Scalar_queries_and_tied_timestamp_keyset_translate_on_sql_server_without_a_connection()
    {
        using var db = new DiaryDbContext(new DbContextOptionsBuilder<DiaryDbContext>()
            .UseSqlServer(sql => sql.UseNetTopologySuite()).Options);
        var upper = new DateTimeOffset(2026, 11, 5, 0, 0, 0, TimeSpan.Zero);
        var queries = new[]
        {
            InsightsQueries.Sessions(db, "owner", upper.AddDays(-367), upper).ToQueryString(),
            InsightsQueries.ActivityTimes(db, "owner", upper).ToQueryString(),
            InsightsQueries.RacePage(db, "owner", upper, null, null, 26).ToQueryString(),
            InsightsQueries.RacePage(db, "owner", upper, upper.AddDays(-1), Guid.NewGuid(), 26).ToQueryString()
        };
        foreach (var sql in queries)
        {
            Assert.Contains("[UserId]", sql, StringComparison.Ordinal);
            Assert.Contains("NOT EXISTS", sql, StringComparison.Ordinal);
            Assert.Contains("[DeleteParentRun]", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("[Notes]", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("[Location_", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("[BlobName]", sql, StringComparison.Ordinal);
        }
        Assert.Contains("ORDER BY", queries[3], StringComparison.Ordinal);
        Assert.Contains("[Id] <", queries[3], StringComparison.Ordinal);
        Assert.Contains("TOP(", queries[3], StringComparison.Ordinal);
    }
}
