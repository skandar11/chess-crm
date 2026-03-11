using ChessCrm.Bot.Handlers;
using ChessCrm.Bot.Services;

namespace Tests;

/// <summary>
/// Group C: ResolveGroupLineAsync tests.
/// We test through AddToGroupHandler which uses the same
/// FindGroupAsync + DayOfWeekMapper resolution.
/// </summary>
[Collection("Database")]
public class ResolveGroupTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture;
    private DatabaseService _db = null!;
    private AddToGroupHandler _handler = null!;
    private int _clientId;
    private int _groupVt16Id;
    private int _groupSr18Id;

    public ResolveGroupTests(TestDatabaseFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await TestHelper.CleanAllAsync(_fixture.ConnectionString);
        var config = TestHelper.CreateConfig(_fixture.ConnectionString);
        _db = TestHelper.CreateDbService(config);
        _handler = TestHelper.CreateAddToGroupHandler(_db);

        _clientId = await TestHelper.InsertClientAsync(_fixture.ConnectionString, "Тестовый Ученик");
        _groupVt16Id = await TestHelper.InsertGroupAsync(
            _fixture.ConnectionString, "вт16", "tuesday", new TimeSpan(16, 30, 0));
        _groupSr18Id = await TestHelper.InsertGroupAsync(
            _fixture.ConnectionString, "ср18", "wednesday", new TimeSpan(18, 45, 0));
    }

    public Task DisposeAsync() => TestHelper.CleanAllAsync(_fixture.ConnectionString);

    // C1: group_name приоритет — group_name игнорирует day+time
    [Fact]
    public async Task C1_GroupName_HasPriority()
    {
        var p = new Dictionary<string, string?>
        {
            ["student_name"] = "Тестовый Ученик",
            ["group_name"] = "вт16",
            ["group_1_day"] = "понедельник",
            ["group_1_time"] = "17:30",
        };
        var result = await _handler.HandleAsync(p, default);

        Assert.True(result.Success);
        Assert.Contains("вт16", result.Message);
    }

    // C2: Fuzzy день через DayOfWeekMapper — "вторн" → tuesday
    [Fact]
    public async Task C2_FuzzyDay_Mapping()
    {
        var p = new Dictionary<string, string?>
        {
            ["student_name"] = "Тестовый Ученик",
            ["group_1_day"] = "вторн",
            ["group_1_time"] = "16:30",
        };
        var result = await _handler.HandleAsync(p, default);

        Assert.True(result.Success);
        Assert.Contains("вт16", result.Message);
    }

    // C3: Day+hour fallback — время 18:00 не совпадает с 18:45, но час 18 совпадает
    [Fact]
    public async Task C3_DayHour_Fallback()
    {
        var p = new Dictionary<string, string?>
        {
            ["student_name"] = "Тестовый Ученик",
            ["group_1_day"] = "среда",
            ["group_1_time"] = "18:00",
        };
        var result = await _handler.HandleAsync(p, default);

        Assert.True(result.Success);
        Assert.Contains("ср18", result.Message);
    }

    // C4: Нет ни name ни day+time — ошибка
    [Fact]
    public async Task C4_NothingProvided_Error()
    {
        var p = new Dictionary<string, string?>
        {
            ["student_name"] = "Тестовый Ученик",
        };
        var result = await _handler.HandleAsync(p, default);

        Assert.False(result.Success);
        Assert.Contains("Не понял группу", result.Message);
    }
}
