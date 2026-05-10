using System;
using System.IO;

namespace Wander.WindowsAPI
{
    public class FolderWatcher : IDisposable
    {
        private readonly FileSystemWatcher _watcher;

        public event EventHandler<FileSystemEventArgs>? FileCreated;
        public event EventHandler<FileSystemEventArgs>? FileChanged;
        public event EventHandler<FileSystemEventArgs>? FileDeleted;
        public event EventHandler<RenamedEventArgs>? FileRenamed;

        public FolderWatcher(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            _watcher = new FileSystemWatcher(path)
            {
                NotifyFilter = NotifyFilters.FileName
                             | NotifyFilters.DirectoryName
                             | NotifyFilters.LastWrite
                             | NotifyFilters.Size
                             | NotifyFilters.CreationTime,
                IncludeSubdirectories = true
            };

            _watcher.Created += (s, e) => FileCreated?.Invoke(this, e);
            _watcher.Changed += (s, e) => FileChanged?.Invoke(this, e);
            _watcher.Deleted += (s, e) => FileDeleted?.Invoke(this, e);
            _watcher.Renamed += (s, e) => FileRenamed?.Invoke(this, e);
        }

        public void Start()
        {
            _watcher.EnableRaisingEvents = true;
        }

        public void Stop()
        {
            _watcher.EnableRaisingEvents = false;
        }

        public void Dispose()
        {
            _watcher.Dispose();
        }
    }
}
