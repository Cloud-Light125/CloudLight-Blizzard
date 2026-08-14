namespace CloudLightBlizzard.Stats;

public enum StatsQueryState
{
    Idle,
    LoginRequired,
    ReadyToQuery,
    Loading,
    Loaded,
    Error,
}

public sealed record StatsAccountSelection(string CacheKey, bool IsChinaRegion);

/// <summary>
/// Owns the explicit-query contract for the stats page. Selection, navigation and account switches are read-only;
/// only QueryAsync and LoginAsync may invoke their supplied external actions.
/// </summary>
public sealed class StatsQueryWorkflow
{
    private readonly Dictionary<string, object> _memoryCache = new(StringComparer.Ordinal);

    public event Action? Changed;
    public StatsAccountSelection? Selection { get; private set; }
    public StatsQueryState State { get; private set; } = StatsQueryState.Idle;
    public object? CurrentResult { get; private set; }
    public bool IsBusy { get; private set; }
    public string ErrorMessage { get; private set; } = "";

    public void PageOpened() => Changed?.Invoke();

    public void SelectAccount(StatsAccountSelection? selection)
    {
        Selection = selection;
        ErrorMessage = "";
        if (selection is not null && _memoryCache.TryGetValue(selection.CacheKey, out var cached))
        {
            CurrentResult = cached;
            State = StatsQueryState.Loaded;
        }
        else
        {
            CurrentResult = null;
            State = StatsQueryState.Idle;
        }
        Changed?.Invoke();
    }

    public async Task QueryAsync(
        Func<Task<bool>> checkChinaLogin,
        Func<Task<object>> queryChina,
        Func<Task<object>> queryInternational)
    {
        if (Selection is not { } requested || IsBusy) return;
        IsBusy = true;
        State = StatsQueryState.Loading;
        ErrorMessage = "";
        Changed?.Invoke();
        try
        {
            if (requested.IsChinaRegion && !await checkChinaLogin())
            {
                if (Selection?.CacheKey == requested.CacheKey) State = StatsQueryState.LoginRequired;
                return;
            }

            var result = requested.IsChinaRegion ? await queryChina() : await queryInternational();
            _memoryCache[requested.CacheKey] = result;
            if (Selection?.CacheKey == requested.CacheKey)
            {
                CurrentResult = result;
                State = StatsQueryState.Loaded;
            }
        }
        catch (Exception ex)
        {
            if (Selection?.CacheKey == requested.CacheKey)
            {
                ErrorMessage = ex.Message;
                State = StatsQueryState.Error;
            }
        }
        finally
        {
            IsBusy = false;
            Changed?.Invoke();
        }
    }

    public async Task LoginAsync(Func<Task<bool>> showLoginDialog)
    {
        if (Selection is not { IsChinaRegion: true } requested || IsBusy || State != StatsQueryState.LoginRequired) return;
        IsBusy = true;
        Changed?.Invoke();
        try
        {
            if (await showLoginDialog() && Selection?.CacheKey == requested.CacheKey)
                State = StatsQueryState.ReadyToQuery;
        }
        finally
        {
            IsBusy = false;
            Changed?.Invoke();
        }
    }
}
