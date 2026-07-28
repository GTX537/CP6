namespace CP6.Mobile;

public partial class TaskDetailPage : ContentPage
{
    private readonly TaskDetailViewModel _viewModel;
    public TaskDetailPage(TaskDetailViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.LoadCommand.Execute(null);
    }
}
