using Xunit;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using ReactiveUI.Avalonia;
using OpenUtau.App;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]
[assembly: AvaloniaTestIsolation(AvaloniaTestIsolationLevel.PerAssembly)]

public class TestAppBuilder {
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .UseReactiveUI(_ => { })
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

namespace OpenUtau.App {
    public class AppTest {
        [Fact]
        public void BuildTest() {
            Assert.False(typeof(App).IsAbstract);
            Assert.False(typeof(Program).IsAbstract);
        }

        [AvaloniaFact]
        public void StringsTest() {
            var app = Application.Current as App;
            Assert.NotNull(app);

            var languages = App.GetLanguages();
            Assert.True(languages.Count > 1);
            Assert.Contains("en-US", languages.Keys);
            Assert.Contains("zh-CN", languages.Keys);
            Assert.Contains("ja-JP", languages.Keys);
            foreach (var pair in languages) {
                Assert.NotNull(pair.Value);
            }
        }
    }
}
