using ChessCrm.Bot.Services;

namespace Tests;

/// <summary>Group B: FindGroupAsync cascade tests (name → id → day+time → day+hour).</summary>
[Collection("Database")]
public class FindGroupTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture;
    private DatabaseService _db = null!;
    private int _groupPn16Id;
    private int _groupVt18Id;

    public FindGroupTests(TestDatabaseFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await TestHelper.CleanAllAsync(_fixture.ConnectionString);
        var config = TestHelper.CreateConfig(_fixture.ConnectionString);
        _db = TestHelper.CreateDbService(config);

        // Seed: "пн16" on monday at 16:30, "вт18" on tuesday at 18:45
        _groupPn16Id = await TestHelper.InsertGroupAsync(
            _fixture.ConnectionString, "пн16", "monday", new TimeSpan(16, 30, 0));
        _groupVt18Id = await TestHelper.InsertGroupAsync(
            _fixture.ConnectionString, "вт18", "tuesday", new TimeSpan(18, 45, 0));
    }

    public Task DisposeAsync() => TestHelper.CleanAllAsync(_fixture.ConnectionString);

    // B1: Step 1 — поиск по groupName (точное)
    [Fact]
    public async Task B1_FindByName_Exact()
    {
        var group = await _db.FindGroupAsync(null, null, null, groupName: "пн16");
        Assert.NotNull(group);
        Assert.Equal(_groupPn16Id, group.Id);
        Assert.Equal("пн16", group.Name);
    }

    // B2: Step 1 — поиск по groupName (case-insensitive)
    [Fact]
    public async Task B2_FindByName_CaseInsensitive()
    {
        var group = await _db.FindGroupAsync(null, null, null, groupName: "ПН16");
        Assert.NotNull(group);
        Assert.Equal(_groupPn16Id, group.Id);
    }

    // B3: Step 2 — поиск по groupId
    [Fact]
    public async Task B3_FindByGroupId()
    {
        var group = await _db.FindGroupAsync(null, null, _groupVt18Id);
        Assert.NotNull(group);
        Assert.Equal("вт18", group.Name);
    }

    // B4: Step 3 — поиск по day + exact time
    [Fact]
    public async Task B4_FindByDayTime_Exact()
    {
        var group = await _db.FindGroupAsync(
            DayOfWeek.Monday, new TimeSpan(16, 30, 0), null);
        Assert.NotNull(group);
        Assert.Equal("пн16", group.Name);
    }

    // B5: Step 3 fail → Step 4 fallback — day + hour (minutes don't match, but hour does)
    [Fact]
    public async Task B5_DayTime_WrongMinutes_FallbackToHour()
    {
        // Exact time 16:00 doesn't match 16:30, but hour 16 matches
        var group = await _db.FindGroupAsync(
            DayOfWeek.Monday, new TimeSpan(16, 0, 0), null);
        Assert.NotNull(group);
        Assert.Equal("пн16", group.Name);
    }

    // B6: Step 4 — day+hour для другой группы
    [Fact]
    public async Task B6_FallbackHour_OtherGroup()
    {
        // Exact time 18:00 doesn't match 18:45, but hour 18 matches вт18
        var group = await _db.FindGroupAsync(
            DayOfWeek.Tuesday, new TimeSpan(18, 0, 0), null);
        Assert.NotNull(group);
        Assert.Equal("вт18", group.Name);
    }

    // B7: Ничего не найдено — wrong day+hour
    [Fact]
    public async Task B7_NotFound_WrongDayAndHour()
    {
        var group = await _db.FindGroupAsync(
            DayOfWeek.Monday, new TimeSpan(20, 0, 0), null);
        Assert.Null(group);
    }

    // B8: groupName не найдено, но groupId найдено — каскад продолжается
    [Fact]
    public async Task B8_NameNotFound_FallsToId()
    {
        var group = await _db.FindGroupAsync(
            null, null, _groupPn16Id, groupName: "несуществующая");
        Assert.NotNull(group);
        Assert.Equal("пн16", group.Name);
    }
}
