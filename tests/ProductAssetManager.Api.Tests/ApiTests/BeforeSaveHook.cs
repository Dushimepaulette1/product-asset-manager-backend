using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ProductAssetManager.Api.Tests.ApiTests;

// Pauses one matching SaveChanges after its entities were loaded but before any SQL is sent, and runs a competing
// write in that gap. The save then always hits a stale concurrency token, so races are forced by await, not timing.
public class BeforeSaveHook : SaveChangesInterceptor
{
    private Func<DbContext, bool>? _when;
    private Func<Task>? _run;
    private int _armed;
    private int _fired;

    public bool HasFired => Volatile.Read(ref _fired) == 1;

    public void ArmOnce(Func<DbContext, bool> when, Func<Task> run)
    {
        _when = when;
        _run = run;
        Volatile.Write(ref _fired, 0);
        Volatile.Write(ref _armed, 1);
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null
            && Volatile.Read(ref _armed) == 1
            && _when!(eventData.Context)
            && Interlocked.CompareExchange(ref _armed, 0, 1) == 1)
        {
            await _run!();
            Volatile.Write(ref _fired, 1);
        }

        return result;
    }
}
