using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace MyNamespace;

public static class Program
{
    public static async Task Main()
    {
        const int PhilosophersCount = 5;
        var cts = new CancellationTokenSource();

        PhilosopherFactory philosopherFactory = new();
        var philosophers = philosopherFactory.Create(PhilosophersCount);

        
        List<PhilosopherEngine> philosophersEngines = new List<PhilosopherEngine>();

        for (var i = 0; i < PhilosophersCount; i++)
        {
            int nx = (i+1) % PhilosophersCount;
            int pr = (i + PhilosophersCount - 1) % PhilosophersCount;
            philosophersEngines.Add(new PhilosopherEngine(philosophers[i], philosophers[pr], philosophers[nx], cts.Token));
        }

        foreach (var philosopherEngine in philosophersEngines)
        {
            philosopherEngine.StartFighting();
        }

        await Task.Delay(10000);
        cts.Cancel();
        
        await Task.Delay(500);
        
        foreach (var philosopherEngine in philosophersEngines)
        {
            var exception = philosopherEngine.CheckExceptions();
            if (exception is not null)
            {
                Console.WriteLine(exception.ToString());
            }
        }
        
        foreach (var (philosopher, i) in philosophers.Select((philosopher, i) => (philosopher, i)))
        {
            Console.WriteLine($"{i}. {philosopher.MealEatenCounter}");
        }
    }
}

public class PhilosopherFactory
{
    public List<Philosopher> Create(int count)
    {
        if (count < 2)
        {
            throw new ArgumentException("Not enough Philosophers (count can not be less than 2)", nameof(count));
        }

        Fork lastFork = new Fork();
        Fork firstFork = lastFork;
        
        var philosophers = Enumerable.Range(1, count)
            .Select(index =>
            {
                var newFork = index != count? new Fork() : firstFork;

                var newPhilosopher = new Philosopher(lastFork, newFork, index);
                lastFork = newFork;
                return newPhilosopher;
            }).ToList();

        return philosophers;
    }
}

public enum PhilosopherState
{
    Contemplating = 0,
    Eating = 1,
}

public class PhilosopherEngine
{
    private readonly Philosopher _philosopher;
    //private readonly int _philosopherNumber;
    private Philosopher _leftPhilosopher;
    private Philosopher _rightPhilosopher;
    private readonly TimeSpan _eatingTime = TimeSpan.FromSeconds(1);
    private readonly CancellationToken ct;

    private readonly object _lockInstance = new object();

    private Task? _thisTask;

    public AggregateException? CheckExceptions()
    {
        return _thisTask?.Exception;
    }
    public PhilosopherEngine(Philosopher philosopher, Philosopher rightPhilosopher, Philosopher leftPhilosopher, CancellationToken ct)
    {
        _philosopher = philosopher;
        _rightPhilosopher = rightPhilosopher;
        _leftPhilosopher = leftPhilosopher;
        this.ct = ct;
    }

    public void StartFighting()
    {
        _thisTask = Task.Run(() => Processing(ct));
    }

    private async Task Processing(CancellationToken ct)
    {
        while (true)
        {
            if (ct.IsCancellationRequested) break;
            
            if (_philosopher.State == PhilosopherState.Contemplating)
            {
                bool lockWasTaken = false;

                if (!_philosopher.LeftFork.IsForkTaken)
                {
                    try
                    {
                        if (Monitor.TryEnter(_philosopher.LeftFork.LockInstance, 20))
                        {
                            if (!_philosopher.LeftFork.IsForkTaken)
                            {
                                _philosopher.LeftFork.TakeFork(_philosopher);
                                Console.WriteLine($"{_philosopher.PhilosopherId} Took left fork");
                            }
                            lockWasTaken = true;
                        }
                    }
                    finally
                    {
                        if (lockWasTaken)
                        {
                            Monitor.Exit(_philosopher.LeftFork.LockInstance);
                        }
                    }
                }
                
                if (!_philosopher.LeftFork.IsMine(_philosopher))
                {
                    Console.WriteLine($"{_philosopher.PhilosopherId} didn't manage to take left fork");
                    continue;
                }
                
                lockWasTaken = false;

                if (!_philosopher.RightFork.IsForkTaken)
                {
                    try
                    {
                        if (Monitor.TryEnter(_philosopher.RightFork.LockInstance, 20))
                        {
                            lockWasTaken = true;
                            if (!_philosopher.RightFork.IsForkTaken)
                            {
                                Console.WriteLine($"{_philosopher.PhilosopherId} Took right fork");
                                _philosopher.RightFork.TakeFork(_philosopher);
                            }
                        }
                    }
                    finally
                    {
                        if (lockWasTaken)
                        {
                            Monitor.Exit(_philosopher.RightFork.LockInstance);
                        }
                    }
                }
                else
                {
                    lock (_philosopher.LeftFork.LockInstance)
                    {
                        _philosopher.LeftFork.PutFork(_philosopher);
                        Console.WriteLine($"{_philosopher.PhilosopherId} Put left fork");
                    }
                }
                
                if (_philosopher.LeftFork.Owner == _philosopher && _philosopher.RightFork.Owner == _philosopher)
                {
                    _philosopher.StartEating();
                    await Task.Delay((int)_eatingTime.TotalMilliseconds);
                    _philosopher.RightFork.PutFork(_philosopher);
                    _philosopher.LeftFork.PutFork(_philosopher);
                    _philosopher.StartContemplating();
                }
            }
        }
    }
}


public class Philosopher
{
    public int MealEatenCounter = 0;
    public Fork LeftFork { get; private set; }
    public Fork RightFork { get; private set; }

    public int PhilosopherId { get; set; }

    public PhilosopherState State { get; private set; }
    
    public Philosopher(Fork leftFork, Fork rightFork, int philosopherId)
    {
        LeftFork = leftFork;
        RightFork = rightFork;
        PhilosopherId = philosopherId;
    }

    public void StartEating()
    {
        if (State == PhilosopherState.Eating) throw new Exception("You are already eating");
        State = PhilosopherState.Eating;
    }

    public void StartContemplating()
    {
        if (State == PhilosopherState.Contemplating) throw new Exception("You are already contemplating");

        State = PhilosopherState.Contemplating;
        MealEatenCounter++;
    }

    public void ChangeState()
    {
        
    }
}

public class Fork
{
    public bool IsForkTaken => Owner is not null;
    public object LockInstance = new object();
    public Philosopher? Owner { get; set; } = null;

    public void TakeFork(Philosopher pretender)
    {
        if (IsForkTaken) throw new Exception("Fork is busy");
        Owner = pretender;
    }

    public bool IsMine(Philosopher philosopher) => philosopher == Owner;
    public void PutFork(Philosopher pretender)
    {
        if (Owner != pretender) throw new Exception("It's not yours!");
        Owner = null;
    }
}