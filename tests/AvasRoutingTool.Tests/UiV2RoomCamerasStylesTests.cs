using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using AvasRoutingTool.Services;
using AvasRoutingTool.Views;
using Wpf.Ui.Markup;
using Xunit;
using Button = System.Windows.Controls.Button;
using Slider = System.Windows.Controls.Slider;
using TextBlock = System.Windows.Controls.TextBlock;

namespace AvasRoutingTool.Tests
{
    /// <summary>
    /// Room Cameras (RC9): the camera styles and size tokens live in <c>Resources/Styles/RoomCameras.xaml</c>. Every key resolves in
    /// both themes, every brush is a theme key, and the templates carry the pressed / checked / disabled states the views rely on.
    /// </summary>
    [Collection("WpfSta")]
    public class UiV2RoomCamerasStylesTests
    {
        private const string File = "RoomCameras.xaml";

        private static readonly string[] Keys =
        {
            "Size.Camera.Rail.Min", "Size.Camera.Rail.Max", "Size.Camera.PadKey", "Size.Camera.Chip.Min",
            "Style.Button.PtzKey", "Style.Button.PtzHome", "Style.Slider.Ptz", "Style.ToggleButton.Segment",
            "Style.Chip.Camera", "Style.Text.CameraStatus",
        };

        private static void RunSta(Action body)
        {
            Exception? failure = null;
            var t = new Thread(() =>
            {
                try
                {
                    InputModality.IsKeyboard = false;
                    TestResources.Ensure();
                    body();
                }
                catch (Exception ex) { failure = ex; }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            if (failure != null) throw new Xunit.Sdk.XunitException("Room cameras styles failure: " + failure);
        }

        private static string SourceRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !System.IO.File.Exists(Path.Combine(dir.FullName, "AvasRoutingTool.sln"))) dir = dir.Parent;
            Assert.NotNull(dir);
            return Path.Combine(dir!.FullName, "src", "AvasRoutingTool");
        }

        private static string Xaml() => System.IO.File.ReadAllText(Path.Combine(SourceRoot(), "Resources", "Styles", File));

        private static double Token(string key) => (double)Application.Current.FindResource(key);

        private static object Setter(Style style, DependencyProperty property)
        {
            for (var s = style; s != null; s = s.BasedOn)
            {
                var setter = s.Setters.OfType<Setter>().FirstOrDefault(x => x.Property == property);
                if (setter != null) return setter.Value;
            }
            throw new Xunit.Sdk.XunitException($"no setter for {property.Name}");
        }

        public static IEnumerable<object[]> AllKeys() => Keys.Select(k => new object[] { k });

        [Theory]
        [MemberData(nameof(AllKeys))]
        public void Key_Resolves_InDarkAndLight_AndLivesInRoomCamerasXaml(string key) =>
            RunSta(() =>
            {
                var app = TestResources.Ensure();
                var home = app.Resources.MergedDictionaries.LastOrDefault(d => d.Contains(key));
                Assert.True(home != null, $"{key} is in no merged dictionary");
                Assert.Equal(File, Path.GetFileName(home!.Source!.OriginalString));

                Assert.NotNull(app.FindResource(key));
                try
                {
                    new ThemeService().SetTheme(ThemeMode.Light);
                    Assert.NotNull(app.FindResource(key)); // resolved against the Light palette
                }
                finally { TestResources.Ensure(); } // back to Dark for the next test
            });

