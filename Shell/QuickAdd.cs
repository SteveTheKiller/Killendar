using System.Globalization;
using System.Windows;
using Killendar.Controls;
using Killendar.Services;

// Sentence box (Q): type a line like "lunch fri 12:30 at Mel's", see how it reads, and Enter
// opens the edit panel filled in. Nothing is saved until Save in the panel.
namespace Killendar.Shell
{
    public partial class MainWindow
    {
        private void OpenQuickAdd()
        {
            if (!_store.IsOpen || _security.IsLocked) return;
            // Settle an unsaved draft first, so a typed sentence is never thrown away by the
            // question that comes after it.
            if (!_appointments.ConfirmReplace()) return;
            var dlg = new QuickAddDialog { Owner = this };
            dlg.ShowDialog();
            if (dlg.Result is QuickAddResult line && _appointments.NewFromQuickAdd(line))
                _calendar.GoTo(line.Start.Date);
        }

        internal static QuickAddWords LocalQuickAddWords()
        {
            string L(string key, string fallback) =>
                Application.Current?.TryFindResource(key) as string ?? fallback;
            var en = QuickAddWords.English;
            return new QuickAddWords(
                today: L("Str_QA_WordToday", en.Today), tomorrow: L("Str_QA_WordTomorrow", en.Tomorrow),
                next: L("Str_QA_WordNext", en.Next), at: L("Str_QA_WordAt", en.At),
                forWord: L("Str_QA_WordFor", en.For), allDay: L("Str_QA_WordAllDay", en.AllDay),
                noon: L("Str_QA_WordNoon", en.Noon));
        }

        internal static QuickAddResult ReadQuickAdd(string text) =>
            QuickAdd.Parse(text, System.DateTime.Now, CultureInfo.CurrentCulture, LocalQuickAddWords());
    }
}
