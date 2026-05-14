using Microsoft.EntityFrameworkCore;
using NightclubWorkforce.Application.Common.Interfaces;
using NightclubWorkforce.Domain.Entities;

namespace NightclubWorkforce.Infrastructure.Persistence;

/// <summary>
/// Seeds realistic bulk test data (50+ rows per table).
/// Safe to run multiple times — every insert is guarded by an existence check.
/// </summary>
public static class BulkSeedData
{
    private static readonly Random Rng = new(42); // fixed seed → reproducible

    public static async Task SeedAsync(AppDbContext db, IPasswordHasher hasher)
    {
        // Skip entirely if bulk data already exists (saves 100+ DB queries on subsequent startups)
        if (await db.WorkLogs.CountAsync() >= 50)
            return;

        // ── 1. Extra locations ──────────────────────────────────────────────────
        var extraLocations = new[]
        {
            "Rooftop Bar", "Private Room A", "Private Room B", "Dance Floor",
            "Pool Area", "Outdoor Terrace", "Karaoke Room", "Sports Bar",
        };
        foreach (var name in extraLocations)
            if (!await db.Locations.AnyAsync(l => l.Name == name))
                db.Locations.Add(Location.Create(name));

        // ── 2. Hourly rates on existing roles ───────────────────────────────────
        await db.SaveChangesAsync();
        var roles = await db.Roles.ToListAsync();
        var rateMap = new Dictionary<string, decimal>
        {
            ["bartender"] = 18m, ["bouncer"] = 20m, ["waitstaff"] = 15m,
            ["dj"]        = 35m, ["manager"] = 45m, ["admin"]     = 50m,
        };
        foreach (var role in roles)
            if (rateMap.TryGetValue(role.Name, out var rate) && role.HourlyRate == 0)
                role.SetRate(rate);

        // ── 3. Overtime rules ───────────────────────────────────────────────────
        if (!await db.OvertimeRules.AnyAsync())
        {
            db.OvertimeRules.Add(OvertimeRule.Create("*",        8m,  1.5m));
            db.OvertimeRules.Add(OvertimeRule.Create("bartender", 6m, 1.5m));
            db.OvertimeRules.Add(OvertimeRule.Create("bouncer",  8m,  1.5m));
            db.OvertimeRules.Add(OvertimeRule.Create("dj",       4m,  2.0m));
        }

        // ── 4. Extra staff members ──────────────────────────────────────────────
        var pwHash = hasher.Hash("password123");
        void AddStaff(string sid, string first, string last, string email,
            List<string> r, string phone, string ic, DateTime dob)
        {
            if (db.Staff.Any(e => e.Email == email || e.StaffId == sid)) return;
            db.Staff.Add(Staff.Create(sid, first, last, email, pwHash,
                r, phone, ic, null, DateTime.SpecifyKind(dob.Date, DateTimeKind.Utc)));
        }

        AddStaff("NC1004","Aisha",  "Rahman",  "aisha@venue.com",   ["bartender"],          "+60 12-3456 7890","950301-04-4004",new(1995,3,1));
        AddStaff("NC1005","Wei",    "Lim",     "wei@venue.com",     ["bouncer"],             "+60 12-4567 8901","920715-05-5005",new(1992,7,15));
        AddStaff("NC1006","Priya",  "Nair",    "priya@venue.com",   ["waitstaff"],           "+60 12-5678 9012","980220-06-6006",new(1998,2,20));
        AddStaff("NC1007","Hafiz",  "Aziz",    "hafiz@venue.com",   ["dj"],                  "+60 12-6789 0123","890410-07-7007",new(1989,4,10));
        AddStaff("NC1008","Nurul",  "Hassan",  "nurul@venue.com",   ["bartender"],           "+60 12-7890 1234","931125-08-8008",new(1993,11,25));
        AddStaff("NC1009","Kevin",  "Tan",     "kevin@venue.com",   ["bouncer"],             "+60 12-8901 2345","870603-09-9009",new(1987,6,3));
        AddStaff("NC1010","Siti",   "Omar",    "siti@venue.com",    ["waitstaff"],           "+60 12-9012 3456","001215-10-0010",new(2000,12,15));
        AddStaff("NC1011","Jason",  "Wong",    "jason@venue.com",   ["bartender","manager"], "+60 13-0123 4567","861030-11-1011",new(1986,10,30));
        AddStaff("NC1012","Fatimah","Ibrahim", "fatimah@venue.com", ["waitstaff"],           "+60 13-1234 5678","990805-12-1212",new(1999,8,5));
        AddStaff("NC1013","Raj",    "Kumar",   "raj@venue.com",     ["bouncer"],             "+60 13-2345 6789","910212-13-1313",new(1991,2,12));
        AddStaff("NC1014","Mei",    "Ling",    "mei@venue.com",     ["bartender"],           "+60 13-3456 7890","940519-14-1414",new(1994,5,19));
        AddStaff("NC1015","Azlan",  "Yusof",   "azlan@venue.com",   ["dj"],                  "+60 13-4567 8901","880913-15-1515",new(1988,9,13));
        AddStaff("NC1016","Grace",  "Chua",    "grace@venue.com",   ["waitstaff"],           "+60 13-5678 9012","970328-16-1616",new(1997,3,28));
        AddStaff("NC1017","Daniel", "Ho",      "daniel@venue.com",  ["bartender"],           "+60 13-6789 0123","900704-17-1717",new(1990,7,4));
        AddStaff("NC1018","Laila",  "Kassim",  "laila@venue.com",   ["bouncer"],             "+60 13-7890 1234","951018-18-1818",new(1995,10,18));
        AddStaff("NC1019","Bryan",  "Ng",      "bryan@venue.com",   ["waitstaff"],           "+60 13-8901 2345","020622-19-1919",new(2002,6,22));
        AddStaff("NC1020","Zara",   "Malik",   "zara@venue.com",    ["bartender"],           "+60 13-9012 3456","931201-20-2020",new(1993,12,1));

        await db.SaveChangesAsync();

        // ── 5. Leave entitlements for all staff ─────────────────────────────────
        var allStaff   = await db.Staff.ToListAsync();
        var thisYear   = DateTime.UtcNow.Year;
        foreach (var s in allStaff)
        {
            if (!await db.LeaveEntitlements.AnyAsync(e => e.EmployeeId == s.Id && e.Year == thisYear))
                db.LeaveEntitlements.Add(LeaveEntitlement.Create(s.Id, thisYear, 14, 14));
            if (!await db.LeaveEntitlements.AnyAsync(e => e.EmployeeId == s.Id && e.Year == thisYear - 1))
                db.LeaveEntitlements.Add(LeaveEntitlement.Create(s.Id, thisYear - 1, 14, 14));
        }
        await db.SaveChangesAsync();

        // ── 6. Work logs — past 90 days, 3-6 shifts per staff ──────────────────
        var locations  = await db.Locations.Where(l => !l.IsDeleted).ToListAsync();
        var staffRoleNames = new[] { "bartender", "bouncer", "waitstaff", "dj" };
        var shifts = new[]   // (start, end)
        {
            ("18:00","22:00"), ("20:00","00:00"), ("21:00","01:00"),
            ("22:00","02:00"), ("23:00","03:00"), ("19:00","23:00"),
            ("17:00","21:00"), ("20:00","23:00"), ("21:00","00:00"),
        };

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        foreach (var s in allStaff)
        {
            // Skip admin/manager-only staff for shift seeding
            var shiftRole = s.Roles.FirstOrDefault(r =>
                staffRoleNames.Contains(r)) ?? "bartender";

            var rate = rateMap.TryGetValue(shiftRole, out var r2) ? r2 : 15m;
            var loc  = locations[Rng.Next(locations.Count)];

            // Generate 5-8 unique shift dates in the past 90 days
            var usedDates = new HashSet<DateOnly>();
            var count     = Rng.Next(5, 9);

            for (int i = 0; i < count; i++)
            {
                DateOnly date;
                var tries = 0;
                do { date = today.AddDays(-Rng.Next(1, 90)); }
                while (!usedDates.Add(date) && ++tries < 20);

                if (!usedDates.Contains(date)) continue;

                // Skip if this staff already has a log on this date
                if (await db.WorkLogs.AnyAsync(w => w.EmployeeId == s.Id && w.WorkDate == date))
                    continue;

                var (start, end) = shifts[Rng.Next(shifts.Length)];
                db.WorkLogs.Add(WorkLog.CreateManual(
                    s.Id, shiftRole, loc.Id, loc.Name,
                    date, start, end, rate,
                    notes: null));
            }
        }
        await db.SaveChangesAsync();

        // ── 7. Leave requests — realistic spread ────────────────────────────────
        var leaveTypes  = new[] { "sick", "annual", "unpaid" };
        var statuses    = new[] { "approved", "approved", "approved", "pending", "rejected" };
        var allStaffIds = allStaff.Select(s => s.Id).ToArray();

        // Aim for at least 50 leave records total
        var existingLeaveCount = await db.LeaveRequests.CountAsync();
        var leaveToAdd = Math.Max(0, 55 - existingLeaveCount);

        for (int i = 0; i < leaveToAdd; i++)
        {
            var empId   = allStaffIds[Rng.Next(allStaffIds.Length)];
            var type    = leaveTypes[Rng.Next(leaveTypes.Length)];
            var status  = statuses[Rng.Next(statuses.Length)];
            var daysOff = Rng.Next(1, 4);
            // Spread over ±180 days from today
            var startOffset = Rng.Next(-120, 60);
            var start = DateTime.UtcNow.Date.AddDays(startOffset);
            var end   = start.AddDays(daysOff - 1);

            // Overlap check — skip if this employee already has leave in this window
            var s = DateTime.SpecifyKind(start, DateTimeKind.Utc);
            var e = DateTime.SpecifyKind(end,   DateTimeKind.Utc);
            var hasOverlap = await db.LeaveRequests.AnyAsync(l =>
                l.EmployeeId == empId &&
                l.Status     != "rejected" &&
                l.StartDate  <= e &&
                l.EndDate    >= s);

            if (hasOverlap) continue;

            var leave = LeaveRequest.Create(empId, type,
                DateTime.SpecifyKind(start, DateTimeKind.Utc),
                DateTime.SpecifyKind(end,   DateTimeKind.Utc),
                notes: null);

            if (status != "pending")
                leave.Update(type,
                    DateTime.SpecifyKind(start, DateTimeKind.Utc),
                    DateTime.SpecifyKind(end,   DateTimeKind.Utc),
                    null, status);

            db.LeaveRequests.Add(leave);
        }
        await db.SaveChangesAsync();

        // ── 8. Announcements ────────────────────────────────────────────────────
        var admin = allStaff.FirstOrDefault(s => s.Roles.Contains("admin"))
                 ?? allStaff.First();

        var announcements = new[]
        {
            ("Welcome to NightCrew! 🎉",
             "All staff please ensure your profile details are up to date. If you have any issues logging in, contact the admin.",
             "*"),
            ("New Overtime Policy",
             "Effective this month, overtime kicks in after 6 hours for bartenders and 8 hours for all other roles. Multiplier is 1.5×.",
             "*"),
            ("Dress Code Reminder",
             "All staff must wear the venue-issued uniform. Black shoes only. No casual wear during operating hours.",
             "*"),
            ("DJ Lineup This Weekend",
             "We have a special guest DJ performing Saturday night. All DJ staff please coordinate with the manager on arrival time.",
             "dj,manager"),
            ("Monthly Payroll Schedule",
             "Payroll is processed on the 28th of each month. Ensure all your shifts are logged correctly by the 25th.",
             "*"),
            ("Staff Meeting — Friday 7PM",
             "Mandatory staff briefing before the weekend rush. Attendance is compulsory. Venue: Private Room A.",
             "*"),
            ("Safety & Emergency Procedures Update",
             "Please review the updated emergency exit plan posted at the entrance. Fire drill scheduled for next Monday.",
             "*"),
            ("VIP Section Protocol",
             "Reminder: VIP guests must be escorted at all times. No unauthorized access to Private Rooms A & B.",
             "bouncer,manager"),
            ("Inventory Count Night",
             "Bar stock count will happen on the last Sunday of the month. Bartenders on duty please stay back 30 mins.",
             "bartender,manager"),
            ("Public Holiday Rates",
             "Shifts on public holidays are paid at 2× the standard rate. Update your work log notes with 'PH' for admin to process.",
             "*"),
            ("Shift Swap Policy",
             "All shift swaps must be approved by the manager at least 24 hours in advance. Self-arranged swaps without approval will not be paid.",
             "*"),
            ("Congratulations Team!",
             "We hit our highest single-night revenue last Saturday. Great work everyone — management is treating the team to a meal this week!",
             "*"),
        };

        foreach (var (title, body, targets) in announcements)
            if (!await db.Announcements.AnyAsync(a => a.Title == title))
                db.Announcements.Add(Announcement.Create(title, body, targets,
                    admin.Id, admin.FullName, expiresAt: null));

        await db.SaveChangesAsync();
    }
}
