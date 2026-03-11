using ChessCrm.Bot.Handlers;
using ChessCrm.Bot.Services;

namespace Tests;

/// <summary>Group C: AddToGroupHandler integration-like tests.</summary>
[Collection("Database")]
public class AddToGroupTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture;
    private DatabaseService _db = null!;
    private AddToGroupHandler _handler = null!;
    private int _clientId;
    private int _groupId;

    public AddToGroupTests(TestDatabaseFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await TestHelper.CleanAllAsync(_fixture.ConnectionString);
        var config = TestHelper.CreateConfig(_fixture.ConnectionString);
        _db = TestHelper.CreateDbService(config);
        _handler = TestHelper.CreateAddToGroupHandler(_db);

        _clientId = await TestHelper.InsertClientAsync(_fixture.ConnectionString, "Иванов Артём");
        _groupId = await TestHelper.InsertGroupAsync(
            _fixture.ConnectionString, "пн16", "monday", new TimeSpan(16, 30, 0), maxStudents: 2);
    }

    public Task DisposeAsync() => TestHelper.CleanAllAsync(_fixture.ConnectionString);

    // C1: Успешная запись
    [Fact]
    public async Task C1_SuccessfulAdd()
    {
        var p = new Dictionary<string, string?>
        {
            ["student_name"] = "Иванов Артём",
            ["group_name"] = "пн16",
        };
        var result = await _handler.HandleAsync(p, default);

        Assert.True(result.Success);
        Assert.Contains("записан", result.Message);
        Assert.Contains("пн16", result.Message);

        // Verify in DB
        Assert.True(await _db.IsClientInGroupAsync(_clientId, _groupId));
    }

    // C2: Ученик уже в группе
    [Fact]
    public async Task C2_AlreadyInGroup()
    {
        await TestHelper.InsertGroupStudentAsync(_fixture.ConnectionString, _clientId, _groupId);

        var p = new Dictionary<string, string?>
        {
            ["student_name"] = "Иванов Артём",
            ["group_name"] = "пн16",
        };
        var result = await _handler.HandleAsync(p, default);

        Assert.False(result.Success);
        Assert.Contains("уже записан", result.Message);
    }

    // C3: Нет мест
    [Fact]
    public async Task C3_GroupFull()
    {
        // Fill group to max (2 students)
        var c1 = await TestHelper.InsertClientAsync(_fixture.ConnectionString, "Первый Ученик");
        var c2 = await TestHelper.InsertClientAsync(_fixture.ConnectionString, "Второй Ученик");
        await TestHelper.InsertGroupStudentAsync(_fixture.ConnectionString, c1, _groupId);
        await TestHelper.InsertGroupStudentAsync(_fixture.ConnectionString, c2, _groupId);

        var p = new Dictionary<string, string?>
        {
            ["student_name"] = "Иванов Артём",
            ["group_name"] = "пн16",
        };
        var result = await _handler.HandleAsync(p, default);

        Assert.False(result.Success);
        Assert.Contains("заполнена", result.Message);
    }

    // C4: Группа не найдена
    [Fact]
    public async Task C4_GroupNotFound()
    {
        var p = new Dictionary<string, string?>
        {
            ["student_name"] = "Иванов Артём",
            ["group_name"] = "zz99",
        };
        var result = await _handler.HandleAsync(p, default);

        Assert.False(result.Success);
        Assert.Contains("не найдена", result.Message);
    }
}
