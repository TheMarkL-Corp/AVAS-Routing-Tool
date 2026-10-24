using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;

namespace AvasRoutingTool.Sdvoe
{
    public class MulticastIpManager
    {
        private readonly uint _startIp;
        private readonly uint _endIp;
        private readonly ConcurrentDictionary<string, uint> _assignedIps = new(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<uint> _freeIps = new();
        private readonly object _lock = new();
        private uint _nextIp;

        public MulticastIpManager(string startIp = "225.0.0.1", string endIp = "225.0.1.254")
        {
            _startIp = IpToUint(IPAddress.Parse(startIp));
            _endIp = IpToUint(IPAddress.Parse(endIp));
            _nextIp = _startIp;
        }

        public string AllocateIp(string macAddress)
        {
            lock (_lock)
            {
                if (_assignedIps.TryGetValue(macAddress, out uint existing))
                {
                    return UintToIp(existing);
                }

                uint allocated;
                if (_freeIps.Count > 0)
                {
                    allocated = _freeIps.Dequeue();
                }
                else if (_nextIp <= _endIp)
                {
                    allocated = _nextIp++;
                }
                else
                {
                    throw new InvalidOperationException("Multicast IP pool exhausted.");
                }

                _assignedIps[macAddress] = allocated;
                return UintToIp(allocated);
            }
        }

        public void ReleaseIp(string macAddress)
        {
            lock (_lock)
            {
                if (_assignedIps.TryRemove(macAddress, out uint ip))
                {
                    _freeIps.Enqueue(ip);
                }
            }
        }

        private static uint IpToUint(IPAddress ip)
        {
            byte[] bytes = ip.GetAddressBytes();
            Array.Reverse(bytes);
            return BitConverter.ToUInt32(bytes, 0);
        }

        private static string UintToIp(uint value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            Array.Reverse(bytes);
            return new IPAddress(bytes).ToString();
        }
    }
}
