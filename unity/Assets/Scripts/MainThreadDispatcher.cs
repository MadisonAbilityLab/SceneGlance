using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

public class MainThreadDispatcher : MonoBehaviour
{
    private static MainThreadDispatcher _instance;

    // a queue of tuples of two actions
    // each tuple: <main thread action, exception handler>
    private readonly Queue<Tuple<Action, Action<Exception>>> _actions = new Queue<Tuple<Action, Action<Exception>>>();
    private List<Tuple<Action, Action<Exception>>> _executing = new List<Tuple<Action, Action<Exception>>>();


    public static MainThreadDispatcher Instance {
        get {
            if (_instance == null) {
                throw new Exception("MainThreadDispatcher is not initialized.");
            }
            return _instance;
        }
    }

    void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else 
        {
            Destroy(gameObject);
        }
    }
    void Update()
    {
        lock (_actions)
        {
            while (_actions.Count > 0)
                _executing.Add(_actions.Dequeue());
        }
        for (int i = 0; i < _executing.Count; i++)
        {
            try
            {
                _executing[i].Item1.Invoke();
            }
            catch (Exception e)
            {
                if (_executing[i].Item2 != null)
                {
                    _executing[i].Item2.Invoke(e);
                }
            }
        }
        _executing.Clear();
    }

    public void Enqueue(Action mainAction, Action<Exception> exceptionHandler = null)
    {
        lock (_actions)
        {
            _actions.Enqueue(new Tuple<Action, Action<Exception>>(mainAction, exceptionHandler));
        }
    }

    void OnApplicationQuit()
    {
        _instance = null;
    }
}
