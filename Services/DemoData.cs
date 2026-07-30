using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Killendar.Models;

namespace Killendar.Services
{
    /// <summary>
    /// Test data. `Killendar.exe --demo` builds a full Demo.kcal and opens it, so every view can
    /// be judged against a realistic calendar instead of an empty grid. Never touches the real
    /// Default.kcal, and rebuilds Demo.kcal from scratch each run so a session always starts from
    /// the same picture.
    ///
    /// This is a TEST fixture, not a marketing sample: it deliberately includes the cases that
    /// break calendar layout. Anything added here should earn its place by exercising something.
    ///   - lane packing:      two- and three-deep overlaps at the same hour
    ///   - month-cell overflow: one day with nine events
    ///   - midnight edges:    a 00:00 start, and a 23:30 patch window crossing into the next day
    ///   - multi-day:         all-day runs (on-call, PTO) that must draw as continuous bars
    ///   - extremes:          a 15-minute call and an 8-hour maintenance window
    ///   - text overflow:     a title long enough to force ellipsis in a month cell
    ///   - font fallback:     accented and CJK titles
    ///   - empty fields:      some events carry location/description/attendees, some carry none
    ///
    /// Dates are generated RELATIVE to the day it runs, so the demo is never stale, and the
    /// randomness is seeded from a constant so two runs on the same day are identical.
    /// </summary>
    internal static class DemoData
    {
        internal const string DemoFileName = "Demo" + EventStore.Extension;

        /// <summary>
        /// Rebuilds Demo.kcal in the data folder, fills it, and makes it the active Killendar.
        /// Returns how many events landed. Deletes any previous Demo.kcal first - stale demo data
        /// accumulating across runs would defeat the point.
        /// </summary>
        internal static int Build()
        {
            Directory.CreateDirectory(EventStore.DataDir);
            var path = Path.Combine(EventStore.DataDir, DemoFileName);

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);

            var store = new EventStore();
            store.Open(path, null);
            int n = store.ImportEvents(Generate(DateTime.Today));
            store.Close();

            EventStore.SetActive(DemoFileName);
            return n;
        }