        [Fact]
        public void Merged_MatchesTheAppXamlList_InOrder()
        {
            var appXaml = XDocumentOf(Path.Combine(SourceRoot(), "App.xaml"));
            var fromApp = appXaml.Descendants()
                .Where(e => e.Name.LocalName is "ResourceDictionary" or "ThemesDictionary" or "ControlsDictionary")
                .Where(e => e.Parent?.Name.LocalName == "ResourceDictionary.MergedDictionaries")
                .Select(e => e.Name.LocalName is "ThemesDictionary" or "ControlsDictionary" ? e.Name.LocalName : (string)e.Attribute("Source")!)
                .ToArray();

            var field = typeof(TestResources).GetField("Merged", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(field);
            var merged = (string[])field!.GetValue(null)!;

            Assert.Equal(fromApp, merged);
            Assert.Contains("Resources/Styles/RoomCameras.xaml", merged);
            Assert.Equal("Resources/Styles/Composer.xaml", merged[Array.IndexOf(merged, "Resources/Styles/RoomCameras.xaml") - 1]);
        }

        private static System.Xml.Linq.XDocument XDocumentOf(string path) => System.Xml.Linq.XDocument.Load(path);

        [Fact]
        public void NoLiteralHex_InRoomCamerasXaml()
        {
            var hex = new Regex(@"#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?\b");
            Assert.DoesNotMatch(hex, Xaml());
        }

        [Fact]
        public void EveryBrush_IsADynamicThemeKey_NoStaticPaletteReference()
        {
            var xaml = Xaml();
            Assert.DoesNotMatch(new Regex(@"\{StaticResource\s+(Brush|Color)\."), xaml);
            Assert.Matches(new Regex(@"\{DynamicResource\s+Brush\.Accent\.Primary\}"), xaml);
        }

        [Fact]
        public void SizeTokens_AreTheSpecifiedValues()
        {
            RunSta(() =>
            {
                TestResources.Ensure();
                Assert.Equal(300.0, Token("Size.Camera.Rail.Min"));
                Assert.Equal(360.0, Token("Size.Camera.Rail.Max"));
                Assert.Equal(56.0, Token("Size.Camera.PadKey"));
                Assert.Equal(Token("Size.Touch.Min"), Token("Size.Camera.Chip.Min"));
                Assert.Equal(48.0, Token("Size.Camera.Chip.Min"));
            });
        }

        [Fact]
        public void PtzKey_HasPressedBlueAndDisabledTriggers_AndA56PadMinimum() =>
            RunSta(() =>
            {
                var style = (Style)Application.Current.FindResource("Style.Button.PtzKey");
                Assert.Equal(Token("Size.Camera.PadKey"), Setter(style, FrameworkElement.MinHeightProperty));
                Assert.Equal(Token("Size.Camera.PadKey"), Setter(style, FrameworkElement.MinWidthProperty));
                var template = (ControlTemplate)Setter(style, Control.TemplateProperty);
                var triggers = template.Triggers.OfType<Trigger>().ToList();
                Assert.Contains(triggers, t => t.Property == ButtonBase.IsPressedProperty && t.Value is true);
                Assert.Contains(triggers, t => t.Property == UIElement.IsEnabledProperty && t.Value is false);
            });

        [Fact]
        public void PtzHome_IsAFlatKeyWithPressedAndDisabledTriggers() =>
            RunSta(() =>
            {
                var style = (Style)Application.Current.FindResource("Style.Button.PtzHome");
                var template = (ControlTemplate)Setter(style, Control.TemplateProperty);
                var triggers = template.Triggers.OfType<Trigger>().ToList();
                Assert.Contains(triggers, t => t.Property == ButtonBase.IsPressedProperty && t.Value is true);
                Assert.Contains(triggers, t => t.Property == UIElement.IsEnabledProperty && t.Value is false);
            });

        [Fact]
        public void PtzSlider_ThumbIsTouchMinimumHigh() =>
            RunSta(() =>
            {
                var app = TestResources.Ensure();
                var slider = new Slider { Style = (Style)app.FindResource("Style.Slider.Ptz"), Minimum = 0, Maximum = 7, Value = 3 };
                slider.ApplyTemplate();
                var thumb = slider.Template.FindName("PtzThumb", slider) as FrameworkElement;
                Assert.NotNull(thumb);
                Assert.Equal(Token("Size.Touch.Min"), thumb!.MinHeight);
                Assert.Equal(Token("Size.Touch.Min"), thumb.Height);
            });

        [Fact]
        public void PtzSlider_KnobIsSmall_ButKeepsTheTouchHitArea() =>
            RunSta(() =>
            {
                var app = TestResources.Ensure();
                var slider = new Slider { Style = (Style)app.FindResource("Style.Slider.Ptz"), Minimum = 0, Maximum = 7, Value = 3 };
                slider.ApplyTemplate();
                var thumb = (Thumb)slider.Template.FindName("PtzThumb", slider);
                thumb.ApplyTemplate();
                var body = (FrameworkElement)thumb.Template.FindName("ThumbBody", thumb);
                Assert.Equal(Token("Size.Camera.Knob"), body.Width);
                Assert.Equal(Token("Size.Camera.Knob"), body.Height);
                Assert.True(body.Width < Token("Size.Touch.Min"), "the drawn knob is smaller than the 48 px hit area");
                Assert.Equal(Token("Size.Touch.Min"), thumb.Width);
            });

        [Fact]
        public void Segment_IsCheckedBlue_AndHasADisabledTrigger() =>
            RunSta(() =>
            {
                var style = (Style)Application.Current.FindResource("Style.ToggleButton.Segment");
                var template = (ControlTemplate)Setter(style, Control.TemplateProperty);
                var triggers = template.Triggers.OfType<Trigger>().ToList();
                Assert.Contains(triggers, t => t.Property == ToggleButton.IsCheckedProperty && t.Value is true);
                Assert.Contains(triggers, t => t.Property == UIElement.IsEnabledProperty && t.Value is false);
            });

        [Fact]
        public void CameraChip_IsA48PixelSelectableRadio_WithCheckedAndDisabledTriggers() =>
            RunSta(() =>
            {
                var style = (Style)Application.Current.FindResource("Style.Chip.Camera");
                Assert.Equal(Token("Size.Camera.Chip.Min"), Setter(style, FrameworkElement.MinHeightProperty));
                var template = (ControlTemplate)Setter(style, Control.TemplateProperty);
                var triggers = template.Triggers.OfType<Trigger>().ToList();
                Assert.Contains(triggers, t => t.Property == ToggleButton.IsCheckedProperty && t.Value is true);
                Assert.Contains(triggers, t => t.Property == UIElement.IsEnabledProperty && t.Value is false);
            });

        [Fact]
        public void CameraStatusText_IsAtLeast12Pixels_FromAFontToken() =>
            RunSta(() =>
            {
                var style = (Style)Application.Current.FindResource("Style.Text.CameraStatus");
                var size = Setter(style, TextBlock.FontSizeProperty);
                Assert.True((double)size >= 12.0, "camera status text must be at least 12 px");
            });
    }
}
