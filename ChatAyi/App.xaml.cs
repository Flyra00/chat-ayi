using System.Diagnostics;

namespace ChatAyi
{
    public partial class App : Application
    {
        public App()
        {
            Debug.WriteLine("=== App constructor started ===");

            try
            {
                InitializeComponent();
                Debug.WriteLine("=== InitializeComponent completed ===");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("=== InitializeComponent FAILED ===");
                Debug.WriteLine(ex.ToString());
                if (ex.InnerException is not null)
                {
                    Debug.WriteLine("=== InitializeComponent INNER EXCEPTION ===");
                    Debug.WriteLine(ex.InnerException.ToString());
                }

                throw;
            }

            Debug.WriteLine("=== Styles loaded from App.xaml merged dictionaries ===");
            
            MainPage = new AppShell();
            Debug.WriteLine("=== MainPage set to AppShell ===");
        }
    }
}
