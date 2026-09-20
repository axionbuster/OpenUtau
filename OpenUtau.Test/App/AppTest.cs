using Xunit;
using System;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Media;
using System.Globalization;
using ReactiveUI.Avalonia;
using OpenUtau.App;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]
[assembly: AvaloniaTestIsolation(AvaloniaTestIsolationLevel.PerAssembly)]

public class TestAppBuilder {
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .UseReactiveUI(_ => { })
        // Keep screenshot rendering on the same default and fallback fonts as
        // the real application. Otherwise FontFamily.Default is resolved from
        // the headless host and UI captures don't represent the shipped app.
        .With(Program.CreateFontManagerOptions())
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

        [AvaloniaFact]
        public void HeadlessRendererUsesProductionMacFonts() {
            if (!OperatingSystem.IsMacOS()) {
                return;
            }
            Assert.True(FontManager.Current.TryGetGlyphTypeface(
                new Typeface(FontFamily.Default), out var defaultFace));
            Assert.StartsWith("Hiragino Sans", defaultFace.FamilyName);

            Assert.Equal("Helvetica Neue", MatchFamily('1'));
            Assert.Equal("Helvetica Neue", MatchFamily('♭'));
            Assert.Equal("Helvetica Neue", MatchFamily('♯'));
        }

        private static string MatchFamily(int codepoint) {
            Assert.True(FontManager.Current.TryMatchCharacter(
                codepoint, FontStyle.Normal, FontWeight.Normal, FontStretch.Normal,
                FontFamily.Default, CultureInfo.InvariantCulture, out var match));
            return match.GlyphTypeface.FamilyName;
        }
    }
}
