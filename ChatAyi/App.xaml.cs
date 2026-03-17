using System.Diagnostics;

namespace ChatAyi
{
    public partial class App : Application
    {
        public App()
        {
            Debug.WriteLine("=== App constructor started ===");
            
            InitializeComponent();
            Debug.WriteLine("=== InitializeComponent completed ===");

            Debug.WriteLine("=== Styles loaded from App.xaml merged dictionaries ===");
            
            MainPage = new AppShell();
            Debug.WriteLine("=== MainPage set to AppShell ===");
        }
    }
}
