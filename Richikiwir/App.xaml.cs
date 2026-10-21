using Microsoft.Maui.Controls;
using Richikiwir.Services;
using Richikiwir.Views;

namespace Richikiwir
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            Page initialPage = AuthService.Current.IsAuthenticated
                ? new AppShell()
                : new NavigationPage(new LoginPage());

            var window = new Window(initialPage)
            {
                Title = "SIM-PKL - Presensi & Monitoring Siswa SMK"
            };

            return window;
        }
    }
}