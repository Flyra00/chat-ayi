namespace ChatAyi.Pages;

public partial class GetStartedPage : ContentPage
{
    private bool _hasAnimated;

    public GetStartedPage()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_hasAnimated)
            return;

        _hasAnimated = true;
        Root.TranslationY = 12;

        await Task.WhenAll(
            Root.FadeTo(1, 350, Easing.CubicOut),
            Root.TranslateTo(0, 0, 350, Easing.CubicOut)
        );
    }

    private async void OnGetStartedClicked(object sender, EventArgs e)
    {
        StartButton.IsEnabled = false;
        try
        {
            await Shell.Current.GoToAsync("chat?fresh=1");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GetStarted] Navigation to chat failed: {ex}");
            await DisplayAlert("Navigation Error", ex.Message, "OK");
        }
        finally
        {
            StartButton.IsEnabled = true;
        }
    }
}
