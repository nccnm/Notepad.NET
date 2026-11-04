using System.Runtime.ExceptionServices;

namespace NotepadNet.Platform.Tests;

/// <summary>Runs a test body on a fresh single-threaded apartment, as the UI thread is.</summary>
internal static class StaThread
{
    public static void Run(Action body)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception e)
            {
                failure = ExceptionDispatchInfo.Capture(e);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }
}
