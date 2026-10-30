namespace PinkieSysMon;

internal static class WinFormsFatalExceptionPolicy
{
    public static UnhandledExceptionMode Mode => UnhandledExceptionMode.ThrowException;

    public static void Configure() => Application.SetUnhandledExceptionMode(Mode);
}
