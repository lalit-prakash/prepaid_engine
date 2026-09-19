using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PrepaidEngine.Api.Security;

namespace PrepaidEngine.Tests.Auth;

public class ListCapTests : IDisposable
{
    private sealed class Row { public int Id { get; set; } }

    private sealed class RowContext(DbContextOptions<RowContext> options) : DbContext(options)
    {
        public DbSet<Row> Rows => Set<Row>();
    }

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly RowContext _db;

    public ListCapTests()
    {
        _connection.Open();
        _db = new RowContext(new DbContextOptionsBuilder<RowContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private void Seed(int count)
    {
        _db.Rows.AddRange(Enumerable.Range(1, count).Select(i => new Row { Id = i }));
        _db.SaveChanges();
    }

    [Fact]
    public async Task Small_result_is_returned_whole_without_the_truncated_header()
    {
        Seed(5);
        var http = new DefaultHttpContext();

        var rows = await _db.Rows.OrderBy(r => r.Id).ToCappedListAsync(http);

        Assert.Equal(5, rows.Count);
        Assert.False(http.Response.Headers.ContainsKey(ListCap.TruncatedHeader));
    }

    [Fact]
    public async Task Exactly_the_cap_is_not_reported_as_truncated()
    {
        Seed(ListCap.MaxRows);
        var http = new DefaultHttpContext();

        var rows = await _db.Rows.OrderBy(r => r.Id).ToCappedListAsync(http);

        Assert.Equal(ListCap.MaxRows, rows.Count);
        Assert.False(http.Response.Headers.ContainsKey(ListCap.TruncatedHeader));
    }

    [Fact]
    public async Task More_than_the_cap_is_cut_and_flagged()
    {
        Seed(ListCap.MaxRows + 25);
        var http = new DefaultHttpContext();

        var rows = await _db.Rows.OrderBy(r => r.Id).ToCappedListAsync(http);

        Assert.Equal(ListCap.MaxRows, rows.Count);
        Assert.Equal(1, rows[0].Id);
        Assert.Equal("true", http.Response.Headers[ListCap.TruncatedHeader].ToString());
    }
}
