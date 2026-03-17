namespace ChatAyi;

public partial class MainPage : ContentPage
{
    private int count = 0;

    public MainPage()
    {
        InitializeComponent();
    }

    private void OnButtonClicked(object sender, EventArgs e)
    {
        count++;
        if (count == 1)
            ((Button)sender).Text = $"Clicked {count} time";
        else
            ((Button)sender).Text = $"Clicked {count} times";
    }
}
