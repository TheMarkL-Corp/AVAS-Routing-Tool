using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AvasRoutingTool.Configuration;
using AvasRoutingTool.Services.Visca;

namespace AvasRoutingTool.ViewModels
{
    /// <summary>One entry of the "bound TX" list. The first entry is None (empty Mac).</summary>
    public sealed record TxChoice(string Name, string Mac, bool IsPreviewSupported);

    /// <summary>
    /// Edit state for one room camera: name, address, port, transport and bound TX, with Detect
    /// (the app's own port auto-detect for a camera on the operator's local network) and Test connection.
    /// </summary>
    public sealed class CameraEditorViewModel : INotifyPropertyChanged
    {
        public const int DefaultUdpPort = 52381;
        public const int DefaultTcpPort = 5678;

        /// <summary>The detect call: host, candidate (transport, port) pairs, per-probe timeout, cancellation.</summary>
        public delegate Task<IReadOnlyList<ProbeHit>> DetectFunc(
            string host,
            IEnumerable<(ViscaTransportKind, int)> candidates,
            TimeSpan perProbeTimeout,
            CancellationToken ct);

        private static readonly TimeSpan PerProbeTimeout = TimeSpan.FromMilliseconds(1500);

        private readonly RoomCameraConfig? _existing;
        private readonly IPtzCameraFactory _factory;
        private readonly DetectFunc _detect;
        private readonly HashSet<string> _otherNames;
        private string _name;
        private string _host;
        private string _portText;
        private ViscaTransportKind _transport;
        private bool _portTouched;
        private TxChoice _selectedTx;
        private bool _isDetecting;
        private string _detectText = string.Empty;
        private IReadOnlyList<ProbeHit> _hits = Array.Empty<ProbeHit>();
        private bool _isTesting;
        private string _testText = string.Empty;
        private bool _isRemovePending;

        public CameraEditorViewModel(
            RoomCameraConfig? existing,
            IEnumerable<SourceCardViewModel> sources,
            IPtzCameraFactory factory,
            DetectFunc? detect = null,
            IEnumerable<string>? otherCameraNames = null)
        {
            _existing = existing;
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _detect = detect ?? ((h, c, t, ct) => ViscaProbe.DetectAsync(h, c, t, ct));
            _otherNames = new HashSet<string>(
                (otherCameraNames ?? Enumerable.Empty<string>()).Select(n => (n ?? string.Empty).Trim()),
                StringComparer.OrdinalIgnoreCase);

            _name = existing?.Name ?? string.Empty;
            _host = existing?.Host ?? string.Empty;
            _transport = existing?.Transport ?? ViscaTransportKind.Udp;
            _portText = (existing?.Port ?? DefaultPortFor(_transport)).ToString(CultureInfo.InvariantCulture);
            // An existing camera on a non-default port keeps it when the transport is flipped.
            _portTouched = existing != null && existing.Port != DefaultPortFor(_transport);

            string boundMac = existing?.BoundTxMac ?? string.Empty;
            TxChoices = BuildTxChoices(sources ?? Enumerable.Empty<SourceCardViewModel>(), boundMac);
            _selectedTx = TxChoices.FirstOrDefault(t => t.Mac == boundMac) ?? TxChoices[0];
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public bool IsNew => _existing == null;

        public string Name
        {
            get => _name;
            set
            {
                if (_name == value) return;
                _name = value ?? string.Empty;
                OnPropertyChanged();
                RaiseValidity();
                OnPropertyChanged(nameof(DuplicateNameWarning));
            }
        }

        public string Host
        {
            get => _host;
            set
            {
                if (_host == value) return;
                _host = value ?? string.Empty;
                OnPropertyChanged();
                RaiseValidity();
            }
        }

        public string PortText
        {
            get => _portText;
            set
            {
                if (_portText == value) return;
                _portText = value ?? string.Empty;
                _portTouched = true;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Port));
                RaiseValidity();
            }
        }

        /// <summary>The parsed port, or 0 when the text is not a number.</summary>
        public int Port => int.TryParse(_portText?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int p) ? p : 0;

