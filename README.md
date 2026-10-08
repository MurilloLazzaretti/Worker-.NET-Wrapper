## 🇧🇷 Worker-.NET-Wrapper 🇧🇷

Wrapper for .NET to application that will be monitored by [`Worker Control`](https://github.com/MurilloLazzaretti/Worker-Control) With this wrapper your application will start and finish by Worker Control and monitored if it crashes.

## ⚙️ Installation

Download the lastest realease of this repository and add to your project like a reference ZapMQWrapper.dll and WorkerCSharpWrapper.dll file.

## 💉 Dependency

ZapMQWrapper needs Newtonsoft.Json library installed in your project. Install it by NuGet package manager.

## ⚡️ First step

You need to start the Wrapper and provide the IP and Port of the ZapMQ service and the Handler for KeepAlive and for SafeStop.

```cs
using WorkerCSharpWrapper;

private WorkerWrapperCore worker;

public Main()
{
    worker = new WorkerWrapperCore("localhost", 5679, KeepAliveHandler, SafeStopeHandler);
}
```

## 🧬 Resources

📞 _Keep Alive_

This resource tell`s to the Worker Control that your app is alive, so just copy and paste the code bellow and its done.

```cs
using ZapMQ;

public class KeepAliveMessage
{
    public string ProcessId = "";
}

...

public object KeepAliveHandler(ZapJSONMessage message, out bool processing)
{
    KeepAliveMessage result = new KeepAliveMessage();
    result.ProcessId = Environment.ProcessId.ToString();
    processing = false;
    return result;
}
```

🔈 _IMPORTANT_

Implement the Keep Alive handler in your main thread, this will prove if your app is crashed or not.

🔐 _Safe Stop_

This handler will be raised in your app when Worker Control needs to close the app safely. Implement this as you want but dont forget to make sure that your app will be closed. After this, Worker Control will not monitore this instance any more.

```cs
using ZapMQ;

public object? SafeStopeHandler(ZapJSONMessage message, out bool processing)
{
    processing = false;
    Environment.Exit(0);    
    return null;
}
```

💎 _Trace App_

The class WorkerWrapperCore has a trace method. What you write with it can be followed live, from any machine, in the web panel of [`ZapMQ`](https://github.com/MurilloLazzaretti/ZapMQ) 2.2 (Worker Control, menu of the process, Trace). The text travels through ZapMQ, and only while somebody is watching: with nobody watching, `Trace` does nothing and costs nothing. If your app writes faster than it can be sent, the oldest lines are dropped and the panel says how many. The online trace of Management Studio 1.x keeps working as before. Keep this in mind and in your logical methods implement this trace for example:

```cs
public object KeepAliveHandler(ZapJSONMessage message, out bool processing) 
{
    worker.Trace("KEEP ALIVE RECIVED");     
    Thread.Sleep(1000);
    KeepAliveMessage result = new KeepAliveMessage();
    result.ProcessId = Environment.ProcessId.ToString();
    processing = false;
    worker.Trace("KEEP ALIVE ANSERED");
    return result;
}
```
