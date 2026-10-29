using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text;

namespace WinRAlias;

/// <summary>
/// 셸 아이콘 비동기 로더.
///  - 셸 추출이 개당 수십~수백 ms라서 STA 작업 스레드 여러 개로 병렬 처리
///  - 화면에 보이는 항목은 urgent로 큐 앞에 넣음
///  - 한 번 뽑은 아이콘은 디스크(%LOCALAPPDATA%\WinRAlias\icons)에 캐시 → 다음 실행부터 즉시 표시
/// 생성한 스레드(UI)의 SynchronizationContext로 결과를 돌려준다.
/// </summary>
internal sealed class IconLoader
{
    private readonly int _size;
    private readonly SynchronizationContext _ui;
    private readonly Action<IReadOnlyList<(string Key, Bitmap Icon)>> _onLoaded;

    private readonly object _lock = new();
    private readonly LinkedList<string> _queue = new();
    private readonly Dictionary<string, LinkedListNode<string>> _queued = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(string, Bitmap)> _pending = new();
    private readonly SemaphoreSlim _signal = new(0);
    private bool _flushScheduled;

    private static string CacheDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinRAlias", "icons");

    public IconLoader(int size, Action<IReadOnlyList<(string Key, Bitmap Icon)>> onLoaded, int workers)
    {
        _size = size;
        _onLoaded = onLoaded;
        _ui = SynchronizationContext.Current ?? throw new InvalidOperationException("UI 스레드에서 만들어야 해요.");

        for (int i = 0; i < workers; i++)
        {
            var t = new Thread(Work) { IsBackground = true, Name = "IconLoader" };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
        }
    }

    /// <summary>이미 요청한 키는 무시. urgent면 (이미 대기 중인 것도) 큐 맨 앞으로</summary>
    public void Request(IEnumerable<string> keys, bool urgent = false)
    {
        var list = keys.Where(k => !string.IsNullOrEmpty(k)).ToList();
        if (urgent) list.Reverse(); // 앞에서부터 AddFirst 하면 순서가 뒤집히므로

        lock (_lock)
        {
            foreach (var key in list)
            {
                if (_seen.Add(key))
                {
                    _queued[key] = urgent ? _queue.AddFirst(key) : _queue.AddLast(key);
                    _signal.Release();
                }
                else if (urgent && _queued.TryGetValue(key, out var node))
                {
                    _queue.Remove(node);
                    _queue.AddFirst(node);
                }
            }
        }
    }

    private void Work()
    {
        while (true)
        {
            _signal.Wait();
            string key;
            lock (_lock)
            {
                if (_queue.First == null) continue;
                key = _queue.First.Value;
                _queue.RemoveFirst();
                _queued.Remove(key);
            }

            var file = CacheFile(key);
            var icon = LoadCached(file);
            if (icon == null)
            {
                icon = NativeShell.GetIcon(key, _size);
                if (icon != null) SaveCached(file, icon);
            }
            if (icon != null) Post(key, icon);
        }
    }

    /// <summary>결과를 모아서 UI 스레드로 한 번에 전달</summary>
    private void Post(string key, Bitmap icon)
    {
        lock (_lock)
        {
            _pending.Add((key, icon));
            if (_flushScheduled) return;
            _flushScheduled = true;
        }

        _ui.Post(_ =>
        {
            List<(string, Bitmap)> batch;
            lock (_lock)
            {
                batch = new List<(string, Bitmap)>(_pending);
                _pending.Clear();
                _flushScheduled = false;
            }
            _onLoaded(batch);
        }, null);
    }

    private string CacheFile(string key)
    {
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(key.ToLowerInvariant())));
        return Path.Combine(CacheDir, $"{hash}_{_size}.png");
    }

    private static Bitmap? LoadCached(string file)
    {
        try
        {
            if (!File.Exists(file)) return null;
            using var fs = File.OpenRead(file);
            using var img = Image.FromStream(fs);
            return new Bitmap(img); // 스트림/파일 잠금과 분리
        }
        catch { return null; }
    }

    private static void SaveCached(string file, Bitmap icon)
    {
        try
        {
            Directory.CreateDirectory(CacheDir);
            icon.Save(file, ImageFormat.Png);
        }
        catch { /* 캐시는 없어도 그만 */ }
    }
}
