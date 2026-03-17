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

            try
            {
                Resources ??= new ResourceDictionary();
                Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("Resources/Styles/Styles.xaml", UriKind.Relative)
                });
                Debug.WriteLine("=== Styles.xaml loaded ===");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"=== Styles.xaml failed to load: {ex} ===");
            }
            
            MainPage = new AppShell();
            Debug.WriteLine("=== MainPage set to AppShell ===");
        }
    }
}
