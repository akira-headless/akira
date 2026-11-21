namespace Akira.Server.Plugins;

/// <summary>
/// Runs a background service of a plugin so that its failure is logged and the server stays up: the host stops when a
/// hosted service of its own throws.
/// </summary>
public sealed class PluginHostedService : IHostedService, IDisposable
{
    private readonly string _plugin;
    private readonly Func<IHostedService> _create;
    private readonly PluginCatalog _catalog;
    private readonly ILogger<PluginHostedService> _logger;
    private IHostedService? _service;

    public PluginHostedService(
        string plugin,
        Func<IHostedService> create,
        PluginCatalog catalog,
        ILogger<PluginHostedService> logger)
    {
        _plugin = plugin;
        _create = create;
        _catalog = catalog;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Skipped since it registered, when its bridge could not be made
        if (_catalog.Plugins.All(plugin => plugin.Key != _plugin))
        {
            return;
        }

        try
        {
            _service = _create();
            await _service.StartAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "A background service of plugin {Plugin} did not start", _plugin);
            return;
        }

        // A BackgroundService is started once it first waits; what it throws after that only shows in its task
        if (_service is BackgroundService { ExecuteTask: { } running })
        {
            _ = running.ContinueWith(
                task => _logger.LogError(task.Exception?.GetBaseException(), "A background service of plugin {Plugin} stopped on an error", _plugin),
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_service is null)
        {
            return;
        }

        try
        {
            await _service.StopAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "A background service of plugin {Plugin} did not stop cleanly", _plugin);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        (_service as IDisposable)?.Dispose();
    }
}