        public ViscaTransportKind Transport
        {
            get => _transport;
            set
            {
                if (_transport == value) return;
                _transport = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TransportText));
                if (!_portTouched)
                {
                    _portText = DefaultPortFor(value).ToString(CultureInfo.InvariantCulture);
                    OnPropertyChanged(nameof(PortText));
                    OnPropertyChanged(nameof(Port));
                    RaiseValidity();
                }
            }
        }

        public string TransportText => TransportWord(_transport);

        /// <summary>None first, then transmitters (preview-supported first, then by name).</summary>
        public IReadOnlyList<TxChoice> TxChoices { get; }

        public TxChoice SelectedTx
        {
            get => _selectedTx;
            set
            {
                if (value == null || ReferenceEquals(_selectedTx, value)) return;
                _selectedTx = value;
                OnPropertyChanged();
            }
        }

        public bool IsValid => string.IsNullOrEmpty(SaveDisabledReason);

        /// <summary>Why Save is disabled; empty when it is enabled.</summary>
        public string SaveDisabledReason
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_host)) return "Enter the camera address";
                if (Port < 1 || Port > 65535) return "Port must be 1-65535";
                if (string.IsNullOrWhiteSpace(_name)) return "Enter a name";
                return string.Empty;
            }
        }

        /// <summary>Warns that another camera has this name. Never blocks saving.</summary>
        public string DuplicateNameWarning =>
            !string.IsNullOrWhiteSpace(_name) && _otherNames.Contains(_name.Trim())
                ? $"Another camera is already named \"{_name.Trim()}\""
                : string.Empty;

        public bool IsDetecting
        {
            get => _isDetecting;
            private set { if (_isDetecting == value) return; _isDetecting = value; OnPropertyChanged(); }
        }

        public string DetectText
        {
            get => _detectText;
            private set { if (_detectText == value) return; _detectText = value; OnPropertyChanged(); }
        }

        /// <summary>Every answer when several candidates replied (empty for none or one).</summary>
        public IReadOnlyList<ProbeHit> Hits
        {
            get => _hits;
            private set { _hits = value; OnPropertyChanged(); }
        }

        public bool IsTesting
        {
            get => _isTesting;
            private set { if (_isTesting == value) return; _isTesting = value; OnPropertyChanged(); }
        }

        public string TestText
        {
            get => _testText;
            private set { if (_testText == value) return; _testText = value; OnPropertyChanged(); }
        }

        public bool IsRemovePending
        {
            get => _isRemovePending;
            private set { if (_isRemovePending == value) return; _isRemovePending = value; OnPropertyChanged(); }
        }

        /// <summary>Asks for the inline confirmation (only for an existing camera).</summary>
        public void RequestRemove()
        {
            if (!IsNew)
                IsRemovePending = true;
        }

        /// <summary>The operator confirmed; returns true when a removal was pending (the dialog then removes the camera).</summary>
        public bool ConfirmRemove()
        {
            bool confirmed = IsRemovePending;
            IsRemovePending = false;
            return confirmed;
        }

        public void CancelRemove() => IsRemovePending = false;

        /// <summary>Builds the config to save. Keeps Id, ViscaAddress, ZoomMax and FocusMax of an existing camera.</summary>
        public RoomCameraConfig BuildConfig()
        {
            var config = new RoomCameraConfig
            {
                Name = _name.Trim(),
                Host = _host.Trim(),
                Port = Port,
                Transport = _transport,
                BoundTxMac = _selectedTx.Mac,
            };
            if (_existing != null)
            {
                config.Id = _existing.Id;
                config.ViscaAddress = _existing.ViscaAddress;
                config.ZoomMax = _existing.ZoomMax;
                config.FocusMax = _existing.FocusMax;
            }
            else
            {
                config.Id = Guid.NewGuid().ToString("N");
            }
            return config;
        }

        /// <summary>
        /// Probes the typed port first (TCP then UDP), then the default candidates, and fills port and
        /// transport from the first hit. Cancelling leaves nothing running.
        /// </summary>
        public async Task DetectAsync(CancellationToken ct)
        {
            if (IsDetecting) return;
            string host = _host.Trim();
            if (host.Length == 0)
            {
                Hits = Array.Empty<ProbeHit>();
                DetectText = "Enter the camera address";
                return;
            }

            IsDetecting = true;
            Hits = Array.Empty<ProbeHit>();
            DetectText = $"Looking for a camera on {host}";
            try
            {
                var hits = await _detect(host, BuildCandidates(), PerProbeTimeout, ct).ConfigureAwait(true);
                if (hits == null || hits.Count == 0)
                {
                    DetectText = $"No camera answered on {host}. Check the address, or enter the port by hand.";
                    return;
                }
                ApplyHit(hits[0]);
                Hits = hits.Count > 1 ? hits : Array.Empty<ProbeHit>();
                DetectText = hits.Count > 1
                    ? $"{hits.Count} answers on {host}. Using {DescribeHit(hits[0])}; pick another below if needed."
                    : $"Camera found: {DescribeHit(hits[0])}";
            }
            catch (OperationCanceledException)
            {
                DetectText = string.Empty;
            }
            catch (Exception)
            {
                DetectText = $"No camera answered on {host}. Check the address, or enter the port by hand.";
            }
            finally
            {
                IsDetecting = false;
            }
        }

        /// <summary>Fills port and transport from one detect answer (also used to pick from several).</summary>
        public void ApplyHit(ProbeHit hit)
        {
            if (hit == null) throw new ArgumentNullException(nameof(hit));
            Transport = hit.Transport;
            PortText = hit.Port.ToString(CultureInfo.InvariantCulture);
        }

        public static string DescribeHit(ProbeHit hit) => $"{hit.Port} ({TransportWord(hit.Transport)})";

        /// <summary>
        /// Connects a temporary camera built from the current fields, polls it once and reports in words.
        /// The temporary camera is always disposed.
        /// </summary>
        public async Task TestConnectionAsync(CancellationToken ct)
        {
            if (IsTesting) return;
            if (!IsValid)
            {
                TestText = SaveDisabledReason;
                return;
            }

            IsTesting = true;
            TestText = "Testing";
            string noAnswer = $"No answer on {_host.Trim()}:{Port} ({TransportText})";
            IPtzCamera? camera = null;
            try
            {
                camera = _factory.Create(BuildConfig());
                await camera.ConnectAsync(ct).ConfigureAwait(true);
                CameraStatus? status = await camera.PollAsync(ct).ConfigureAwait(true);
                TestText = status == null
                    ? noAnswer
                    : status.Power == CameraPower.On ? "Connected, camera on" : "Connected, camera off";
            }
            catch (OperationCanceledException)
            {
                TestText = string.Empty;
            }
            catch (Exception)
            {
                TestText = noAnswer;
            }
            finally
            {
                if (camera != null)
                {
                    try { await camera.DisposeAsync().ConfigureAwait(true); }
                    catch (Exception) { /* nothing more to release */ }
                }
                IsTesting = false;
            }
        }

        private List<(ViscaTransportKind, int)> BuildCandidates()
        {
            var list = new List<(ViscaTransportKind, int)>();
            int typed = Port;
            if (typed >= 1 && typed <= 65535)
            {
                list.Add((ViscaTransportKind.Tcp, typed));
                list.Add((ViscaTransportKind.Udp, typed));
            }
            foreach (var candidate in ViscaProbe.DefaultCandidates)
            {
                if (!list.Contains(candidate))
                    list.Add(candidate);
            }
            return list;
        }

        private static IReadOnlyList<TxChoice> BuildTxChoices(IEnumerable<SourceCardViewModel> sources, string boundMac)
        {
            var list = new List<TxChoice> { new TxChoice("None", string.Empty, false) };
            var cards = sources
                .Where(s => s.Device.IsTransmitter)
                .OrderBy(s => s.IsPreviewSupported ? 0 : 1)
                .ThenBy(s => s.DeviceName, StringComparer.CurrentCultureIgnoreCase)
                .Select(s => new TxChoice(s.DeviceName, s.MacAddress, s.IsPreviewSupported))
                .ToList();
            list.AddRange(cards);
            // A saved binding to a TX that is not discovered right now is kept, not silently dropped.
            if (!string.IsNullOrEmpty(boundMac) && cards.All(c => c.Mac != boundMac))
                list.Add(new TxChoice($"Offline device ({boundMac})", boundMac, false));
            return list;
        }

        private static int DefaultPortFor(ViscaTransportKind kind) =>
            kind == ViscaTransportKind.Tcp ? DefaultTcpPort : DefaultUdpPort;

        private static string TransportWord(ViscaTransportKind kind) =>
            kind == ViscaTransportKind.Tcp ? "TCP" : "UDP";

        private void RaiseValidity()
        {
            OnPropertyChanged(nameof(IsValid));
            OnPropertyChanged(nameof(SaveDisabledReason));
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
