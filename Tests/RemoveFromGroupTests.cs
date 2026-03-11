using ChessCrm.Bot.Handlers;
using ChessCrm.Bot.Services;

namespace Tests;

/// <summary>Group D: RemoveFromGroupHandler tests.</summary>
[Collection("Database")]
public class RemoveFromGroupTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture;
    private DatabaseService _db = null!;
    private RemoveFromGroupHandler _handler = null!;
    private int _clientId;
    private int _groupId;

    public RemoveFromGroupTests(TestDatabaseFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await TestHelper.CleanAllAsync(_fixture.ConnectionString);
        var config = TestHelper.CreateConfig(_fixture.ConnectionString);
        _db = TestHelper.CreateDbService(config);
        _handler = TestHelper.CreateRemoveFromGroupHandler(_db);

        _clientId = await TestHelper.InsertClientAsync(_fixture.ConnectionString, "Попов Богдан");
        _groupId = await TestHelper.InsertGroupAsync(
            _fixture.ConnectionString, "вт18", "tuesday", new TimeSpan(18, 45, 0));
    }

    public Task DisposeAsync() => TestHelper.CleanAllAsync(_fixture.ConnectionString);

    // D1: Успешное удаление
    [Fact]
    public async Task D1_SuccessfulRemove()
    {
        await TestHelper.InsertGroupStudentAsync(_fixture.ConnectionString, _clientId, _groupId);

        var result = await _handler.HandleAsync(_clientId, "Попов Богдан", _groupId, "вт18", default);

        Assert.True(result.Success);
        Assert.Contains("убран", result.Message);

        // Verify: no longer in group
        Assert.False(await _db.IsClientInGroupAsync(_clientId, _groupId));
    }

    // D2: Ученик не в группе
    [Fact]
    public async Task D2_NotInGroup()
    {
        var result = await _handler.HandleAsync(_clientId, "Попов Богдан", _groupId, "вт18", default);

        Assert.False(result.Success);
        Assert.Contains("Не удалось убрать", result.Message);
    }
}
