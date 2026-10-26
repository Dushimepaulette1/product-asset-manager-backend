using System.Collections;
using System.Diagnostics;

namespace ProductAssetManager.Api.Tests.ApiTests;

public class ExportedActivities : ICollection<Activity>
{
    private readonly List<Activity> _activities = new();
    private readonly object _lock = new();

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _activities.Count;
            }
        }
    }

    public bool IsReadOnly => false;

    public void Add(Activity item)
    {
        lock (_lock)
        {
            _activities.Add(item);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _activities.Clear();
        }
    }

    public bool Contains(Activity item)
    {
        lock (_lock)
        {
            return _activities.Contains(item);
        }
    }

    public void CopyTo(Activity[] array, int arrayIndex)
    {
        lock (_lock)
        {
            _activities.CopyTo(array, arrayIndex);
        }
    }

    public bool Remove(Activity item)
    {
        lock (_lock)
        {
            return _activities.Remove(item);
        }
    }

    public IReadOnlyList<Activity> Snapshot()
    {
        lock (_lock)
        {
            return _activities.ToList();
        }
    }

    public IEnumerator<Activity> GetEnumerator() => Snapshot().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
