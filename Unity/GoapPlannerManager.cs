﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using UnityEngine;


// every thread runs on one of these classes
public class GoapPlannerThread
{
    private volatile ReGoapPlanner planner;
    private volatile Queue<PlanWork> worksQueue;
    private bool isRunning;
    private readonly Action<GoapPlannerThread, PlanWork, IReGoapGoal> onDonePlan;

    public GoapPlannerThread(Queue<PlanWork> worksQueue, Action<GoapPlannerThread, PlanWork, IReGoapGoal> onDonePlan)
    {
        planner = new ReGoapPlanner();
        this.worksQueue = worksQueue;
        isRunning = true;
        this.onDonePlan = onDonePlan;
    }

    public void Stop()
    {
        isRunning = false;
    }

    public void MainLoop()
    {
        while (isRunning)
        {
            PlanWork? checkWork = null;
            lock (worksQueue)
            {
                if (worksQueue.Count > 0)
                {
                    checkWork = worksQueue.Dequeue();
                }
            }
            if (checkWork != null)
            {
                var work = checkWork.Value;
                planner.Plan(work.agent, work.goal, work.actions,
                    (newGoal) => onDonePlan(this, work, newGoal));
            }
        }
    }
}

// behaviour that should be added once (and only once) to a gameobject in your unity's scene
public class GoapPlannerManager : MonoBehaviour
{
    public static GoapPlannerManager instance;

    public readonly int threadsCount = 4;
    private GoapPlannerThread[] planners;

    private volatile Queue<PlanWork> worksQueue;
    private volatile List<PlanWork> doneWorks;
    private Thread[] threads;

    public bool workInFixedUpdate = true;

    void Awake()
    {
        if (instance != null)
        {
            Destroy(this);
            throw new UnityException("A scene can have only one GoapPlannerManager");
        }
        instance = this;

        doneWorks = new List<PlanWork>();
        worksQueue = new Queue<PlanWork>();
        planners = new GoapPlannerThread[threadsCount];
        threads = new Thread[threadsCount];
        for (int i = 0; i < threadsCount; i++)
        {
            planners[i] = new GoapPlannerThread(worksQueue, OnDonePlan);
            var thread = new Thread(planners[i].MainLoop) {IsBackground = true};
            thread.Start();
            threads[i] = thread;
        }
    }

    void OnDestroy()
    {
        foreach (var planner in planners)
        {
            planner.Stop();
        }
        // should wait here?
        foreach (var thread in threads)
        {
            thread.Abort();
        }
    }

    void Update()
    {
        if (workInFixedUpdate) return;
        Tick();
    }

    void FixedUpdate()
    {
        if (!workInFixedUpdate) return;
        Tick();
    }

    // check all threads for done work
    private void Tick()
    {
        lock (doneWorks)
        {
            if (doneWorks.Count > 0)
            {
                var doneWorksCopy = doneWorks.ToArray();
                doneWorks.Clear();
                foreach (var work in doneWorksCopy)
                {
                    work.callback(work.newGoal);
                }
            }
        }
    }

    // called in another thread
    private void OnDonePlan(GoapPlannerThread plannerThread, PlanWork work, IReGoapGoal newGoal)
    {
        work.newGoal = newGoal;
        lock (doneWorks)
        {
            doneWorks.Add(work);
        }
    }

    public PlanWork Plan(IReGoapAgent agent, IReGoapGoal goal, Queue<IReGoapAction> actions, Action<IReGoapGoal> callback)
    {
        var work = new PlanWork(agent, goal, actions, callback);
        lock (worksQueue)
            worksQueue.Enqueue(work);
        return work;
    }
}

public struct PlanWork
{
    public readonly IReGoapAgent agent;
    public readonly IReGoapGoal goal;
    public readonly Queue<IReGoapAction> actions;
    public readonly Action<IReGoapGoal> callback;

    public IReGoapGoal newGoal;

    public PlanWork(IReGoapAgent agent, IReGoapGoal goal, Queue<IReGoapAction> actions, Action<IReGoapGoal> callback) : this()
    {
        this.agent = agent;
        this.goal = goal;
        this.actions = actions;
        this.callback = callback;
    }
}