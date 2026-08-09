using CP6.Client.Api;

namespace CP6.Mobile;

public partial class TaskListPage : ContentPage
{
    private readonly TaskListViewModel _viewModel;

    public TaskListPage(TaskListViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.Activate();
        _viewModel.LoadCommand.Execute(null);
    }

    protected override void OnDisappearing()
    {
        _viewModel.Deactivate();
        base.OnDisappearing();
    }

    private void Tasks_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is MobileTask task)
            _viewModel.OpenTaskCommand.Execute(task);
        ((CollectionView)sender).SelectedItem = null;
    }
}
