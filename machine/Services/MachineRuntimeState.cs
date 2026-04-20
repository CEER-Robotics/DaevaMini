using System;
using System.Threading;

namespace DaevaMini.Services;

public sealed class MachineRuntimeState
{
    private static MachineRuntimeState? _instance;
    private static readonly object LockObject = new();

    private int _activeOperationCount;
    private string? _currentOperation;

    public static MachineRuntimeState Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (LockObject)
                {
                    _instance ??= new MachineRuntimeState();
                }
            }

            return _instance;
        }
    }

    private MachineRuntimeState()
    {
    }

    public bool IsBusy => Volatile.Read(ref _activeOperationCount) > 0;

    public string Status => IsBusy ? "busy" : "idle";

    public string? CurrentOperation => _currentOperation;

    public IDisposable BeginOperation(string operationType)
    {
        Interlocked.Increment(ref _activeOperationCount);
        _currentOperation = operationType;
        return new OperationScope(this);
    }

    private void EndOperation()
    {
        int remaining = Interlocked.Decrement(ref _activeOperationCount);
        if (remaining <= 0)
        {
            Interlocked.Exchange(ref _activeOperationCount, 0);
            _currentOperation = null;
        }
    }

    private sealed class OperationScope : IDisposable
    {
        private readonly MachineRuntimeState _owner;
        private bool _disposed;

        public OperationScope(MachineRuntimeState owner)
        {
            _owner = owner;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _owner.EndOperation();
        }
    }
}
