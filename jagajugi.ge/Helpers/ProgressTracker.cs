using System.Collections.Concurrent;

namespace Muzzon.ge.Helpers
{
    public static class ProgressTracker
    {
        private static readonly ConcurrentDictionary<string, string> _progressDict = new();

        public static void SetProgress(string id, string progress) =>
            _progressDict[id] = progress;

        public static string? GetProgress(string id) =>
            _progressDict.TryGetValue(id, out var progress) ? progress : null;

        public static void RemoveProgress(string id) =>
            _progressDict.TryRemove(id, out _);
    }
}
