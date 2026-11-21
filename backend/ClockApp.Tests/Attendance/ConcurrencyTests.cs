using ClockApp.Attendance;
using ClockApp.Attendance.Entities;
using ClockApp.Attendance.Time;
using ClockApp.Tests.Data;
using Microsoft.EntityFrameworkCore;

namespace ClockApp.Tests.Attendance;

/// <summary>
/// A correction and a clock-in for the SAME employee that cover the same moment, started at the same instant.
/// The per-user lock must serialise them: exactly one wins, the other is rejected, and the employee never ends up
/// with overlapping effective records. Runs at service level (no HTTP) with separate DbContexts as the app user,
/// so the two operations really interleave.
/// </summary>
public sealed class ConcurrencyTests : IClassFixture<DatabaseFixture>
{
    private const int Rounds = 30;
    private readonly DatabaseFixture _db;

    public ConcurrencyTests(DatabaseFixture db) => _db = db;

    private AttendanceDbContext AppContext() =>
        new(new DbContextOptionsBuilder<AttendanceDbContext>().UseSqlServer(_db.AppConnectionString).Options);

    private sealed class FixedTime(AttendanceTime time) : IAttendanceTimeSource
    {
        public Task<AttendanceTime> GetNowAsync(CancellationToken ct = default) => Task.FromResult(time);
    }

    [RequiresDbFact]
    public async Task A_correction_racing_a_clock_in_never_both_succeed_and_never_overlaps()
    {
        int punchWins = 0, correctionWins = 0;

        for (var round = 0; round < Rounds; round++)
        {
            // Fresh employee + admin per round (privileged context only for setup).
            int employeeId, adminId;
            await using (var setup = _db.CreateContext())
            {
                var e = new User { Email = $"race-e-{Guid.NewGuid():N}@test.local", FullName = "Race Employee", Role = UserRole.Employee };
                var a = new User { Email = $"race-a-{Guid.NewGuid():N}@test.local", FullName = "Race Admin", Role = UserRole.Admin };
                setup.Users.AddRange(e, a);
                await setup.SaveChangesAsync();
                employeeId = e.Id; adminId = a.Id;
            }

            // The punch's API time (10:15 Zurich) falls inside the correction [10:00, 10:30) Zurich.
            var punchTime = ZurichTime.FromLocal(new DateTime(2026, 2, 20, 10, 15, 0), dstActive: false, "stub");
            var correction = new CorrectionRequest(
                CorrectionKind.Add, employeeId, null,
                DateTimeOffset.Parse("2026-02-20T09:00:00Z"), DateTimeOffset.Parse("2026-02-20T09:30:00Z"), "race test correction");
            var serverNow = DateTimeOffset.Parse("2026-11-21T12:00:00Z"); // "not in the future" reference, a parameter by design

            using var barrier = new Barrier(2);

            var punch = Task.Run(async () =>
            {
                await using var db = AppContext();
                var svc = new AttendanceService(db, new FixedTime(punchTime));
                barrier.SignalAndWait();
                return await svc.ClockInAsync(employeeId);
            });
            var fix = Task.Run(async () =>
            {
                await using var db = AppContext();
                var svc = new CorrectionService(db);
                barrier.SignalAndWait();
                try { await svc.CreateAsync(adminId, correction, serverNow); return true; }
                catch (CorrectionException) { return false; }
            });
            await Task.WhenAll(punch, fix);

            var punchWon = punch.Result.Outcome == ClockOutcome.Recorded;
            var correctionWon = fix.Result;

            // Exactly one operation wins; the loser is rejected with the documented conflict outcome.
            Assert.True(punchWon ^ correctionWon, $"round {round}: punchWon={punchWon}, correctionWon={correctionWon} (outcome {punch.Result.Outcome})");
            if (!punchWon) Assert.Equal(ClockOutcome.OverlapsExisting, punch.Result.Outcome);
            if (punchWon) punchWins++; else correctionWins++;

            // The stored result has no overlapping effective records.
            await using var verify = _db.CreateContext();
            var records = await EffectiveAttendance.LoadAsync(verify, employeeId);
            var single = Assert.Single(records); // one winner => one effective record, never two
            Assert.Equal(punchWon, !single.IsManual);
            for (var i = 0; i < records.Count; i++)
                for (var j = i + 1; j < records.Count; j++)
                    Assert.False(EffectiveAttendance.Overlaps(records[i].In, records[i].Out, records[j].In, records[j].Out));
        }

        // Informational: which side tended to win (both orders are legal).
        Assert.Equal(Rounds, punchWins + correctionWins);
    }

    [RequiresDbFact]
    public async Task An_employee_shift_edit_racing_their_own_clock_in_never_leaves_overlapping_records()
    {
        for (var round = 0; round < Rounds; round++)
        {
            int employeeId;
            await using (var setup = _db.CreateContext())
            {
                var e = new User { Email = $"race-s-{Guid.NewGuid():N}@test.local", FullName = "Race Employee", Role = UserRole.Employee };
                setup.Users.Add(e);
                await setup.SaveChangesAsync();
                employeeId = e.Id;
            }
            var punchTime = ZurichTime.FromLocal(new DateTime(2026, 2, 20, 10, 15, 0), dstActive: false, "stub");
            var edit = new ShiftEditRequest(employeeId, new DateOnly(2026, 2, 20),
                DateTimeOffset.Parse("2026-02-20T09:00:00Z"), DateTimeOffset.Parse("2026-02-20T09:30:00Z"), ReasonCodes.ForgotClockIn, null);
            var serverNow = DateTimeOffset.Parse("2026-11-21T12:00:00Z");
            using var barrier = new Barrier(2);

            var punch = Task.Run(async () =>
            {
                await using var db = AppContext();
                var svc = new AttendanceService(db, new FixedTime(punchTime));
                barrier.SignalAndWait();
                return await svc.ClockInAsync(employeeId);
            });
            var fix = Task.Run(async () =>
            {
                await using var db = AppContext();
                var svc = new ShiftEditService(db, new AlwaysOpenMonthState());
                barrier.SignalAndWait();
                try { await svc.CommitAsync(edit, serverNow); return true; }
                catch (CorrectionException) { return false; }
            });
            await Task.WhenAll(punch, fix);

            var punchWon = punch.Result.Outcome == ClockOutcome.Recorded;
            // Either order is legal: if the punch landed first the edit adjusts that punch (same day), otherwise the
            // edit adds a manual shift and the punch is refused. Never two overlapping effective records.
            Assert.True(punchWon || fix.Result, $"round {round}: both rejected");
            if (!punchWon) Assert.Equal(ClockOutcome.OverlapsExisting, punch.Result.Outcome);

            await using var verify = _db.CreateContext();
            var records = await EffectiveAttendance.LoadAsync(verify, employeeId);
            Assert.Single(records);
        }
    }
}
