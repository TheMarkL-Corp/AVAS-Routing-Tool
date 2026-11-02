using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AvasRoutingTool.Configuration;
using AvasRoutingTool.Sdvoe;
using AvasRoutingTool.Services.Visca;
using AvasRoutingTool.Tests.Fakes;
using AvasRoutingTool.ViewModels;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public class CameraEditorViewModelTests
    {
        private readonly FakePtzCameraFactory _factory = new();

        private static SourceCardViewModel Tx(string mac, string name, int vendor = 105, bool transmitter = true) =>
            new(new AvasDevice { DeviceId = mac, DeviceName = name, IsTransmitter = transmitter, IsReceiver = !transmitter, VendorId = vendor });

        private CameraEditorViewModel Build(
            RoomCameraConfig? existing = null,
            IEnumerable<SourceCardViewModel>? sources = null,
            CameraEditorViewModel.DetectFunc? detect = null,
            IEnumerable<string>? others = null) =>
            new(existing, sources ?? Array.Empty<SourceCardViewModel>(), _factory, detect, others);

        private static CameraEditorViewModel.DetectFunc Hits(params ProbeHit[] hits) =>
            (_, _, _, _) => Task.FromResult<IReadOnlyList<ProbeHit>>(hits);

        private CameraEditorViewModel Valid(CameraEditorViewModel.DetectFunc? detect = null)
        {
            var sut = Build(detect: detect);
            sut.Name = "Room";
            sut.Host = "192.168.1.163";
            return sut;
        }

        [Fact]
        public void Host_Empty_Invalid()
        {
            var sut = Build();
            sut.Name = "Room";
            sut.Host = "   ";
            Assert.False(sut.IsValid);
            Assert.Equal("Enter the camera address", sut.SaveDisabledReason);

            sut.Host = "192.168.1.163";
            Assert.True(sut.IsValid);
            Assert.Equal(string.Empty, sut.SaveDisabledReason);
        }

        [Theory]
        [InlineData("0")]
        [InlineData("65536")]
        [InlineData("abc")]
        [InlineData("")]
        public void Port_0_And_65536_Invalid(string text)
        {
            var sut = Valid();
            sut.PortText = text;
            Assert.False(sut.IsValid);
            Assert.Equal("Port must be 1-65535", sut.SaveDisabledReason);

            sut.PortText = "65535";
            Assert.True(sut.IsValid);
            sut.PortText = "1";
            Assert.True(sut.IsValid);
        }

        [Fact]
        public void EmptyName_Invalid()
        {
            var sut = Build();
            sut.Host = "192.168.1.163";
            Assert.Equal("Enter a name", sut.SaveDisabledReason);
        }

        [Fact]
        public void TransportChange_SetsDefaultPort_OnlyIfPortUntouched()
        {
            var sut = Build();
            Assert.Equal("52381", sut.PortText);

            sut.Transport = ViscaTransportKind.Tcp;
            Assert.Equal("5678", sut.PortText);
            Assert.Equal(5678, sut.Port);
            sut.Transport = ViscaTransportKind.Udp;
            Assert.Equal("52381", sut.PortText);

            sut.PortText = "1259";
            sut.Transport = ViscaTransportKind.Tcp;
            Assert.Equal("1259", sut.PortText);
            Assert.Equal(1259, sut.Port);
        }

        [Fact]
        public void DuplicateName_Warns_NotBlocks()
        {
            var sut = Build(others: new[] { "Boom cam" });
            sut.Host = "192.168.1.163";
            sut.Name = " boom CAM ";

            Assert.NotEqual(string.Empty, sut.DuplicateNameWarning);
            Assert.True(sut.IsValid);

            sut.Name = "Ceiling";
            Assert.Equal(string.Empty, sut.DuplicateNameWarning);
        }

        [Fact]
        public void BoundTxList_IsTransmittersOnly_PreviewSupportedFirst()
        {
            var sources = new[]
            {
                Tx("00:01", "Zulu"),
                Tx("00:02", "Alpha", vendor: 7),     // transmitter, preview not supported
                Tx("00:03", "RX1", transmitter: false),
                Tx("00:04", "Bravo"),
            };
            var sut = Build(sources: sources);

            Assert.Equal(new[] { "None", "Bravo", "Zulu", "Alpha" }, sut.TxChoices.Select(t => t.Name));
            Assert.Equal(string.Empty, sut.TxChoices[0].Mac);
            Assert.Equal(new[] { false, true, true, false }, sut.TxChoices.Select(t => t.IsPreviewSupported));
            Assert.Equal("None", sut.SelectedTx.Name);
        }

        [Fact]
        public void Unbound_IsAllowed()
        {
            var sut = Valid();
            Assert.True(sut.IsValid);
            Assert.Equal(string.Empty, sut.BuildConfig().BoundTxMac);

            var bound = Build(sources: new[] { Tx("00:01", "TX1") });
            bound.Name = "Room";
            bound.Host = "h";
            bound.SelectedTx = bound.TxChoices[1];
            Assert.Equal("00:01", bound.BuildConfig().BoundTxMac);
        }

        [Fact]
        public void BuildConfig_KeepsIdAddressAndRanges_OfExisting()
        {
            var existing = new RoomCameraConfig
            {
                Id = "keep", Name = "Old", Host = "10.0.0.5", Port = 3001, Transport = ViscaTransportKind.Tcp,
                ViscaAddress = 3, ZoomMax = 1234, FocusMax = 4321, BoundTxMac = "00:09",
            };
            var sut = Build(existing);
            Assert.False(sut.IsNew);
            Assert.Equal("00:09", sut.SelectedTx.Mac);   // kept even though that TX is not discovered now
            sut.Name = "New";

            var config = sut.BuildConfig();
            Assert.Equal("keep", config.Id);
            Assert.Equal(3, config.ViscaAddress);
            Assert.Equal(1234, config.ZoomMax);
            Assert.Equal(4321, config.FocusMax);
            Assert.Equal("New", config.Name);
            Assert.Equal(3001, config.Port);
            Assert.Equal(ViscaTransportKind.Tcp, config.Transport);

            var fresh = Valid();
            Assert.True(fresh.IsNew);
            Assert.NotEqual(Valid().BuildConfig().Id, fresh.BuildConfig().Id);
        }

        [Fact]
        public async Task Detect_FillsPortAndTransport_FromFirstHit()
        {
            var sut = Valid(Hits(new ProbeHit(ViscaTransportKind.Tcp, 1259, CameraPower.On)));
            await sut.DetectAsync(CancellationToken.None);

            Assert.Equal(ViscaTransportKind.Tcp, sut.Transport);
            Assert.Equal(1259, sut.Port);
            Assert.Empty(sut.Hits);
            Assert.False(sut.IsDetecting);
        }

        [Fact]
        public async Task Detect_SeveralHits_FirstFillsAndAllAreExposed()
        {
            var first = new ProbeHit(ViscaTransportKind.Tcp, 1259, CameraPower.On);
            var second = new ProbeHit(ViscaTransportKind.Udp, 52381, CameraPower.On);
            var sut = Valid(Hits(first, second));
            await sut.DetectAsync(CancellationToken.None);

            Assert.Equal(1259, sut.Port);
            Assert.Equal(new[] { first, second }, sut.Hits);

            sut.ApplyHit(second);
            Assert.Equal(ViscaTransportKind.Udp, sut.Transport);
            Assert.Equal(52381, sut.Port);
        }

        [Fact]
        public async Task Detect_NoHit_Words()
        {
            var sut = Valid(Hits());
            sut.PortText = "1259";
            await sut.DetectAsync(CancellationToken.None);

            Assert.Equal("No camera answered on 192.168.1.163. Check the address, or enter the port by hand.", sut.DetectText);
            Assert.Equal("1259", sut.PortText);   // manual entry kept
            Assert.False(sut.IsDetecting);
        }

        [Fact]
        public async Task Detect_Cancelled_LeavesNothingRunning()
        {
            using var cts = new CancellationTokenSource();
            var started = new TaskCompletionSource();
            var sut = Valid(async (_, _, _, ct) =>
            {
                started.SetResult();
                await Task.Delay(Timeout.Infinite, ct);
                return Array.Empty<ProbeHit>();
            });

            var run = sut.DetectAsync(cts.Token);
            await started.Task;
            Assert.True(sut.IsDetecting);
            cts.Cancel();
            await run;

            Assert.False(sut.IsDetecting);
            Assert.Equal(string.Empty, sut.DetectText);
        }

        [Fact]
        public async Task TypedPortIsTriedFirst()
        {
            IReadOnlyList<(ViscaTransportKind, int)>? seen = null;
            var sut = Valid((_, c, _, _) =>
            {
                seen = c.ToList();
                return Task.FromResult<IReadOnlyList<ProbeHit>>(Array.Empty<ProbeHit>());
            });
            sut.PortText = "1259";
            await sut.DetectAsync(CancellationToken.None);

            Assert.NotNull(seen);
            Assert.Equal((ViscaTransportKind.Tcp, 1259), seen![0]);
            Assert.Equal((ViscaTransportKind.Udp, 1259), seen[1]);
            Assert.Equal(seen.Count, seen.Distinct().Count());
            Assert.Contains((ViscaTransportKind.Tcp, 5678), seen);
            Assert.Contains((ViscaTransportKind.Udp, 52381), seen);
            Assert.Equal(ViscaProbe.DefaultCandidates.Length, seen.Count);   // 1259 is already a default on both transports
        }

        [Fact]
        public async Task TestConnection_ReportsPowerState_AndDisposesCamera()
        {
            var camera = new FakePtzCamera { PollResult = new CameraStatus(CameraPower.On, 0, 0, true) };
            var factory = new FakePtzCameraFactory(camera);
            var sut = new CameraEditorViewModel(null, Array.Empty<SourceCardViewModel>(), factory) { Name = "Room", Host = "h" };

            await sut.TestConnectionAsync(CancellationToken.None);
            Assert.Equal("Connected, camera on", sut.TestText);

            camera.PollResult = new CameraStatus(CameraPower.Off, 0, 0, true);
            await sut.TestConnectionAsync(CancellationToken.None);
            Assert.Equal("Connected, camera off", sut.TestText);

            Assert.Equal(2, factory.CreateCount);
            Assert.Equal(2, camera.DisposeCount);
        }

        [Fact]
        public async Task TestConnection_DisposesTemporaryCamera_OnFailure()
        {
            var camera = new FakePtzCamera { PollResult = null };
            var factory = new FakePtzCameraFactory(camera);
            var sut = new CameraEditorViewModel(null, Array.Empty<SourceCardViewModel>(), factory)
            {
                Name = "Room", Host = "192.168.1.163", Transport = ViscaTransportKind.Udp,
            };
            sut.PortText = "1259";

            await sut.TestConnectionAsync(CancellationToken.None);

            Assert.Equal("No answer on 192.168.1.163:1259 (UDP)", sut.TestText);
            Assert.Equal(1, factory.CreateCount);
            Assert.Equal(1, camera.DisposeCount);
            Assert.False(sut.IsTesting);
        }

        [Fact]
        public void Remove_NeedsInlineConfirmation()
        {
            var sut = Build(new RoomCameraConfig { Id = "a", Name = "A", Host = "h" });
            Assert.False(sut.IsRemovePending);
            Assert.False(sut.ConfirmRemove());

            sut.RequestRemove();
            Assert.True(sut.IsRemovePending);
            sut.CancelRemove();
            Assert.False(sut.IsRemovePending);

            sut.RequestRemove();
            Assert.True(sut.ConfirmRemove());
            Assert.False(sut.IsRemovePending);

            var fresh = Build();
            fresh.RequestRemove();
            Assert.False(fresh.IsRemovePending);   // nothing to remove for a new camera
        }
    }
}
