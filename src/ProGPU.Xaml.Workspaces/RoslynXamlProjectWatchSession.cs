using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace ProGPU.Xaml.Workspaces;

public enum RoslynXamlProjectWatchStatus
{
    Applied,
    AcceptedWithoutRuntimeChange,
    Rejected,
    Superseded,
    Stopped
}

public sealed class RoslynXamlProjectWatchResult
{
    internal RoslynXamlProjectWatchResult(
        long version,
        RoslynXamlProjectWatchStatus status,
        RoslynXamlProjectPreviewUpdate? update,
        RoslynXamlProjectCommitResult? commitResult,
        long committedGeneration,
        TimeSpan duration,
        string message)
    {
        Version = version;
        Status = status;
        Update = update;
        CommitResult = commitResult;
        CommittedGeneration = committedGeneration;
        Duration = duration;
        Message = message;
    }

    public long Version { get; }
    public RoslynXamlProjectWatchStatus Status { get; }
    public RoslynXamlProjectPreviewUpdate? Update { get; }
    public RoslynXamlProjectCommitResult? CommitResult { get; }
    public long CommittedGeneration { get; }
    public TimeSpan Duration { get; }
    public string Message { get; }
    public bool Accepted =>
        CommitResult ==
        RoslynXamlProjectCommitResult.Accepted;
}

