using System.Windows;
using Killendar.Features;

// The three seams every feature shares. Implemented once here; each feature's own interface
// extends IShellServices, so all of them are satisfied by these.
namespace Killendar.Shell
{
    public partial class MainWindow : IShellServices
    {
        Window IShellServices.Window => this;

        string IShellServices.Loc(string key) => Loc(key);

        void IShellServices.SetStatus(string text) => StatusText.Text = text;
    }
}
