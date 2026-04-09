using FyreApp.Models;
using FyreApp.Services.Clients;
using FyreApp.Tests.Helpers;
using FyreApp.ViewModels.Clients;
using Xunit;

namespace FyreApp.Tests.Services;

public class ClientServiceTests
{
    // -------------------------------------------------------
    // CreateAsync
    // -------------------------------------------------------

    [Fact]
    public async Task CreateAsync_ValidName_ReturnsSuccess()
    {
        using var db = DbContextFactory.Create();
        var sut = new ClientService(db);

        var result = await sut.CreateAsync(new CreateClientVm { Name = "Acme Corp", PrimaryContactName = "Jane", PrimaryContactMobile = "0400000000" });

        Assert.Equal(ClientCreateStatus.Success, result.Status);
        Assert.NotNull(result.ClientId);
    }

    [Fact]
    public async Task CreateAsync_DuplicateName_ReturnsDuplicateStatus()
    {
        using var db = DbContextFactory.Create();
        db.Clients.Add(new Client { Name = "Acme Corp" });
        await db.SaveChangesAsync();

        var sut = new ClientService(db);
        var result = await sut.CreateAsync(new CreateClientVm { Name = "Acme Corp", PrimaryContactName = "Jane", PrimaryContactMobile = "0400000000" });

        Assert.Equal(ClientCreateStatus.DuplicateName, result.Status);
        Assert.NotNull(result.Existing);
    }

    [Fact]
    public async Task CreateAsync_EmptyName_ReturnsValidationError()
    {
        using var db = DbContextFactory.Create();
        var sut = new ClientService(db);

        var result = await sut.CreateAsync(new CreateClientVm { Name = "   ", PrimaryContactName = "Jane", PrimaryContactMobile = "0400000000" });

        Assert.Equal(ClientCreateStatus.ValidationError, result.Status);
    }

    // -------------------------------------------------------
    // GetAllAsync
    // -------------------------------------------------------

    [Fact]
    public async Task GetAllAsync_ActiveOnly_ExcludesInactiveClients()
    {
        using var db = DbContextFactory.Create();
        db.Clients.AddRange(
            new Client { Name = "Active Client", Active = true },
            new Client { Name = "Inactive Client", Active = false }
        );
        await db.SaveChangesAsync();

        var sut = new ClientService(db);
        var result = await sut.GetAllAsync(activeOnly: true);

        Assert.Single(result);
        Assert.Equal("Active Client", result[0].Name);
    }

    [Fact]
    public async Task GetAllAsync_AllClients_IncludesInactive()
    {
        using var db = DbContextFactory.Create();
        db.Clients.AddRange(
            new Client { Name = "Active Client", Active = true },
            new Client { Name = "Inactive Client", Active = false }
        );
        await db.SaveChangesAsync();

        var sut = new ClientService(db);
        var result = await sut.GetAllAsync(activeOnly: false);

        Assert.Equal(2, result.Count);
    }

    // -------------------------------------------------------
    // UpdateAsync
    // -------------------------------------------------------

    [Fact]
    public async Task UpdateAsync_ValidRequest_UpdatesClient()
    {
        using var db = DbContextFactory.Create();
        db.Clients.Add(new Client { Name = "Old Name", Active = true });
        await db.SaveChangesAsync();
        var clientId = db.Clients.First().Id;

        var sut = new ClientService(db);
        var result = await sut.UpdateAsync(clientId, new UpdateClientRequest
        {
            Name = "New Name",
            Active = true
        });

        Assert.Equal(ClientUpdateStatus.Success, result.Status);
        Assert.Equal("New Name", db.Clients.First().Name);
    }

    [Fact]
    public async Task UpdateAsync_DuplicateName_ReturnsDuplicateStatus()
    {
        using var db = DbContextFactory.Create();
        db.Clients.AddRange(
            new Client { Name = "Acme Corp", Active = true },
            new Client { Name = "Other Corp", Active = true }
        );
        await db.SaveChangesAsync();
        var otherCorpId = db.Clients.First(c => c.Name == "Other Corp").Id;

        var sut = new ClientService(db);
        var result = await sut.UpdateAsync(otherCorpId, new UpdateClientRequest
        {
            Name = "Acme Corp",
            Active = true
        });

        Assert.Equal(ClientUpdateStatus.DuplicateName, result.Status);
    }

    // -------------------------------------------------------
    // DeleteAsync
    // -------------------------------------------------------

    [Fact]
    public async Task DeleteAsync_SoftDelete_SetsActiveToFalse()
    {
        using var db = DbContextFactory.Create();
        db.Clients.Add(new Client { Name = "Acme Corp", Active = true });
        await db.SaveChangesAsync();
        var clientId = db.Clients.First().Id;

        var sut = new ClientService(db);
        await sut.DeleteAsync(clientId, hardDelete: false);

        Assert.False(db.Clients.First().Active);
    }

    [Fact]
    public async Task DeleteAsync_HardDelete_RemovesClient()
    {
        using var db = DbContextFactory.Create();
        db.Clients.Add(new Client { Name = "Acme Corp", Active = true });
        await db.SaveChangesAsync();
        var clientId = db.Clients.First().Id;

        var sut = new ClientService(db);
        await sut.DeleteAsync(clientId, hardDelete: true);

        Assert.Empty(db.Clients);
    }

    [Fact]
    public async Task DeleteAsync_NotFound_ReturnsNotFoundStatus()
    {
        using var db = DbContextFactory.Create();
        var sut = new ClientService(db);

        var result = await sut.DeleteAsync(999);

        Assert.Equal(ClientDeleteStatus.NotFound, result.Status);
    }
}