/// <summary>
/// Debounces immutable Roslyn project snapshots and routes only the latest prepared
/// candidate through one transactional preview coordinator. Superseded compilation is
/// canceled, no-op semantic updates advance the baseline without invoking the runtime
/// publisher, and rejected candidates preserve the last host-confirmed snapshot.
/// </summary>
public sealed class RoslynXamlProjectWatchSession :
    IDisposable
{
    private static readonly TimeSpan MaximumDebounce =
        TimeSpan.FromMinutes(1);

    private readonly object _gate = new object();
    private readonly RoslynXamlProjectPreviewCoordinator
        _coordinator;
    private readonly Func<
        RoslynXamlProjectPreviewUpdate,
        CancellationToken,
        Task<bool>> _publishAsync;
    private readonly TimeSpan _debounce;
    private readonly CancellationTokenSource _lifetime =
        new CancellationTokenSource();
    private CancellationTokenSource? _pending;
    private long _version;
    private bool _disposed;

    public RoslynXamlProjectWatchSession(
        RoslynXamlProjectPreviewCoordinator coordinator,
        Func<
            RoslynXamlProjectPreviewUpdate,
            CancellationToken,
            Task<bool>> publishAsync,
        TimeSpan? debounce = null)
    {
        _coordinator =
            coordinator ??
            throw new ArgumentNullException(
                nameof(coordinator));
        _publishAsync =
            publishAsync ??
            throw new ArgumentNullException(
                nameof(publishAsync));
        _debounce =
            debounce ??
            TimeSpan.FromMilliseconds(250);
        if (_debounce < TimeSpan.Zero ||
            _debounce > MaximumDebounce)
        {
            throw new ArgumentOutOfRangeException(
                nameof(debounce),
                "The watch debounce must be between zero and one minute.");
        }
    }

    public long Version
    {
        get
        {
            lock (_gate)
                return _version;
        }
    }

    public RoslynXamlProjectPreviewCoordinator
        Coordinator => _coordinator;

    public Task<RoslynXamlProjectWatchResult> SubmitAsync(
        Project project,
        DocumentId xamlDocumentId,
        SourceText? unsavedText = null,
        bool immediate = false,
        CancellationToken cancellationToken = default)
    {
        if (project == null)
            throw new ArgumentNullException(nameof(project));
        if (xamlDocumentId == null)
        {
            throw new ArgumentNullException(
                nameof(xamlDocumentId));
        }

        cancellationToken.ThrowIfCancellationRequested();
        CancellationTokenSource operation;
        long version;
        lock (_gate)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(
                    nameof(
                        RoslynXamlProjectWatchSession));
            }
            version = checked(++_version);
            _pending?.Cancel();
            operation =
                CancellationTokenSource
                    .CreateLinkedTokenSource(
                        _lifetime.Token,
                        cancellationToken);
            _pending = operation;
        }

        return RunAsync(
            version,
            project,
            xamlDocumentId,
            unsavedText,
            immediate,
            cancellationToken,
            operation);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _lifetime.Cancel();
            _pending?.Cancel();
        }

        _lifetime.Dispose();
    }

    private async Task<RoslynXamlProjectWatchResult>
        RunAsync(
            long version,
            Project project,
            DocumentId xamlDocumentId,
            SourceText? unsavedText,
            bool immediate,
            CancellationToken callerToken,
            CancellationTokenSource operation)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            if (!immediate &&
                _debounce > TimeSpan.Zero)
            {
                await Task.Delay(
                        _debounce,
                        operation.Token)
                    .ConfigureAwait(false);
            }

            var update = await PrepareAsync(
                    project,
                    xamlDocumentId,
                    unsavedText,
                    operation.Token)
                .ConfigureAwait(false);
            RoslynXamlProjectCommitResult commit;
            while (true)
            {
                operation.Token
                    .ThrowIfCancellationRequested();
                commit = await ApplyAsync(
                        version,
                        update,
                        operation.Token)
                    .ConfigureAwait(false);
                if (commit !=
                        RoslynXamlProjectCommitResult
                            .RejectedStale ||
                    IsSuperseded(version))
                {
                    break;
                }

                update = await PrepareAsync(
                        project,
                        xamlDocumentId,
                        unsavedText,
                        operation.Token)
                    .ConfigureAwait(false);
            }

            stopwatch.Stop();
            if (commit !=
                    RoslynXamlProjectCommitResult.Accepted &&
                IsSuperseded(version))
            {
                return new RoslynXamlProjectWatchResult(
                    version,
                    RoslynXamlProjectWatchStatus
                        .Superseded,
                    update,
                    commit,
                    _coordinator.Generation,
                    stopwatch.Elapsed,
                    "A newer project snapshot superseded this update.");
            }

            var status = GetStatus(update, commit);
            return new RoslynXamlProjectWatchResult(
                version,
                status,
                update,
                commit,
                _coordinator.Generation,
                stopwatch.Elapsed,
                GetMessage(update, commit, status));
        }
        catch (OperationCanceledException)
            when (IsSuperseded(version))
        {
            stopwatch.Stop();
            return new RoslynXamlProjectWatchResult(
                version,
                RoslynXamlProjectWatchStatus
                    .Superseded,
                update: null,
                commitResult: null,
                _coordinator.Generation,
                stopwatch.Elapsed,
                "A newer project snapshot superseded this update.");
        }
        catch (OperationCanceledException)
            when (IsStopped())
        {
            stopwatch.Stop();
            return new RoslynXamlProjectWatchResult(
                version,
                RoslynXamlProjectWatchStatus.Stopped,
                update: null,
                commitResult: null,
                _coordinator.Generation,
                stopwatch.Elapsed,
                "The project watch session stopped.");
        }
        catch (OperationCanceledException)
        {
            callerToken.ThrowIfCancellationRequested();
            throw;
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(
                        _pending,
                        operation))
                {
                    _pending = null;
                }
            }

            operation.Dispose();
        }
    }

    private Task<RoslynXamlProjectPreviewUpdate>
        PrepareAsync(
            Project project,
            DocumentId xamlDocumentId,
            SourceText? unsavedText,
            CancellationToken cancellationToken) =>
        _coordinator.PrepareAsync(
            project,
            xamlDocumentId,
            unsavedText,
            cancellationToken);

    private Task<RoslynXamlProjectCommitResult>
        ApplyAsync(
            long version,
            RoslynXamlProjectPreviewUpdate update,
            CancellationToken cancellationToken) =>
        _coordinator.ApplyAsync(
            update,
            update.RequiresRuntimePublication
                ? (candidate, token) =>
                    PublishIfCurrentAsync(
                        version,
                        candidate,
                        token)
                : (_, token) =>
                    ConfirmCurrentAsync(
                        version,
                        token),
            cancellationToken);

    private async Task<bool> PublishIfCurrentAsync(
        long version,
        RoslynXamlProjectPreviewUpdate update,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IsSuperseded(version))
            return false;
        return await _publishAsync(
                update,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private Task<bool> ConfirmCurrentAsync(
        long version,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            !IsSuperseded(version));
    }

    private bool IsSuperseded(long version)
    {
        lock (_gate)
            return version != _version;
    }

    private bool IsStopped()
    {
        lock (_gate)
            return _disposed;
    }

    private static RoslynXamlProjectWatchStatus
        GetStatus(
            RoslynXamlProjectPreviewUpdate update,
            RoslynXamlProjectCommitResult commit)
    {
        if (commit !=
            RoslynXamlProjectCommitResult.Accepted)
        {
            return RoslynXamlProjectWatchStatus
                .Rejected;
        }

        return update.RequiresRuntimePublication
            ? RoslynXamlProjectWatchStatus.Applied
            : RoslynXamlProjectWatchStatus
                .AcceptedWithoutRuntimeChange;
    }

    private static string GetMessage(
        RoslynXamlProjectPreviewUpdate update,
        RoslynXamlProjectCommitResult commit,
        RoslynXamlProjectWatchStatus status)
    {
        if (status ==
            RoslynXamlProjectWatchStatus
                .AcceptedWithoutRuntimeChange)
        {
            return "The snapshot was accepted without changing the runtime preview.";
        }

        if (commit ==
            RoslynXamlProjectCommitResult.Accepted)
        {
            return update.IsInitial
                ? "The initial project preview was published."
                : "The project preview delta was published.";
        }

        return update.FailureMessage ??
               "The project preview update was rejected (" +
               commit +
               "); the last good snapshot was retained.";
    }
}
