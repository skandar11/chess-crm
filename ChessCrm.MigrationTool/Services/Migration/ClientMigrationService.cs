using System.Text.RegularExpressions;
using ChessCrm.MigrationTool.Database;
using ChessCrm.MigrationTool.Dtos;
using ChessCrm.MigrationTool.Entities;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace ChessCrm.MigrationTool.Services.Migration;

public class ClientMigrationService
{
    private readonly AppDbContext _dbContext;

    public ClientMigrationService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task MigrateAsync(List<RawClientDto> clientsDto)
    {
        var existingClientExternalIds = await _dbContext.Clients
            .Select(c => c.ExternalId)
            .Where(id => id != null)
            .ToHashSetAsync();

        var newClients = new List<Client>();
        foreach (var dto in clientsDto)
        {
            if (!string.IsNullOrWhiteSpace(dto.Id) && existingClientExternalIds.Contains(dto.Id))
            {
                continue;
            }

            var client = new Client
            {
                LastName = dto.LastName.Trim(),
                FirstName = ToNullIfEmpty(dto.FirstName),
                MiddleName = ToNullIfEmpty(dto.MiddleName),
                Parent1Name = ToNullIfEmpty(dto.Parent1),
                Parent1Phone = NormalizePhone(dto.Phone1),
                Parent2Name = ToNullIfEmpty(dto.Parent2),
                Parent2Phone = NormalizePhone(dto.Phone2),

                Status = Enums.ClientStatus.Active,
                GoogleSheetRowIndex = dto.RowIndex,
                ExternalId = ToNullIfEmpty(dto.Id)
            };

            if (DateOnly.TryParse(dto.BirthDate, out var birthDate))
            {
                client.BirthDate = birthDate;
            }

            newClients.Add(client);
        }

        if (newClients.Any())
        {
            _dbContext.Clients.AddRange(newClients);
            await _dbContext.SaveChangesAsync();
            Log.Information("Успешно сохранено {Count} новых клиентов в БД.", newClients.Count);
        }
        else
        {
            Log.Information("Новых клиентов для добавления не найдено.");
        }
    }

    private string? ToNullIfEmpty(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.Trim();
    }

    private string? NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;
        var digitsOnly = Regex.Replace(phone, @"[^\d]", "");
        if (digitsOnly.StartsWith("8"))
        {
            digitsOnly = "7" + digitsOnly.Substring(1);
        }
        return digitsOnly.Length == 11 ? digitsOnly : null;
    }
}