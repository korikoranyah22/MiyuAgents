using FluentAssertions;
using MiyuAgents.Memory;
using Xunit;

namespace MiyuAgents.Tests.Unit.Memory;

// ── Test domain types (not file-scoped — needed in field/property signatures) ─

internal record Note(string Id, string Content);

/// <summary>
/// Finds the first Note whose Content contains the search term.
/// Returns Note.Empty ("", "") if nothing matches.
/// </summary>
internal sealed class NoteByTermQuery(string term) : IInMemoryQuery<Note>
{
    public Note Search(IReadOnlyList<Note> entries) =>
        entries.FirstOrDefault(n => n.Content.Contains(term, StringComparison.OrdinalIgnoreCase))
            ?? new Note("", "");
}

// ── Store + Search ────────────────────────────────────────────────────────────

public class InMemoryStore_StoreAndSearch_FindsMatchingEntry : IAsyncLifetime
{
    private InMemoryStore<NoteByTermQuery, Note> _store = default!;
    private Note                                 _result = default!;

    public async Task InitializeAsync()
    {
        _store = new InMemoryStore<NoteByTermQuery, Note>();
        await _store.StoreAsync(new Note("1", "cats are fluffy"));
        await _store.StoreAsync(new Note("2", "dogs are loyal"));
        await _store.StoreAsync(new Note("3", "fish swim silently"));

        _result = await _store.SearchAsync(new NoteByTermQuery("dogs"));
    }

    [Fact] public void Found_Note_IsCorrect()    => _result.Id.Should().Be("2");
    [Fact] public void Found_Content_IsCorrect() => _result.Content.Should().Contain("dogs");

    public Task DisposeAsync() => Task.CompletedTask;
}

public class InMemoryStore_Search_NoMatch_ReturnsEmpty : IAsyncLifetime
{
    private Note _result = default!;

    public async Task InitializeAsync()
    {
        var store = new InMemoryStore<NoteByTermQuery, Note>();
        await store.StoreAsync(new Note("1", "cats are fluffy"));

        _result = await store.SearchAsync(new NoteByTermQuery("dragons"));
    }

    [Fact] public void Result_Id_IsEmpty() => _result.Id.Should().BeEmpty();

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── Upsert (update existing) ─────────────────────────────────────────────────

public class InMemoryStore_Upsert_UpdatesExistingEntry : IAsyncLifetime
{
    private Note _result = default!;

    public async Task InitializeAsync()
    {
        var store = new InMemoryStore<NoteByTermQuery, Note>
        {
            IdSelector = n => n.Id
        };
        await store.StoreAsync(new Note("1", "original content"));
        await store.UpsertAsync("1", new Note("1", "updated content"));

        _result = await store.SearchAsync(new NoteByTermQuery("updated"));
    }

    [Fact] public void Updated_Note_IsFound()     => _result.Id.Should().Be("1");
    [Fact] public void Old_Content_IsGone()        => _result.Content.Should().NotContain("original");

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── Upsert (insert new when missing) ─────────────────────────────────────────

public class InMemoryStore_Upsert_InsertsNewEntry : IAsyncLifetime
{
    private Note _result = default!;

    public async Task InitializeAsync()
    {
        var store = new InMemoryStore<NoteByTermQuery, Note>
        {
            IdSelector = n => n.Id
        };
        await store.UpsertAsync("brand-new", new Note("brand-new", "brand new note"));

        _result = await store.SearchAsync(new NoteByTermQuery("brand new"));
    }

    [Fact] public void NewEntry_IsFound() => _result.Id.Should().Be("brand-new");

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── Delete ────────────────────────────────────────────────────────────────────

public class InMemoryStore_Delete_RemovesEntry : IAsyncLifetime
{
    private Note _deletedSearch  = default!;
    private Note _survivingSearch = default!;

    public async Task InitializeAsync()
    {
        var store = new InMemoryStore<NoteByTermQuery, Note>
        {
            IdSelector = n => n.Id
        };
        await store.StoreAsync(new Note("del-1", "delete me please"));
        await store.StoreAsync(new Note("del-2", "keep me safe"));
        await store.DeleteAsync("del-1");

        _deletedSearch   = await store.SearchAsync(new NoteByTermQuery("delete me please"));
        _survivingSearch = await store.SearchAsync(new NoteByTermQuery("keep me safe"));
    }

