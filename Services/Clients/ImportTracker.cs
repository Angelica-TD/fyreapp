using System.Collections.Concurrent;
using FyreApp.Dtos;

namespace FyreApp.Services.Clients;

public interface IImportTracker
{
    void Set(string id, ClientImportProgressDto progress);
    ClientImportProgressDto? Get(string id);
}

public class ImportTracker : IImportTracker
{
    private readonly ConcurrentDictionary<string, ClientImportProgressDto> _store = new();

    public void Set(string id, ClientImportProgressDto progress) => _store[id] = progress;
    public ClientImportProgressDto? Get(string id) => _store.TryGetValue(id, out var p) ? p : null;
}
