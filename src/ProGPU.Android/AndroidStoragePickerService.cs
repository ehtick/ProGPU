using Android.App;
using Android.Content;
using Android.Database;
using Android.Provider;
using Android.Webkit;
using Microsoft.UI.Xaml;
using AndroidUri = Android.Net.Uri;

namespace ProGPU.Android;

/// <summary>
/// Adapts Android's Storage Access Framework to the path-shaped WinUI storage seam.
/// Open documents are materialized into the app cache. Save documents retain their
/// content URI and synchronize every framework write through ContentResolver.
/// </summary>
internal sealed class AndroidStoragePickerService : IDisposable
{
    private const int OpenMode = 0;
    private const int SaveMode = 1;
    private const int FolderMode = 2;
    private const int FirstRequestCode = 0x5A20;

    private readonly Activity _activity;
    private readonly string _sessionDirectory;
    private readonly SemaphoreSlim _pickerGate = new(1, 1);
    private readonly object _stateLock = new();
    private readonly Dictionary<string, string> _documentUris = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _writableDocuments = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _folderTrees = new(StringComparer.Ordinal);
    private TaskCompletionSource<string?>? _activeCompletion;
    private int _activeRequestCode;
    private int _activeMode = -1;
    private int _nextRequestCode = FirstRequestCode;
    private bool _disposed;

    public AndroidStoragePickerService(Activity activity)
    {
        _activity = activity ?? throw new ArgumentNullException(nameof(activity));
        string cache = activity.CacheDir?.AbsolutePath ?? Path.GetTempPath();
        _sessionDirectory = Path.Combine(cache, "ProGPU.Pickers", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sessionDirectory);
    }

