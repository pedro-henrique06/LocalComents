using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using LocalComents.Models;
using Newtonsoft.Json;

namespace LocalComents.Services
{
    public sealed class CommentsChangedEventArgs : EventArgs
    {
        public CommentsChangedEventArgs(string? filePath) => FilePath = filePath;

        /// <summary>The affected file, or <c>null</c> when every file may have changed.</summary>
        public string? FilePath { get; }
    }

    /// <summary>
    /// Owns the in-memory set of comments and keeps it in sync with the JSON file on disk.
    /// A single instance is shared by the package (commands, tool window) and by the MEF
    /// editor components (taggers, quick info), so it is deliberately not MEF-composed.
    /// </summary>
    public sealed class CommentStore
    {
        public static CommentStore Instance { get; } = new CommentStore();

        private readonly object _gate = new object();
        private readonly JsonSerializerSettings _jsonSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
        };

        private Dictionary<string, List<LocalComment>> _byFile =
            new Dictionary<string, List<LocalComment>>(StringComparer.OrdinalIgnoreCase);

        private string? _storagePath;
        private FileSystemWatcher? _watcher;
        private Timer? _reloadDebounce;
        private bool _writing;

        private CommentStore()
        {
        }

        public event EventHandler<CommentsChangedEventArgs>? CommentsChanged;

        public string? StoragePath
        {
            get { lock (_gate) { return _storagePath; } }
        }

        /// <summary>Points the store at a storage file and loads it. Safe to call repeatedly.</summary>
        public void UseStorageFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            lock (_gate)
            {
                if (string.Equals(_storagePath, path, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                _storagePath = path;
            }

            Reload();
            StartWatching();
        }

        public IReadOnlyList<LocalComment> GetComments(string? filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                return Array.Empty<LocalComment>();
            }

            var key = Normalize(filePath!);
            lock (_gate)
            {
                return _byFile.TryGetValue(key, out var list)
                    ? list.ToArray()
                    : Array.Empty<LocalComment>();
            }
        }

        public IReadOnlyList<KeyValuePair<string, IReadOnlyList<LocalComment>>> GetAll()
        {
            lock (_gate)
            {
                return _byFile
                    .Select(pair => new KeyValuePair<string, IReadOnlyList<LocalComment>>(pair.Key, pair.Value.ToArray()))
                    .ToArray();
            }
        }

        public bool HasComments(string? filePath) => GetComments(filePath).Count > 0;

        public void Add(string filePath, LocalComment comment)
        {
            var key = Normalize(filePath);
            lock (_gate)
            {
                if (!_byFile.TryGetValue(key, out var list))
                {
                    list = new List<LocalComment>();
                    _byFile[key] = list;
                }

                list.Add(comment);
            }

            SaveAndNotify(key);
        }

        public void Update(string filePath, string commentId, string newText, string? newColor)
        {
            var key = Normalize(filePath);
            lock (_gate)
            {
                if (!_byFile.TryGetValue(key, out var list))
                {
                    return;
                }

                var existing = list.FirstOrDefault(c => c.Id == commentId);
                if (existing == null)
                {
                    return;
                }

                existing.Text = newText;
                existing.Color = newColor;
                existing.Timestamp = LocalComment.NowTimestamp();
            }

            SaveAndNotify(key);
        }

        public void Remove(string filePath, string commentId)
        {
            var key = Normalize(filePath);
            lock (_gate)
            {
                if (!_byFile.TryGetValue(key, out var list))
                {
                    return;
                }

                list.RemoveAll(c => c.Id == commentId);
                if (list.Count == 0)
                {
                    _byFile.Remove(key);
                }
            }

            SaveAndNotify(key);
        }

        public void RemoveFile(string filePath)
        {
            var key = Normalize(filePath);
            lock (_gate)
            {
                if (!_byFile.Remove(key))
                {
                    return;
                }
            }

            SaveAndNotify(key);
        }

        public void Reload()
        {
            string? path = StoragePath;
            var loaded = new Dictionary<string, List<LocalComment>>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                try
                {
                    var json = ReadAllTextWithRetry(path!);
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        var raw = JsonConvert.DeserializeObject<Dictionary<string, List<LocalComment>>>(json);
                        if (raw != null)
                        {
                            foreach (var pair in raw)
                            {
                                if (pair.Value == null || pair.Value.Count == 0)
                                {
                                    continue;
                                }

                                var key = Normalize(pair.Key);
                                if (loaded.TryGetValue(key, out var existing))
                                {
                                    existing.AddRange(pair.Value);
                                }
                                else
                                {
                                    loaded[key] = new List<LocalComment>(pair.Value);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LocalComentsLog.Write($"Failed to read '{path}': {ex.Message}");
                    return;
                }
            }

            lock (_gate)
            {
                _byFile = loaded;
            }

            RaiseChanged(null);
        }

        public void Save()
        {
            string? path = StoragePath;
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            string json;
            lock (_gate)
            {
                json = JsonConvert.SerializeObject(_byFile, _jsonSettings);
            }

            try
            {
                var directory = Path.GetDirectoryName(path!);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory!);
                }

                _writing = true;
                File.WriteAllText(path!, json);
            }
            catch (Exception ex)
            {
                LocalComentsLog.Write($"Failed to write '{path}': {ex.Message}");
            }
            finally
            {
                _writing = false;
            }
        }

        private void SaveAndNotify(string filePath)
        {
            Save();
            RaiseChanged(filePath);
        }

        private void RaiseChanged(string? filePath)
        {
            CommentsChanged?.Invoke(this, new CommentsChangedEventArgs(filePath));
        }

        private void StartWatching()
        {
            StopWatching();

            var path = StoragePath;
            var directory = string.IsNullOrEmpty(path) ? null : Path.GetDirectoryName(path!);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                return;
            }

            try
            {
                var watcher = new FileSystemWatcher(directory!, Path.GetFileName(path!))
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                    EnableRaisingEvents = true,
                };

                watcher.Changed += OnStorageFileTouched;
                watcher.Created += OnStorageFileTouched;
                watcher.Deleted += OnStorageFileTouched;
                watcher.Renamed += OnStorageFileTouched;
                _watcher = watcher;
            }
            catch (Exception ex)
            {
                LocalComentsLog.Write($"Failed to watch '{directory}': {ex.Message}");
            }
        }

        private void StopWatching()
        {
            _watcher?.Dispose();
            _watcher = null;
            _reloadDebounce?.Dispose();
            _reloadDebounce = null;
        }

        private void OnStorageFileTouched(object sender, FileSystemEventArgs e)
        {
            if (_writing)
            {
                return;
            }

            // Editors write in bursts; collapse them into a single reload.
            _reloadDebounce?.Dispose();
            _reloadDebounce = new Timer(_ => Reload(), null, 300, Timeout.Infinite);
        }

        private static string ReadAllTextWithRetry(string path)
        {
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var reader = new StreamReader(stream))
                    {
                        return reader.ReadToEnd();
                    }
                }
                catch (IOException) when (attempt < 3)
                {
                    Thread.Sleep(100);
                }
            }
        }

        private static string Normalize(string filePath)
        {
            try
            {
                return Path.GetFullPath(filePath);
            }
            catch
            {
                return filePath;
            }
        }
    }
}