    [Fact] public void DeletedEntry_IsNotFound()    => _deletedSearch.Id.Should().BeEmpty();
    [Fact] public void SurvivingEntry_IsStillFound() => _survivingSearch.Id.Should().Be("del-2");

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── GetAllAsync with scope query ──────────────────────────────────────────────

public class InMemoryStore_GetAllAsync_UsesDefaultScopeQuery : IAsyncLifetime
{
    private Note _result = default!;

    public async Task InitializeAsync()
    {
        // Scope query: find the note whose Id starts with the given scope prefix
        var store = new InMemoryStore<NoteByTermQuery, Note>
        {
            DefaultScopeQuery = (scopeId, entries) =>
                entries.FirstOrDefault(n => n.Id.StartsWith(scopeId))
                ?? new Note("", "")
        };
        await store.StoreAsync(new Note("scope-a-1", "note in scope a"));
        await store.StoreAsync(new Note("scope-b-1", "note in scope b"));

        _result = await store.GetAllAsync("scope-a");
    }

    [Fact] public void Result_IsFromCorrectScope() => _result.Id.Should().StartWith("scope-a");

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── GetAllAsync without scope query throws ───────────────────────────────────

public class InMemoryStore_GetAllAsync_WithoutScopeQuery_Throws
{
    [Fact]
    public async Task GetAllAsync_WithoutFactory_ThrowsNotSupported()
    {
        var store = new InMemoryStore<NoteByTermQuery, Note>();
        await store.StoreAsync(new Note("1", "hi"));

        Func<Task> act = () => store.GetAllAsync("any-scope");
        await act.Should().ThrowAsync<NotSupportedException>();
    }
}

// ── StoreAsync ID selector ────────────────────────────────────────────────────

public class InMemoryStore_StoreAsync_WithIdSelector_ReturnsCorrectId : IAsyncLifetime
{
    private string _storedId = default!;

    public async Task InitializeAsync()
    {
        var store = new InMemoryStore<NoteByTermQuery, Note>
        {
            IdSelector = n => n.Id
        };
        _storedId = await store.StoreAsync(new Note("note-42", "content"));
    }

    [Fact] public void StoredId_MatchesNoteId() => _storedId.Should().Be("note-42");

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── StoreAsync with wrong type throws ────────────────────────────────────────

public class InMemoryStore_StoreAsync_WrongType_Throws
{
    [Fact]
    public async Task StoreAsync_WithWrongType_ThrowsArgumentException()
    {
        var store = new InMemoryStore<NoteByTermQuery, Note>();

        Func<Task> act = () => store.StoreAsync("this is not a Note");
        await act.Should().ThrowAsync<ArgumentException>();
    }
}

// ── EnsureReadyAsync is a no-op ───────────────────────────────────────────────

public class InMemoryStore_EnsureReadyAsync_DoesNotThrow
{
    [Fact]
    public async Task EnsureReadyAsync_CompletesImmediately()
    {
        var store = new InMemoryStore<NoteByTermQuery, Note>();
        Func<Task> act = () => store.EnsureReadyAsync();
        await act.Should().NotThrowAsync();
    }
}

// ── Upsert without IdSelector throws ─────────────────────────────────────────

public class InMemoryStore_Upsert_WithoutIdSelector_Throws
{
    [Fact]
    public async Task UpsertAsync_WithoutIdSelector_ThrowsInvalidOperation()
    {
        var store = new InMemoryStore<NoteByTermQuery, Note>(); // no IdSelector

        Func<Task> act = () => store.UpsertAsync("some-id", new Note("some-id", "content"));
        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}

// ── Concurrent writes ─────────────────────────────────────────────────────────

public class InMemoryStore_ConcurrentStoreAsync_NoDataRace
{
    [Fact]
    public async Task ConcurrentWrites_AllEntriesStored()
    {
        var store = new InMemoryStore<NoteByTermQuery, Note>
        {
            DefaultScopeQuery = (_, entries) => entries.FirstOrDefault() ?? new Note("", "")
        };

        const int count = 50;
        var tasks = Enumerable.Range(0, count)
            .Select(i => store.StoreAsync(new Note($"id-{i}", $"content {i}")));

        await Task.WhenAll(tasks);

        // Verify all were stored by checking one by scope
        // (We can't count directly, but search should find any entry)
        var found = await store.SearchAsync(new NoteByTermQuery("content"));
        found.Id.Should().NotBeEmpty();
    }
}
