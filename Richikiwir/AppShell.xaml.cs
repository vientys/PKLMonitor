using Microsoft.Maui.Controls;
using Richikiwir.Views;

namespace Richikiwir
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();
            Routing.RegisterRoute(nameof(AttendancePage), typeof(AttendancePage));
            Routing.RegisterRoute(nameof(AccountManagementPage), typeof(AccountManagementPage));
            Routing.RegisterRoute(nameof(LoginPage), typeof(LoginPage));
        }
    }
}
