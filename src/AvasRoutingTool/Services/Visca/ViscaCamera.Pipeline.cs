using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AvasRoutingTool.Services.Visca
{
    /// <summary>
    /// Sending and reply matching. Replies carry an unreliable socket nibble, so each
    /// ACK, completion or inquiry reply goes to the oldest exchange that is waiting for
    /// that kind of reply (arrival order). Exchanges are registered and written to the
    /// transport under one lock, so registration order is wire order.
    /// </summary>
    public sealed partial class ViscaCamera
    {
        private static readonly TimeSpan CompletionStale = TimeSpan.FromSeconds(10);

        private readonly object _wire = new();
        private readonly List<Exchange> _pending = new();   // guarded by _wire
        private readonly SemaphoreSlim _queue = new(1, 1);  // serialises normal commands

        // Count of Stops issued per motion. Incremented under _wire; read with Volatile.
        private int _panTiltStopEpoch;
        private int _zoomStopEpoch;

        /// <summary>How long to wait for an ACK (or an inquiry reply) before retrying.</summary>
        internal TimeSpan AckTimeout { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>How long a command that was acknowledged may take to complete before the queue moves on.</summary>
        internal TimeSpan CompletionTimeout { get; set; } = TimeSpan.FromSeconds(3);

        private enum Stage { AwaitFirst, AwaitCompletion }

        private enum Motion { None, PanTilt, Zoom }

        private sealed class Exchange
        {
            public Exchange(bool isInquiry, Motion motion)
            {
                IsInquiry = isInquiry;
                Motion = motion;
                Created = Environment.TickCount64;
            }

            public bool IsInquiry { get; }
            public Motion Motion { get; }
            public long Created { get; set; }
            public Stage Stage { get; set; } = Stage.AwaitFirst;

            /// <summary>Resolves with the ACK, the inquiry reply, or an error; null when the link was lost.</summary>
            public TaskCompletionSource<ViscaResponse?> First { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>Resolves with the completion or an error; null when superseded by a Stop or the link was lost.</summary>
            public TaskCompletionSource<ViscaResponse?> Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        /// <summary>
        /// Registers an exchange and starts writing it. Returns the write task, or null when there is no live transport,
        /// or when requiredEpoch is given and a Stop for the exchange's motion has been issued since (nothing is sent).
        /// The check and the registration share the _wire lock, so no Stop can slip between them.
        /// </summary>
        private Task? BeginSend(Exchange exchange, byte[] bytes, IViscaTransport transport, CancellationToken ct, int? requiredEpoch = null)
        {
            lock (_wire)
            {
                if (requiredEpoch is { } epoch && StopEpoch(exchange.Motion) != epoch)
                    return null;

                PurgeStaleLocked();
                _pending.Add(exchange);
                try
                {
                    return transport.SendAsync(bytes, exchange.IsInquiry, ct);
                }
                catch (Exception)
                {
                    _pending.Remove(exchange);
                    return null;
                }
            }
        }

        private void Remove(Exchange exchange)
        {
            lock (_wire) _pending.Remove(exchange);
        }

        /// <summary>
        /// Sends a command until it is acknowledged. waitCompletion also waits (bounded) for the
        /// completion; a Stop (or an error reply) ends that wait with false. epoch, when given, is the
        /// stop epoch of the motion at call time: once a Stop has been issued since, nothing is sent.
        /// </summary>
        private async Task<bool> ExchangeAsync(byte[] bytes, Motion motion, int retries, bool waitCompletion, CancellationToken ct, int? epoch = null)
        {
            for (int attempt = 0; attempt <= retries; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                IViscaTransport? transport = CurrentTransport();
                if (transport == null)
                    return false;

                var exchange = new Exchange(isInquiry: false, motion);
                Task? write = BeginSend(exchange, bytes, transport, ct, epoch);
                if (write == null)
                    return false;

                try
                {
                    await write.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    Remove(exchange);
                    throw;
                }
                catch (Exception)
                {
                    Remove(exchange);
                    continue;   // the transport reports a lost link itself; a retry fails fast
                }

                ViscaResponse? first;
                try
                {
                    first = await exchange.First.Task.WaitAsync(AckTimeout, ct).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    Remove(exchange);
                    continue;
                }
                catch (OperationCanceledException)
                {
                    Remove(exchange);
                    throw;
                }

                if (first is not { Kind: ViscaResponseKind.Ack or ViscaResponseKind.Completion })
                    return false;   // error reply, or the link was lost

                ReportSuccess();
                if (!waitCompletion || first.Kind == ViscaResponseKind.Completion)
                    return true;

                try
                {
                    ViscaResponse? done = await exchange.Done.Task.WaitAsync(CompletionTimeout, ct).ConfigureAwait(false);
                    return done is { Kind: ViscaResponseKind.Completion };
                }
                catch (TimeoutException)
                {
                    return true;   // acknowledged; the camera is just slow to say it finished
                }
            }
            return false;
        }

        /// <summary>One inquiry, no retry. Returns the reply data, or null on timeout, error or lost link.</summary>
        private async Task<byte[]?> InquireAsync(byte[] bytes, CancellationToken ct)
        {
            IViscaTransport? transport = CurrentTransport();
            if (transport == null)
                return null;

            var exchange = new Exchange(isInquiry: true, Motion.None);
            Task? write = BeginSend(exchange, bytes, transport, ct);
            if (write == null)
                return null;

            try
            {
                await write.ConfigureAwait(false);
                ViscaResponse? reply = await exchange.First.Task.WaitAsync(AckTimeout, ct).ConfigureAwait(false);
                return reply is { Kind: ViscaResponseKind.InquiryReply } ? reply.Data : null;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                Remove(exchange);
                throw;
            }
            catch (Exception)
            {
                Remove(exchange);
                return null;
            }
        }

        // ---- receiving ----

        private void OnMessage(byte[] message)
        {
            ViscaResponse? response = ViscaResponse.TryParse(message);
            if (response == null)
                return;

            lock (_wire)
            {
                PurgeStaleLocked();
                switch (response.Kind)
                {
                    case ViscaResponseKind.Ack:
                    {
                        Exchange? e = Find(x => !x.IsInquiry && x.Stage == Stage.AwaitFirst);
                        if (e != null)
                        {
                            e.Stage = Stage.AwaitCompletion;
                            e.Created = Environment.TickCount64;
                            e.First.TrySetResult(response);
                        }
                        break;
                    }
                    case ViscaResponseKind.Completion:
                    {
                        Exchange? e = Find(x => !x.IsInquiry && x.Stage == Stage.AwaitCompletion);
                        if (e != null)
                        {
                            _pending.Remove(e);
                            e.Done.TrySetResult(response);
                            break;
                        }
                        // A camera may complete without a separate ACK.
                        e = Find(x => !x.IsInquiry && x.Stage == Stage.AwaitFirst);
                        if (e != null)
                        {
                            _pending.Remove(e);
                            e.First.TrySetResult(response);
                            e.Done.TrySetResult(response);
                        }
                        break;
                    }
                    case ViscaResponseKind.InquiryReply:
                    {
                        Exchange? e = Find(x => x.IsInquiry && x.Stage == Stage.AwaitFirst);
                        if (e != null)
                        {
                            _pending.Remove(e);
                            e.First.TrySetResult(response);
                        }
                        break;
                    }
                    case ViscaResponseKind.Error:
                    {
                        // 04 (cancelled) refers to a command already in progress; the others reject the newest unacknowledged one.
                        Exchange? e = response.ErrorCode == 0x04
                            ? Find(x => x.Stage == Stage.AwaitCompletion) ?? Find(x => x.Stage == Stage.AwaitFirst)
                            : Find(x => x.Stage == Stage.AwaitFirst) ?? Find(x => x.Stage == Stage.AwaitCompletion);
                        if (e != null)
                        {
                            _pending.Remove(e);
                            if (e.Stage == Stage.AwaitFirst)
                                e.First.TrySetResult(response);
                            e.Done.TrySetResult(response);
                        }
                        break;
                    }
                }
            }
        }

        private Exchange? Find(Func<Exchange, bool> predicate)
        {
            foreach (Exchange e in _pending)
                if (predicate(e))
                    return e;
            return null;
        }

        /// <summary>Stop epoch of a motion. Commands capture it when they are called; a Stop that follows makes them stale.</summary>
        private int StopEpoch(Motion motion) => motion switch
        {
            Motion.PanTilt => Volatile.Read(ref _panTiltStopEpoch),
            Motion.Zoom => Volatile.Read(ref _zoomStopEpoch),
            _ => 0
        };

        /// <summary>Called under _wire when a Stop for the motion is issued.</summary>
        private void BumpStopEpochLocked(Motion motion)
        {
            if (motion == Motion.PanTilt)
                Interlocked.Increment(ref _panTiltStopEpoch);
            else if (motion == Motion.Zoom)
                Interlocked.Increment(ref _zoomStopEpoch);
        }

        /// <summary>Ends the wait of every motion command of this kind that is still waiting for completion. They keep their place in line so the camera's reply still lands on them.</summary>
        private void SupersedeLocked(Motion motion)
        {
            foreach (Exchange e in _pending)
                if (e.Motion == motion && e.Stage == Stage.AwaitCompletion)
                    e.Done.TrySetResult(null);
        }

        private void FailAllPending()
        {
            lock (_wire)
            {
                foreach (Exchange e in _pending)
                {
                    e.First.TrySetResult(null);
                    e.Done.TrySetResult(null);
                }
                _pending.Clear();
            }
        }

        // Exchanges nobody waits for any more (fire-and-forget stops, slow completions) must not
        // swallow a later reply.
        private void PurgeStaleLocked()
        {
            long now = Environment.TickCount64;
            long ackLimit = (long)(AckTimeout.TotalMilliseconds * 2) + 1000;
            long doneLimit = (long)CompletionStale.TotalMilliseconds;
            _pending.RemoveAll(e => now - e.Created > (e.Stage == Stage.AwaitFirst ? ackLimit : doneLimit));
        }
    }
}
