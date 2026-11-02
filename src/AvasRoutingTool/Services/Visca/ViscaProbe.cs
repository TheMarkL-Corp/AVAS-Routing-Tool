using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AvasRoutingTool.Configuration;

namespace AvasRoutingTool.Services.Visca
{
    /// <summary>A candidate port that answered a power inquiry with a valid camera reply.</summary>
    public record ProbeHit(ViscaTransportKind Transport, int Port, CameraPower Power);

    /// <summary>
    /// Finds VISCA cameras on a host by asking each candidate port for its power state.
    /// One short-lived transport per candidate, always disposed. Candidates are probed
    /// in parallel, at most four at a time.
    /// </summary>
    public static class ViscaProbe
    {
        public static readonly (ViscaTransportKind, int)[] DefaultCandidates =
        {
            (ViscaTransportKind.Tcp, 5678),
            (ViscaTransportKind.Tcp, 1259),
            (ViscaTransportKind.Tcp, 3001),
            (ViscaTransportKind.Udp, 52381),
            (ViscaTransportKind.Udp, 1259),
            (ViscaTransportKind.Udp, 3001),
        };

        private const int MaxParallel = 4;
        private const byte PowerOnValue = 0x02;
        private const byte PowerOffValue = 0x03;
        private const byte PowerFaultValue = 0x04;
        private const int CameraAddress = 1;

        /// <summary>
        /// Probes every candidate. Returns hits with TCP before UDP, then in candidate order.
        /// Cancelling throws OperationCanceledException after every probe socket is disposed.
        /// </summary>
        public static async Task<IReadOnlyList<ProbeHit>> DetectAsync(
            string host,
            IEnumerable<(ViscaTransportKind, int)>? candidates,
            TimeSpan perProbeTimeout,
            CancellationToken ct)
        {
            var list = (candidates ?? DefaultCandidates).ToList();
            using var gate = new SemaphoreSlim(MaxParallel, MaxParallel);

            var probes = list.Select(c => ProbeOneAsync(host, c.Item1, c.Item2, perProbeTimeout, gate, ct)).ToArray();
            ProbeHit?[] results = await Task.WhenAll(probes).ConfigureAwait(false);

            var hits = new List<(ProbeHit Hit, int Index)>();
            for (int i = 0; i < results.Length; i++)
            {
                if (results[i] is { } hit)
                    hits.Add((hit, i));
            }

            return hits
                .OrderBy(h => h.Hit.Transport == ViscaTransportKind.Tcp ? 0 : 1)
                .ThenBy(h => h.Index)
                .Select(h => h.Hit)
                .ToList();
        }

        private static async Task<ProbeHit?> ProbeOneAsync(
            string host,
            ViscaTransportKind kind,
            int port,
            TimeSpan perProbeTimeout,
            SemaphoreSlim gate,
            CancellationToken ct)
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
                budget.CancelAfter(perProbeTimeout);

                await using IViscaTransport transport = kind == ViscaTransportKind.Tcp
                    ? new TcpViscaTransport(host, port)
                    : new UdpViscaTransport(host, port);

                var power = new TaskCompletionSource<CameraPower>(TaskCreationOptions.RunContinuationsAsynchronously);
                transport.MessageReceived += message =>
                {
                    if (TryReadPower(message, out CameraPower value))
                        power.TrySetResult(value);
                };

                try
                {
                    await transport.ConnectAsync(budget.Token).ConfigureAwait(false);
                    await transport.SendAsync(ViscaCommands.InquirePower(CameraAddress), true, budget.Token).ConfigureAwait(false);
                    CameraPower value = await power.Task.WaitAsync(budget.Token).ConfigureAwait(false);
                    return new ProbeHit(kind, port, value);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                    // Refused, unreachable, timed out, or not a camera: not a hit.
                    return null;
                }
            }
            finally
            {
                gate.Release();
            }
        }

        // A power inquiry reply is 90 50 pp FF with pp in {02, 03, 04}.
        private static bool TryReadPower(byte[] message, out CameraPower power)
        {
            power = CameraPower.Unknown;
            ViscaResponse? response = ViscaResponse.TryParse(message);
            if (response is not { Kind: ViscaResponseKind.InquiryReply, Data.Length: 1 } reply)
                return false;

            switch (reply.Data[0])
            {
                case PowerOnValue:
                    power = CameraPower.On;
                    return true;
                case PowerOffValue:
                    power = CameraPower.Off;
                    return true;
                case PowerFaultValue:
                    power = CameraPower.Fault;
                    return true;
                default:
                    return false;
            }
        }
    }
}
