using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using AvasRoutingTool.Configuration;
using AvasRoutingTool.Services.Visca;
using AvasRoutingTool.Tests.Fakes;
using AvasRoutingTool.ViewModels;
using AvasRoutingTool.Views;
using Xunit;

namespace AvasRoutingTool.Tests
{
    /// <summary>RC2: the camera editor's preview-source choice (TX live preview or the camera's RTSP stream).</summary>
    [Collection("WpfSta")]
    public class ViewSmokeTestsRoomCameraEditorRtsp
    {
        // Fake values only.
        private const string FakeUrl = "rtsp://user:pass-fake@example.test:554/live/1?token=abc";

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
            if (failure != null) throw new Xunit.Sdk.XunitException("Camera editor RTSP failure: " + failure);
        }

        private static CameraEditorDialog Open(CameraEditorViewModel vm)
        {
            var dialog = new CameraEditorDialog(vm)
            {
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10000,
                Top = 0,
            };
            dialog.Show();
            dialog.UpdateLayout();
            RoomCamerasRig.Idle();
            return dialog;
        }

        private static CameraEditorViewModel NewVm(RoomCameraConfig? existing = null) =>
            new(existing, Array.Empty<SourceCardViewModel>(), new FakePtzCameraFactory());

        private static T Find<T>(CameraEditorDialog d, string name) where T : FrameworkElement =>
            (T)d.FindName(name) ?? throw new InvalidOperationException("no element named " + name);

        private static void Pump() => RoomCamerasRig.Idle();

        private static void Choose(RadioButton r)
        {
            r.IsChecked = true;
            Pump();
        }

        private static void Type(CameraEditorDialog d, string text)
        {
            Find<Wpf.Ui.Controls.TextBox>(d, "RtspUrlBox").Text = text;
            Pump();
        }

        private static CameraEditorViewModel Filled()
        {
            var vm = NewVm();
            vm.Name = "Room";
            vm.Host = "127.0.0.1";
            return vm;
        }

