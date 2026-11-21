namespace ClockApp.Attendance;

public enum MonthState { Open, PendingApproval, Approved }

/// <summary>
/// Seam for month locking (Milestone 2). Milestone 1 uses <see cref="AlwaysOpenMonthState"/>, so every month is
/// editable; Milestone 2 replaces it with a reader over the month submission events.
/// </summary>
public interface IMonthStateReader
{
    Task<MonthState> GetStateAsync(int userId, int year, int month, CancellationToken ct = default);
}

public sealed class AlwaysOpenMonthState : IMonthStateReader
{
    public Task<MonthState> GetStateAsync(int userId, int year, int month, CancellationToken ct = default) =>
        Task.FromResult(MonthState.Open);
}