    public async Task<string?> PickPathAsync(
        int mode,
        IReadOnlyList<string>? fileTypes,
        string? suggestedFileName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (mode is not (OpenMode or SaveMode or FolderMode))
            throw new ArgumentOutOfRangeException(nameof(mode));

        await _pickerGate.WaitAsync().ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            int requestCode;
            lock (_stateLock)
            {
                requestCode = _nextRequestCode++;
                if (_nextRequestCode > FirstRequestCode + 1024) _nextRequestCode = FirstRequestCode;
                _activeRequestCode = requestCode;
                _activeMode = mode;
                _activeCompletion = completion;
            }

            var intent = CreatePickerIntent(mode, fileTypes, suggestedFileName);
            _activity.RunOnUiThread(() =>
            {
                try
                {
                    _activity.StartActivityForResult(intent, requestCode);
                }
                catch (Exception exception)
                {
                    CompleteActivePicker(requestCode, exception: exception);
                }
            });
            return await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            _pickerGate.Release();
        }
    }

    public bool HandleActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        int mode;
        lock (_stateLock)
        {
            if (_activeCompletion == null || requestCode != _activeRequestCode) return false;
            mode = _activeMode;
        }

        if (resultCode != Result.Ok || data?.Data is not { } uri)
        {
            CompleteActivePicker(requestCode, result: null);
            return true;
        }

        TryPersistAccess(uri, data.Flags, mode);
        _ = MaterializeSelectionAsync(requestCode, mode, uri);
        return true;
    }

    public async Task<bool> WriteTextAsync(string path, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(text);
        return await WriteExternalAsync(path, async stream =>
        {
            using var writer = new StreamWriter(stream, leaveOpen: true);
            await writer.WriteAsync(text).ConfigureAwait(false);
            await writer.FlushAsync().ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public Task<bool> WriteBytesAsync(string path, byte[] bytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(bytes);
        return WriteExternalAsync(path, stream => stream.WriteAsync(bytes).AsTask());
    }

    public async Task<string> ReadTextAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string? uriText = GetDocumentUri(path);
        if (uriText == null) return await File.ReadAllTextAsync(path).ConfigureAwait(false);
        await using Stream stream = OpenDocumentForRead(uriText);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync().ConfigureAwait(false);
    }

    public async Task<byte[]> ReadBytesAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string? uriText = GetDocumentUri(path);
        if (uriText == null) return await File.ReadAllBytesAsync(path).ConfigureAwait(false);
        await using Stream stream = OpenDocumentForRead(uriText);
        using var output = new MemoryStream();
        await stream.CopyToAsync(output).ConfigureAwait(false);
        return output.ToArray();
    }

    public Task<IReadOnlyList<string>> EnumerateFilesAsync(string folderPath) =>
        EnumerateChildrenAsync(folderPath, folders: false);

    public Task<IReadOnlyList<string>> EnumerateFoldersAsync(string folderPath) =>
        EnumerateChildrenAsync(folderPath, folders: true);

    public Task<string> CreateFileAsync(string folderPath, string desiredName) =>
        CreateDocumentAsync(folderPath, desiredName, ResolveMimeType(desiredName));

    public Task<string> CreateFolderAsync(string folderPath, string desiredName) =>
        CreateDocumentAsync(folderPath, desiredName, DocumentsContract.Document.MimeTypeDir);

    private Intent CreatePickerIntent(
        int mode,
        IReadOnlyList<string>? fileTypes,
        string? suggestedFileName)
    {
        var intent = new Intent(mode switch
        {
            OpenMode => Intent.ActionOpenDocument,
            SaveMode => Intent.ActionCreateDocument,
            FolderMode => Intent.ActionOpenDocumentTree,
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        });
        intent.AddFlags(
            ActivityFlags.GrantReadUriPermission |
            ActivityFlags.GrantWriteUriPermission |
            ActivityFlags.GrantPersistableUriPermission |
            ActivityFlags.GrantPrefixUriPermission);

        if (mode != FolderMode)
        {
            intent.AddCategory(Intent.CategoryOpenable);
            string[] mimeTypes = ResolveMimeTypes(fileTypes);
            intent.SetType(mimeTypes.Length == 1 ? mimeTypes[0] : "*/*");
            if (mimeTypes.Length > 1) intent.PutExtra(Intent.ExtraMimeTypes, mimeTypes);
        }
        if (mode == SaveMode)
            intent.PutExtra(Intent.ExtraTitle, ResolveSuggestedFileName(suggestedFileName, fileTypes));
        return intent;
    }

    private async Task MaterializeSelectionAsync(int requestCode, int mode, AndroidUri uri)
    {
        try
        {
            string displayName = QueryDisplayName(uri) ?? (mode == FolderMode ? "Folder" : "document");
            string result;
            if (mode == FolderMode)
            {
                result = CreateUniqueLocalDirectory("Folders", displayName);
                string uriText = uri.ToString() ?? string.Empty;
                lock (_stateLock)
                {
                    _folderTrees[result] = uriText;
                    _documentUris[result] = uriText;
                }
            }
            else
            {
                string directory = CreateUniqueLocalDirectory(mode == OpenMode ? "Open" : "Save", null);
                result = Path.Combine(
                    directory,
                    AndroidStorageNamePolicy.SanitizeFileName(displayName, "document"));
                if (mode == OpenMode)
                {
                    await using Stream? input = _activity.ContentResolver?.OpenInputStream(uri);
                    if (input == null) throw new IOException($"The Android document provider did not open '{uri}'.");
                    await using var output = new FileStream(
                        result,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        81920,
                        FileOptions.Asynchronous);
                    await input.CopyToAsync(output).ConfigureAwait(false);
                }
                else
                {
                    await File.WriteAllBytesAsync(result, []).ConfigureAwait(false);
                    string uriText = uri.ToString() ?? string.Empty;
                    lock (_stateLock)
                    {
                        _writableDocuments[result] = uriText;
                        _documentUris[result] = uriText;
                    }
                }
            }
            CompleteActivePicker(requestCode, result: result);
        }
        catch (Exception exception)
        {
            CompleteActivePicker(requestCode, exception: exception);
        }
    }

    private async Task<bool> WriteExternalAsync(string path, Func<Stream, Task> write)
    {
        string? uriText;
        lock (_stateLock) _writableDocuments.TryGetValue(path, out uriText);
        if (uriText == null) return false;

        AndroidUri? uri = AndroidUri.Parse(uriText);
        if (uri == null) return false;
        await using Stream? stream = _activity.ContentResolver?.OpenOutputStream(uri, "wt");
        if (stream == null) throw new IOException($"The Android document provider did not open '{uri}' for writing.");
        await write(stream).ConfigureAwait(false);
        await stream.FlushAsync().ConfigureAwait(false);
        return true;
    }

    private Stream OpenDocumentForRead(string uriText)
    {
        AndroidUri? uri = AndroidUri.Parse(uriText);
        Stream? stream = uri == null ? null : _activity.ContentResolver?.OpenInputStream(uri);
        return stream ?? throw new IOException($"The Android document provider did not open '{uriText}' for reading.");
    }

    private string? GetDocumentUri(string path)
    {
        lock (_stateLock)
            return _documentUris.TryGetValue(path, out string? value) ? value : null;
    }

    private Task<IReadOnlyList<string>> EnumerateChildrenAsync(string folderPath, bool folders)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        string? treeText;
        lock (_stateLock) _folderTrees.TryGetValue(folderPath, out treeText);
        if (treeText == null)
        {
            IReadOnlyList<string> local = folders
                ? Directory.Exists(folderPath) ? Directory.GetDirectories(folderPath) : []
                : Directory.Exists(folderPath) ? Directory.GetFiles(folderPath) : [];
            return Task.FromResult(local);
        }

        AndroidUri treeUri = AndroidUri.Parse(treeText)
            ?? throw new IOException($"The Android tree URI for '{folderPath}' is invalid.");
        string parentId = TryGetDocumentId(treeUri) ??
            throw new IOException($"The Android provider did not expose a document id for '{treeUri}'.");
        AndroidUri childrenUri = DocumentsContract.BuildChildDocumentsUriUsingTree(treeUri, parentId)
            ?? throw new IOException($"The Android provider did not expose children for '{treeUri}'.");
        string[] projection =
        [
            DocumentsContract.Document.ColumnDocumentId,
            DocumentsContract.Document.ColumnDisplayName,
            DocumentsContract.Document.ColumnMimeType
        ];
        var results = new List<string>();
        using ICursor? cursor = _activity.ContentResolver?.Query(childrenUri, projection, null, null, null);
        if (cursor == null) return Task.FromResult<IReadOnlyList<string>>(results);
        int idColumn = cursor.GetColumnIndex(DocumentsContract.Document.ColumnDocumentId);
        int nameColumn = cursor.GetColumnIndex(DocumentsContract.Document.ColumnDisplayName);
        int mimeColumn = cursor.GetColumnIndex(DocumentsContract.Document.ColumnMimeType);
        while (cursor.MoveToNext())
        {
            string? documentId = idColumn >= 0 ? cursor.GetString(idColumn) : null;
            string? displayName = nameColumn >= 0 ? cursor.GetString(nameColumn) : null;
            string? mimeType = mimeColumn >= 0 ? cursor.GetString(mimeColumn) : null;
            bool isFolder = string.Equals(mimeType, DocumentsContract.Document.MimeTypeDir, StringComparison.Ordinal);
            if (isFolder != folders || string.IsNullOrEmpty(documentId)) continue;
            AndroidUri? documentUri = DocumentsContract.BuildDocumentUriUsingTree(treeUri, documentId);
            if (documentUri == null) continue;
            string uriText = documentUri.ToString() ?? string.Empty;
            string childPath = ResolveProxyChildPath(folderPath, displayName, uriText, isFolder);
            lock (_stateLock)
            {
                _documentUris[childPath] = uriText;
                if (isFolder) _folderTrees[childPath] = uriText;
                else _writableDocuments[childPath] = uriText;
            }
            if (isFolder) Directory.CreateDirectory(childPath);
            results.Add(childPath);
        }
        return Task.FromResult<IReadOnlyList<string>>(results);
    }

    private Task<string> CreateDocumentAsync(string folderPath, string desiredName, string mimeType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        desiredName = AndroidStorageNamePolicy.SanitizeFileName(desiredName, "untitled");
        string? treeText;
        lock (_stateLock) _folderTrees.TryGetValue(folderPath, out treeText);
        if (treeText == null)
        {
            string localPath = Path.Combine(folderPath, desiredName);
            if (mimeType == DocumentsContract.Document.MimeTypeDir) Directory.CreateDirectory(localPath);
            else File.WriteAllBytes(localPath, []);
            return Task.FromResult(localPath);
        }

        AndroidUri treeUri = AndroidUri.Parse(treeText)
            ?? throw new IOException($"The Android tree URI for '{folderPath}' is invalid.");
        string parentId = TryGetDocumentId(treeUri) ??
            throw new IOException($"The Android provider did not expose a document id for '{treeUri}'.");
        AndroidUri parentUri = DocumentsContract.BuildDocumentUriUsingTree(treeUri, parentId)
            ?? throw new IOException($"The Android provider did not expose '{parentId}'.");
        AndroidUri createdUri = DocumentsContract.CreateDocument(
            _activity.ContentResolver!,
            parentUri,
            mimeType,
            desiredName) ?? throw new IOException($"The Android provider could not create '{desiredName}'.");
        bool isFolder = mimeType == DocumentsContract.Document.MimeTypeDir;
        string uriText = createdUri.ToString() ?? string.Empty;
        string childPath = ResolveProxyChildPath(folderPath, desiredName, uriText, isFolder);
        lock (_stateLock)
        {
            _documentUris[childPath] = uriText;
            if (isFolder) _folderTrees[childPath] = uriText;
            else _writableDocuments[childPath] = uriText;
        }
        if (isFolder) Directory.CreateDirectory(childPath);
        else File.WriteAllBytes(childPath, []);
        return Task.FromResult(childPath);
    }

    private string ResolveProxyChildPath(string folderPath, string? displayName, string uriText, bool isFolder)
    {
        lock (_stateLock)
        {
            foreach ((string existingPath, string existingUri) in _documentUris)
            {
                if (string.Equals(existingUri, uriText, StringComparison.Ordinal)) return existingPath;
            }
        }

        string name = AndroidStorageNamePolicy.SanitizeFileName(displayName, isFolder ? "Folder" : "document");
        string candidate = Path.Combine(folderPath, name);
        int suffix = 2;
        while (File.Exists(candidate) || Directory.Exists(candidate) || GetDocumentUri(candidate) != null)
        {
            string stem = Path.GetFileNameWithoutExtension(name);
            string extension = Path.GetExtension(name);
            candidate = Path.Combine(folderPath, $"{stem}-{suffix++}{extension}");
        }
        return candidate;
    }

    private static string? TryGetDocumentId(AndroidUri uri)
    {
        try
        {
            string? documentId = DocumentsContract.GetDocumentId(uri);
            if (!string.IsNullOrEmpty(documentId)) return documentId;
        }
        catch (Java.Lang.IllegalArgumentException)
        {
        }
        return DocumentsContract.GetTreeDocumentId(uri);
    }

    private static string ResolveMimeType(string fileName)
    {
        string extension = NormalizeExtension(Path.GetExtension(fileName));
        return MimeTypeMap.Singleton?.GetMimeTypeFromExtension(extension.ToLowerInvariant())
            ?? "application/octet-stream";
    }

    private void TryPersistAccess(AndroidUri uri, ActivityFlags returnedFlags, int mode)
    {
        ActivityFlags access = returnedFlags &
            (ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission);
        if (mode == OpenMode) access &= ActivityFlags.GrantReadUriPermission;
        if (access == 0) return;
        try
        {
            _activity.ContentResolver?.TakePersistableUriPermission(uri, access);
        }
        catch (Java.Lang.SecurityException)
        {
            // Some providers grant access only for the current activity. Materializing
            // open files immediately still honors that contract; save writes may later fail.
        }
    }

    private string? QueryDisplayName(AndroidUri uri)
    {
        using ICursor? cursor = _activity.ContentResolver?.Query(
            uri,
            [IOpenableColumns.DisplayName],
            null,
            null,
            null);
        if (cursor == null || !cursor.MoveToFirst()) return null;
        int column = cursor.GetColumnIndex(IOpenableColumns.DisplayName);
        return column >= 0 ? cursor.GetString(column) : null;
    }

    private string CreateUniqueLocalDirectory(string category, string? displayName)
    {
        string directory = Path.Combine(
            _sessionDirectory,
            category,
            Guid.NewGuid().ToString("N") +
                (string.IsNullOrWhiteSpace(displayName)
                    ? string.Empty
                    : "-" + AndroidStorageNamePolicy.SanitizeFileName(displayName, category)));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string[] ResolveMimeTypes(IReadOnlyList<string>? fileTypes)
    {
        if (fileTypes == null || fileTypes.Count == 0) return ["*/*"];
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in fileTypes)
        {
            string extension = NormalizeExtension(raw);
            if (extension.Length == 0 || extension == "*") return ["*/*"];
            string? mime = MimeTypeMap.Singleton?.GetMimeTypeFromExtension(extension.ToLowerInvariant());
            result.Add(string.IsNullOrWhiteSpace(mime) ? "application/octet-stream" : mime);
        }
        return result.Count == 0 ? ["*/*"] : [.. result];
    }

    private static string ResolveSuggestedFileName(
        string? suggestedFileName,
        IReadOnlyList<string>? fileTypes)
    {
        string fileName = AndroidStorageNamePolicy.SanitizeFileName(
            Path.GetFileName(suggestedFileName ?? string.Empty),
            "untitled");
        if (!Path.HasExtension(fileName) && fileTypes != null)
        {
            foreach (string type in fileTypes)
            {
                string extension = NormalizeExtension(type);
                if (extension.Length == 0 || extension == "*") continue;
                fileName += "." + extension;
                break;
            }
        }
        return fileName;
    }

    private static string NormalizeExtension(string? fileType)
    {
        string extension = fileType?.Trim() ?? string.Empty;
        if (extension is "*" or "*.*") return "*";
        if (extension.StartsWith("*.", StringComparison.Ordinal)) extension = extension[2..];
        else if (extension.StartsWith(".", StringComparison.Ordinal)) extension = extension[1..];
        return extension.Trim();
    }

    private void CompleteActivePicker(int requestCode, string? result = null, Exception? exception = null)
    {
        TaskCompletionSource<string?>? completion;
        lock (_stateLock)
        {
            if (_activeCompletion == null || _activeRequestCode != requestCode) return;
            completion = _activeCompletion;
            _activeCompletion = null;
            _activeMode = -1;
            _activeRequestCode = 0;
        }
        if (exception != null) completion.TrySetException(exception);
        else completion.TrySetResult(result);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        TaskCompletionSource<string?>? completion;
        lock (_stateLock)
        {
            completion = _activeCompletion;
            _activeCompletion = null;
            _writableDocuments.Clear();
            _documentUris.Clear();
            _folderTrees.Clear();
        }
        completion?.TrySetResult(null);
        _pickerGate.Dispose();
    }
}