        private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var c = VisualTreeHelper.GetChild(root, i);
                if (c is T t) yield return t;
                foreach (var x in Descendants<T>(c)) yield return x;
            }
        }

        [Fact]
        public void NewCamera_DefaultsToTx_UrlBoxHidden() => RunSta(() =>
        {
            var d = Open(NewVm());
            try
            {
                Assert.True(Find<RadioButton>(d, "PreviewTxChoice").IsChecked);
                Assert.False(Find<RadioButton>(d, "PreviewRtspChoice").IsChecked);
                Assert.True(Find<ComboBox>(d, "TxBox").IsVisible);
                Assert.False(Find<Wpf.Ui.Controls.TextBox>(d, "RtspUrlBox").IsVisible);
            }
            finally { d.Close(); }
        });

        [Fact]
        public void ChoosingRtsp_ShowsUrlBox_HidesTxCombo_AndBack() => RunSta(() =>
        {
            var d = Open(NewVm());
            try
            {
                Choose(Find<RadioButton>(d, "PreviewRtspChoice"));
                var box = Find<Wpf.Ui.Controls.TextBox>(d, "RtspUrlBox");
                Assert.True(box.IsVisible);
                Assert.True(box.ActualHeight >= 48, "url box " + box.ActualHeight);
                Assert.False(Find<ComboBox>(d, "TxBox").IsVisible);
                Assert.Equal("RTSP address", AutomationProperties.GetName(box));

                Choose(Find<RadioButton>(d, "PreviewTxChoice"));
                Assert.False(box.IsVisible);
                Assert.True(Find<ComboBox>(d, "TxBox").IsVisible);
            }
            finally { d.Close(); }
        });

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("http://x")]
        [InlineData("rtsp://")]
        public void Rtsp_InvalidInput_DisablesSave_WithTheParsersReason(string typed) => RunSta(() =>
        {
            var vm = Filled();
            var d = Open(vm);
            try
            {
                Choose(Find<RadioButton>(d, "PreviewRtspChoice"));
                Type(d, typed);
                Assert.True(AvasRoutingTool.Rtsp.RtspUrl.TryParse(typed, out _, out string expected) == false);

                Assert.False(Find<Button>(d, "SaveButton").IsEnabled);
                Assert.Equal(expected, Find<TextBlock>(d, "ReasonText").Text);
                var caption = Find<TextBlock>(d, "RtspReasonText");
                Assert.True(caption.IsVisible);
                Assert.Equal(expected, caption.Text);
            }
            finally { d.Close(); }
        });

        [Fact]
        public void Rtsp_ValidUrl_EnablesSave_AndSavesTheTypedUrl() => RunSta(() =>
        {
            var d = Open(Filled());
            try
            {
                Choose(Find<RadioButton>(d, "PreviewRtspChoice"));
                Type(d, "  " + FakeUrl + "  ");
                Assert.True(Find<Button>(d, "SaveButton").IsEnabled);
                Assert.False(Find<TextBlock>(d, "RtspReasonText").IsVisible);

                var click = new System.Windows.Automation.Peers.ButtonAutomationPeer(Find<Button>(d, "SaveButton"));
                ((System.Windows.Automation.Provider.IInvokeProvider)click.GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)!).Invoke();
                Pump();

                Assert.Equal(CameraEditorOutcome.Saved, d.Outcome);
                Assert.Equal(RoomCameraPreviewMode.Rtsp, d.Result!.PreviewMode);
                Assert.Equal(FakeUrl, d.Result.RtspUrl);
            }
            finally { if (d.IsVisible) d.Close(); }
        });

        [Fact]
        public void EditingASavedRtspCamera_PreFillsChoiceAndBox() => RunSta(() =>
        {
            var d = Open(NewVm(new RoomCameraConfig
            {
                Id = "cam-r", Name = "R", Host = "127.0.0.1", Port = 52381,
                PreviewMode = RoomCameraPreviewMode.Rtsp, RtspUrl = FakeUrl,
            }));
            try
            {
                Assert.True(Find<RadioButton>(d, "PreviewRtspChoice").IsChecked);
                Assert.Equal(FakeUrl, Find<Wpf.Ui.Controls.TextBox>(d, "RtspUrlBox").Text);
                Assert.True(Find<Wpf.Ui.Controls.TextBox>(d, "RtspUrlBox").IsVisible);
                Assert.False(Find<ComboBox>(d, "TxBox").IsVisible);
                Assert.True(Find<Button>(d, "SaveButton").IsEnabled);
            }
            finally { d.Close(); }
        });

        [Fact]
        public void SwitchingToTx_KeepsTheTypedUrl_ButSavesTx() => RunSta(() =>
        {
            var vm = Filled();
            var d = Open(vm);
            try
            {
                Choose(Find<RadioButton>(d, "PreviewRtspChoice"));
                Type(d, FakeUrl);
                Choose(Find<RadioButton>(d, "PreviewTxChoice"));

                Assert.Equal(FakeUrl, vm.RtspUrl);
                Assert.True(Find<Button>(d, "SaveButton").IsEnabled);
                var config = vm.BuildConfig();
                Assert.Equal(RoomCameraPreviewMode.Tx, config.PreviewMode);
                Assert.Equal(FakeUrl, config.RtspUrl);

                // Back to RTSP: the box still has the text.
                Choose(Find<RadioButton>(d, "PreviewRtspChoice"));
                Assert.Equal(FakeUrl, Find<Wpf.Ui.Controls.TextBox>(d, "RtspUrlBox").Text);
            }
            finally { d.Close(); }
        });

        [Fact]
        public void TxMode_IgnoresAnInvalidUrl_ForValidation() => RunSta(() =>
        {
            var vm = Filled();
            vm.RtspUrl = "not a url";
            Assert.True(vm.IsValid);
            vm.PreviewMode = RoomCameraPreviewMode.Rtsp;
            Assert.False(vm.IsValid);
        });

        [Fact]
        public void ExistingTxCamera_SavedUntouched_StaysTx_WithEmptyUrl() => RunSta(() =>
        {
            var d = Open(NewVm(new RoomCameraConfig { Id = "cam-t", Name = "T", Host = "127.0.0.1", Port = 52381 }));
            try
            {
                Assert.True(Find<RadioButton>(d, "PreviewTxChoice").IsChecked);
                var click = new System.Windows.Automation.Peers.ButtonAutomationPeer(Find<Button>(d, "SaveButton"));
                ((System.Windows.Automation.Provider.IInvokeProvider)click.GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)!).Invoke();
                Pump();

                Assert.Equal(CameraEditorOutcome.Saved, d.Outcome);
                Assert.Equal(RoomCameraPreviewMode.Tx, d.Result!.PreviewMode);
                Assert.Equal(string.Empty, d.Result.RtspUrl);
            }
            finally { if (d.IsVisible) d.Close(); }
        });

        [Fact]
        public void DialogTextsTooltipsAndAutomationNames_NeverContainTheCredentialsOrQuery() => RunSta(() =>
        {
            var vm = Filled();
            var d = Open(vm);
            try
            {
                Choose(Find<RadioButton>(d, "PreviewRtspChoice"));
                Type(d, FakeUrl);
                Type(d, FakeUrl + " x");   // invalid with spaces: reason text must not echo the input
                Type(d, FakeUrl);

                var box = Find<Wpf.Ui.Controls.TextBox>(d, "RtspUrlBox");
                var texts = new List<string> { d.Title };
                foreach (var el in Descendants<FrameworkElement>(d).Append(d))
                {
                    if (ReferenceEquals(el, box) || el.TemplatedParent == box || IsInside(el, box)) continue;
                    if (el is TextBlock tb) texts.Add(tb.Text);
                    if (el is ContentControl cc && cc.Content is string s) texts.Add(s);
                    texts.Add(AutomationProperties.GetName(el));
                    texts.Add(AutomationProperties.GetHelpText(el));
                    texts.Add(el.ToolTip?.ToString() ?? string.Empty);
                }
                texts.Add(AutomationProperties.GetName(box));
                texts.Add(box.ToolTip?.ToString() ?? string.Empty);
                texts.Add(vm.SaveDisabledReason);
                texts.Add(vm.TestText);
                texts.Add(vm.DetectText);

                foreach (var t in texts.Where(t => !string.IsNullOrEmpty(t)))
                {
                    Assert.DoesNotContain("pass-fake", t);
                    Assert.DoesNotContain("abc", t);
                }

                // And the reason for an invalid URL that contains them.
                Type(d, "http://user:pass-fake@example.test/x?token=abc");
                Assert.DoesNotContain("pass-fake", Find<TextBlock>(d, "ReasonText").Text);
                Assert.DoesNotContain("abc", Find<TextBlock>(d, "RtspReasonText").Text);
            }
            finally { d.Close(); }
        });

        private static bool IsInside(DependencyObject el, DependencyObject ancestor)
        {
            for (DependencyObject? p = el; p != null; p = p is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(p) : null)
                if (ReferenceEquals(p, ancestor)) return true;
            return false;
        }
    }
}
