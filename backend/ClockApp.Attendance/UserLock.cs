using Microsoft.EntityFrameworkCore;

namespace ClockApp.Attendance;

/// <summary>
/// Per-user application lock (sp_getapplock, held until the surrounding transaction ends).
/// Serialises punches and corrections of ONE user so that "check for overlap, then insert" cannot race
/// (e.g. an admin correction and a simultaneous clock-in can never both succeed into an overlap).
/// Other users are unaffected.
/// </summary>
internal static class UserLock
{
    public static async Task AcquireAsync(AttendanceDbContext db, int userId, CancellationToken ct)
    {
        var resource = $"att:{userId}";
        var result = await db.Database
            .SqlQuery<int>($"DECLARE @r int; EXEC @r = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 5000; SELECT @r AS Value")
            .ToListAsync(ct);
        // 0/1 = granted; negative = timeout, cancelled, deadlock or error.
        if (result.Single() < 0)
            throw new InvalidOperationException($"Could not obtain the attendance lock for user {userId}.");
    }
}
