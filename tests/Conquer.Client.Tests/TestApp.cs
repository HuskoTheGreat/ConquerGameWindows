using Avalonia;
using Avalonia.Headless;
using Conquer.Client;
using Conquer.Client.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Conquer.Client.Tests
{
    public static class TestAppBuilder
    {
        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }
}
