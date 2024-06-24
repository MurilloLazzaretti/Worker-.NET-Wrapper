// See https://aka.ms/new-console-template for more information
using SimpleSample;

var _ = new Main();

Console.WriteLine("Simple Sample Worker! => ProcessId : " + Environment.ProcessId.ToString());
Console.WriteLine("Press any key to stop...");
Console.ReadKey();
Environment.Exit(0);