        /// <summary>
        /// The fixture itself. Pure - takes today's date, returns events, touches no disk - so it
        /// can be called from a test without building a database.
        /// </summary>
        internal static IReadOnlyList<CalendarEvent> Generate(DateTime today)
        {
            // Fixed seed: same day in, same calendar out. A demo that reshuffles every launch
            // makes "did that layout change?" impossible to answer.
            var rng = new Random(20260730);
            var list = new List<CalendarEvent>();

            DateTime monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));   // Monday of this week

            // ---------- the daily/weekly spine: three months back, four months forward ----------
            for (var d = monday.AddDays(-13 * 7); d < monday.AddDays(17 * 7); d = d.AddDays(1))
            {
                bool weekday = d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday;
                if (!weekday) continue;

                // Standup: short, every weekday. The stress case for a 15-minute block in the
                // hour grid, and for a month cell that has to show the same title 20 times.
                Add(list, "Morning standup", d.AddHours(9), d.AddHours(9).AddMinutes(15),
                    location: "Teams",
                    attendees: new[] { "dispatch@example.com", "nate@example.com" });

                // Lunch, most days but not all, so the grid is not a perfect comb.
                if (rng.Next(100) < 80)
                    Add(list, "Lunch", d.AddHours(12), d.AddHours(12).AddMinutes(45));

                // A site visit on roughly half of weekdays.
                if (rng.Next(100) < 55)
                {
                    var (client, site) = Sites[rng.Next(Sites.Length)];
                    int startHour = 10 + rng.Next(6);                  // 10:00 - 15:00
                    int mins = new[] { 30, 45, 60, 90, 120 }[rng.Next(5)];
                    var s = d.AddHours(startHour);
                    Add(list, client + " - " + Jobs[rng.Next(Jobs.Length)], s, s.AddMinutes(mins),
                        location: site,
                        description: "Ticket #" + (10000 + rng.Next(89999)) +
                                     "\nBadge in at reception. Ask for the IDF key.",
                        attendees: new[] { "helpdesk@example.com" });
                }

                // Ticket triage most afternoons, deliberately abutting the 16:00 slot so
                // back-to-back rendering (no gap, no overlap) gets exercised.
                if (rng.Next(100) < 45)
                    Add(list, "Ticket triage", d.AddHours(15).AddMinutes(30), d.AddHours(16));
            }

            // ---------- on-call: multi-day all-day runs, one week in three ----------
            for (int w = -12; w < 16; w += 3)
            {
                var s = monday.AddDays(w * 7);
                AddAllDay(list, "On-call rotation", s, s.AddDays(7),
                          description: "Primary. Escalation path: dispatch, then the duty manager.");
            }

            // ---------- monthly Saturday maintenance window: the 8-hour extreme ----------
            for (int m = -3; m <= 4; m++)
            {
                var first = new DateTime(today.Year, today.Month, 1).AddMonths(m);
                var sat = first;
                while (sat.DayOfWeek != DayOfWeek.Saturday) sat = sat.AddDays(1);
                sat = sat.AddDays(14);                                  // third Saturday
                if (sat.Month != first.Month) sat = sat.AddDays(-7);
                Add(list, "Maintenance window - firmware and reboots", sat.AddHours(8), sat.AddHours(16),
                    location: "Remote",
                    description: "Change window approved. Comms out to all sites the Thursday before.");
            }

            // ---------- patch nights: 23:30 to 01:30, the crossing-midnight case ----------
            for (int w = -10; w < 14; w += 2)
            {
                var wed = monday.AddDays(w * 7 + 2);
                Add(list, "Patch window", wed.AddHours(23).AddMinutes(30), wed.AddDays(1).AddHours(1).AddMinutes(30),
                    location: "Remote",
                    description: "Reboot ring 2. Verify backups completed before starting.");
            }

            // ---------- a 00:00 start: the top edge of the hour grid ----------
            var midnightDay = monday.AddDays(3);
            Add(list, "Overnight restore test", midnightDay, midnightDay.AddHours(3),
                location: "DR site");

            // ---------- deliberate three-deep overlap on one afternoon ----------
            var clash = monday.AddDays(1);
            Add(list, "Change advisory board", clash.AddHours(14), clash.AddHours(15),
                location: "Room 2", attendees: new[] { "cab@example.com" });
            Add(list, "Vendor call - switch RMA", clash.AddHours(14), clash.AddHours(14).AddMinutes(30),
                location: "Phone");
            Add(list, "1:1", clash.AddHours(14).AddMinutes(30), clash.AddHours(15).AddMinutes(30),
                location: "Room 4");

            // ---------- two-deep overlap, offset rather than aligned ----------
            var clash2 = monday.AddDays(9);
            Add(list, "Cutover planning", clash2.AddHours(10), clash2.AddHours(12));
            Add(list, "Interview - tier 2 candidate", clash2.AddHours(11), clash2.AddHours(12),
                location: "Teams");

            // ---------- one deliberately overloaded day: month-cell overflow ----------
            var heavy = monday.AddDays(16);
            for (int i = 0; i < 9; i++)
            {
                var s = heavy.AddHours(8 + i);
                Add(list, HeavyDay[i], s, s.AddMinutes(45));
            }

            // ---------- title long enough to force ellipsis everywhere ----------
            var longDay = monday.AddDays(4);
            Add(list, "Quarterly infrastructure review with the client's security team and their " +
                      "external auditors, including the remediation plan walkthrough",
                longDay.AddHours(13), longDay.AddHours(14).AddMinutes(30),
                location: "Client HQ, 14th floor, the long conference room past the kitchen",
                attendees: new[] { "audit@example.com", "security@example.com", "pm@example.com" });

            // ---------- font fallback: accented and CJK ----------
            var intl = monday.AddDays(11);
            Add(list, "Réunion d'équipe - déploiement réseau", intl.AddHours(9).AddMinutes(30), intl.AddHours(10));
            Add(list, "顧客ミーティング - 移行計画", intl.AddHours(10).AddMinutes(30), intl.AddHours(11));
            Add(list, "Zálohování - kontrola obnovy", intl.AddHours(11).AddMinutes(30), intl.AddHours(12));

            // ---------- all-day singles scattered about ----------
            AddAllDay(list, "Company holiday", monday.AddDays(-10), monday.AddDays(-9));
            AddAllDay(list, "Cert renewal deadline", monday.AddDays(24), monday.AddDays(25),
                      description: "CompTIA. Book the test centre before this drops off.");
            AddAllDay(list, "Inventory audit", monday.AddDays(31), monday.AddDays(32));

            // ---------- PTO: a multi-day all-day run in the future ----------
            AddAllDay(list, "PTO", monday.AddDays(38), monday.AddDays(45),
                      description: "Phone off. Coverage arranged with the other field techs.");

            // ---------- a couple in the past, so paging backwards is not empty ----------
            AddAllDay(list, "Conference", monday.AddDays(-45), monday.AddDays(-42),
                      location: "Convention centre");

            return list;
        }

        // ------------------------------------------------------------------
        // helpers
        // ------------------------------------------------------------------

        private static void Add(List<CalendarEvent> list, string title, DateTime start, DateTime end,
                                string location = "", string description = "", string[]? attendees = null)
            => list.Add(new CalendarEvent
            {
                Title = title,
                Start = start,
                End = end,
                AllDay = false,
                Location = location,
                Description = description,
                Attendees = attendees != null ? attendees.ToList() : new List<string>()
            });

        /// <summary>
        /// All-day event. `end` is EXCLUSIVE, matching the ICS importer and OccursOn: a single
        /// all-day on the 5th is (5th, 6th). Getting this off by one slides every all-day bar.
        /// </summary>
        private static void AddAllDay(List<CalendarEvent> list, string title, DateTime start, DateTime end,
                                      string location = "", string description = "")
            => list.Add(new CalendarEvent
            {
                Title = title,
                Start = start.Date,
                End = end.Date,
                AllDay = true,
                Location = location,
                Description = description,
                Attendees = new List<string>()
            });

        private static readonly (string Client, string Site)[] Sites =
        {
            ("Northgate Dental",     "412 Northgate Ave, Suite 200"),
            ("Prairie Credit Union", "88 Main St - branch 3"),
            ("Hollis Manufacturing", "Plant 2, receiving door"),
            ("Verdant Property Mgmt","1600 Lakeshore, 3rd floor"),
            ("Cardinal Law",         "Ste 900, parking validated"),
            ("Bell & Fisk Accounting","221 River Rd"),
            ("Summit Orthopedics",   "Clinic B, IT closet behind reception"),
            ("Ridgeline Logistics",  "Yard office, gate 4"),
        };

        private static readonly string[] Jobs =
        {
            "workstation swap",
            "AP replacement",
            "switch stack firmware",
            "printer mapping",
            "new hire setup",
            "backup verification",
            "VoIP handset rollout",
            "UPS battery replacement",
            "cabling drop",
            "server room walkthrough",
        };

        private static readonly string[] HeavyDay =
        {
            "Standup",
            "Handover notes",
            "Prairie CU - AP replacement",
            "Lunch",
            "Vendor call - licensing",
            "Hollis - switch firmware",
            "Escalation review",
            "Timesheets",
            "End of day wrap",
        };
    }
}
