using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using LenovoLegionToolkit.Lib.Settings;

namespace LenovoLegionToolkit.Lib.Controllers;

/// <summary>
/// Tracks GPU process changes, maintains history, and fires events for notifications.
/// </summary>
public class GPUWatcherService
{
    private readonly ApplicationSettings _settings;
    private readonly object _lock = new();
    
    private HashSet<string> _previousProcesses = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<GpuProcessHistoryEntry> _history = new();
    
    /// <summary>
    /// Maximum number of history entries to keep
    /// </summary>
    public int MaxHistorySize { get; set; } = 50;
    
    /// <summary>
    /// Fired when a new process starts using the GPU
    /// </summary>
    public event EventHandler<GpuProcessChangedEventArgs>? ProcessStarted;
    
    /// <summary>
    /// Fired when a process stops using the GPU
    /// </summary>
    public event EventHandler<GpuProcessChangedEventArgs>? ProcessStopped;
    
    /// <summary>
    /// Gets current history of GPU processes
    /// </summary>
    public IReadOnlyList<GpuProcessHistoryEntry> History
    {
        get
        {
            lock (_lock)
            {
                return _history.ToList().AsReadOnly();
            }
        }
    }

    public GPUWatcherService(ApplicationSettings settings)
    {
        _settings = settings;
    }

    /// <summary>
    /// Call this when GPU status is refreshed with new process list.
    /// Compares with previous state and fires events for changes.
    /// </summary>
    public void UpdateProcessList(IEnumerable<Process> currentProcesses)
    {
        lock (_lock)
        {
            var currentNames = currentProcesses
                .Select(p => SafeGetProcessName(p))
                .Where(n => !string.IsNullOrEmpty(n))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            
            var newProcesses = currentNames.Except(_previousProcesses, StringComparer.OrdinalIgnoreCase);
            var stoppedProcesses = _previousProcesses.Except(currentNames, StringComparer.OrdinalIgnoreCase);
            var notificationsEnabled = _settings.Store.Notifications.GpuProcesses;
            
            foreach (var processName in newProcesses)
            {
                _history.Add(new GpuProcessHistoryEntry
                {
                    ProcessName = processName,
                    StartTime = DateTime.Now,
                    EndTime = null
                });
                
                if (notificationsEnabled)
                {
                    ProcessStarted?.Invoke(this, new GpuProcessChangedEventArgs
                    {
                        ProcessName = processName,
                        Started = true
                    });
                }
            }
            
            foreach (var processName in stoppedProcesses)
            {
                var entryIndex = _history.FindLastIndex(e => 
                    e.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase) && 
                    e.IsActive);
                    
                if (entryIndex >= 0)
                {
                    var entry = _history[entryIndex];
                    _history[entryIndex] = new GpuProcessHistoryEntry
                    {
                        ProcessName = entry.ProcessName,
                        StartTime = entry.StartTime,
                        EndTime = DateTime.Now
                    };
                }
                
                if (notificationsEnabled)
                {
                    ProcessStopped?.Invoke(this, new GpuProcessChangedEventArgs
                    {
                        ProcessName = processName,
                        Started = false
                    });
                }
            }
            
            while (_history.Count > MaxHistorySize)
            {
                _history.RemoveAt(0);
            }
            
            _previousProcesses = currentNames;
        }
    }
    
    /// <summary>
    /// Clears all history
    /// </summary>
    public void ClearHistory()
    {
        lock (_lock)
        {
            _history.Clear();
        }
    }
    
    private static string SafeGetProcessName(Process process)
    {
        try
        {
            return process.ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }
}
