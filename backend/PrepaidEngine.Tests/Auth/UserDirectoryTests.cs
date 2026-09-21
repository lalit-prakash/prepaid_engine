using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using PrepaidEngine.Api.Auth;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;
using PrepaidEngine.Infrastructure.Persistence;

namespace PrepaidEngine.Tests.Auth;

public class UserDirectoryTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly PrepaidEngineDbContext _db;
    private readonly UserDirectory _directory;

    public UserDirectoryTests()
    {
        _connection.Open();
        _db = new PrepaidEngineDbContext(new DbContextOptionsBuilder<PrepaidEngineDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DemoAuth:Users:0:Username"] = "Boot_Admin",
            ["DemoAuth:Users:0:Role"] = "Admin",
            ["DemoAuth:Users:0:Password"] = "Original@1",
        }).Build();
        _directory = new UserDirectory(_db, new UserStore(config));
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private async Task<AppUser> AddUserAsync(string loginId = "Meera_K", UserRole role = UserRole.Operator, string password = "Test@123", bool active = true)
    {
        var u = new AppUser(Guid.NewGuid(), loginId, "Meera K", "meera@example.com", role, PasswordHasher.Hash(password), DateTime.UtcNow, "test");
        if (!active) u.SetActive(false, DateTime.UtcNow);
        _db.Users.Add(u);
        await _db.SaveChangesAsync();
        return u;
    }

    [Fact]
    public async Task A_database_user_signs_in_with_the_right_password_and_gets_their_role()
    {
        await AddUserAsync();
        var user = await _directory.ValidateAsync("meera_k", "Test@123"); // login id ignores case
        Assert.NotNull(user);
        Assert.Equal(UserRole.Operator, user!.Role);
        Assert.NotNull((await _db.Users.SingleAsync()).LastLoginAt);
    }

    [Fact]
    public async Task A_wrong_password_or_unknown_user_does_not_sign_in()
    {
        await AddUserAsync();
        Assert.Null(await _directory.ValidateAsync("Meera_K", "nope"));
        Assert.Null(await _directory.ValidateAsync("nobody", "Test@123"));
    }

    [Fact]
    public async Task A_deactivated_user_cannot_sign_in_even_with_the_right_password()
    {
        await AddUserAsync(active: false);
        Assert.Null(await _directory.ValidateAsync("Meera_K", "Test@123"));
    }

    [Fact]
    public async Task A_configured_user_still_signs_in()
    {
        var user = await _directory.ValidateAsync("boot_admin", "Original@1");
        Assert.NotNull(user);
        Assert.Equal(UserRole.Admin, user!.Role);
    }

    [Fact]
    public async Task Setting_a_password_on_a_database_user_replaces_their_password()
    {
        var u = await AddUserAsync();
        var found = (await _directory.FindAsync("Meera_K"))!;
        await _directory.SetPasswordAsync(found, "New@Pass9", DateTime.UtcNow);
        Assert.Null(await _directory.ValidateAsync("Meera_K", "Test@123"));
        Assert.NotNull(await _directory.ValidateAsync("Meera_K", "New@Pass9"));
        Assert.Equal(u.Id, found.Id);
    }

    [Fact]
    public async Task Setting_a_password_on_a_configured_user_stores_an_override_that_wins()
    {
        var found = (await _directory.FindAsync("Boot_Admin"))!;
        Assert.Equal(UserSource.Configuration, found.Source);
        await _directory.SetPasswordAsync(found, "New@Pass9", DateTime.UtcNow);
        Assert.Null(await _directory.ValidateAsync("Boot_Admin", "Original@1"));
        Assert.NotNull(await _directory.ValidateAsync("Boot_Admin", "New@Pass9"));
    }

    [Fact]
    public async Task A_login_id_is_taken_by_either_kind_of_user()
    {
        await AddUserAsync();
        Assert.True(await _directory.ExistsAsync("MEERA_k"));
        Assert.True(await _directory.ExistsAsync("boot_admin"));
        Assert.False(await _directory.ExistsAsync("someone_else"));
    }

    [Fact]
    public async Task The_login_id_is_unique_ignoring_case()
    {
        await AddUserAsync("Meera_K");
        _db.Users.Add(new AppUser(Guid.NewGuid(), "meera_k", "Dup", "d@example.com", UserRole.ReadOnly, PasswordHasher.Hash("Test@123"), DateTime.UtcNow, "test"));
        await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync());
    }

    [Fact]
    public void Every_role_appears_and_operations_are_not_granted_to_read_only_or_utility()
    {
        Assert.Equal(Enum.GetValues<UserRole>().Length, AccessPolicies.Roles.Length);
        var operations = AccessPolicies.Permissions.Single(p => p.Policy == AccessPolicies.Operations).Roles;
        Assert.DoesNotContain(UserRole.ReadOnly, operations);
        Assert.DoesNotContain(UserRole.Utility, operations);
        Assert.Contains(UserRole.Operator, operations);
    }
}
