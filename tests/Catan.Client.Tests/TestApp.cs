using Avalonia;
using Avalonia.Headless;
using Catan.Client;
using Catan.Client.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Catan.Client.Tests
{
    public static class TestAppBuilder
    {
        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }
}
