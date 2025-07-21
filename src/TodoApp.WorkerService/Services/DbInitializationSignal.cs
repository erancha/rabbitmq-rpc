using System.Threading.Tasks;

namespace TodoApp.WorkerService.Services;

public class DbInitializationSignal
{
    private readonly TaskCompletionSource<bool> _initializationCompleted = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );

    public Task Initialization => _initializationCompleted.Task;

    public void MarkAsComplete() => _initializationCompleted.TrySetResult(true);
}
