using System;

namespace AvasRoutingTool.Services.Visca
{
    public enum PanTiltDirection { Up, Down, Left, Right, UpLeft, UpRight, DownLeft, DownRight }

    public enum ZoomDirection { Tele, Wide }

    public enum FocusDirection { Far, Near }

    /// <summary>
    /// Builds VISCA packets (Sony-standard byte patterns, see the plan's
    /// "VISCA command set (v1)"). Pure functions: no I/O, no state.
    /// </summary>
    public static class ViscaCommands
    {
        public const byte Terminator = 0xFF;

        public const int PanSpeedMin = 0x01;
        public const int PanSpeedMax = 0x18;
        public const int TiltSpeedMin = 0x01;
        public const int TiltSpeedMax = 0x14;
        public const int ZoomFocusSpeedMin = 0;
        public const int ZoomFocusSpeedMax = 7;
        public const int PositionMin = 0;
        public const int PositionMax = 0xFFFF;

        /// <summary>Pan/tilt drive: 81 01 06 01 VV WW pp qq FF.</summary>
        public static byte[] PanTilt(int address, PanTiltDirection direction, int panSpeed, int tiltSpeed)
        {
            byte pan = direction switch
            {
                PanTiltDirection.Left or PanTiltDirection.UpLeft or PanTiltDirection.DownLeft => 0x01,
                PanTiltDirection.Right or PanTiltDirection.UpRight or PanTiltDirection.DownRight => 0x02,
                _ => 0x03
            };
            byte tilt = direction switch
            {
                PanTiltDirection.Up or PanTiltDirection.UpLeft or PanTiltDirection.UpRight => 0x01,
                PanTiltDirection.Down or PanTiltDirection.DownLeft or PanTiltDirection.DownRight => 0x02,
                _ => 0x03
            };
            return Command(address, 0x06, 0x01,
                (byte)Clamp(panSpeed, PanSpeedMin, PanSpeedMax),
                (byte)Clamp(tiltSpeed, TiltSpeedMin, TiltSpeedMax),
                pan, tilt);
        }

        /// <summary>Stop pan/tilt: 81 01 06 01 01 01 03 03 FF.</summary>
        public static byte[] StopPanTilt(int address)
            => Command(address, 0x06, 0x01, 0x01, 0x01, 0x03, 0x03);

        /// <summary>Home: 81 01 06 04 FF.</summary>
        public static byte[] Home(int address)
            => Command(address, 0x06, 0x04);

        /// <summary>Zoom tele 81 01 04 07 2p FF, wide 81 01 04 07 3p FF.</summary>
        public static byte[] Zoom(int address, ZoomDirection direction, int speed)
        {
            byte baseByte = direction == ZoomDirection.Tele ? (byte)0x20 : (byte)0x30;
            return Command(address, 0x04, 0x07, (byte)(baseByte | Clamp(speed, ZoomFocusSpeedMin, ZoomFocusSpeedMax)));
        }

        /// <summary>Zoom stop: 81 01 04 07 00 FF.</summary>
        public static byte[] StopZoom(int address)
            => Command(address, 0x04, 0x07, 0x00);

        /// <summary>Zoom direct: 81 01 04 47 0p 0q 0r 0s FF (nibbles of the position).</summary>
        public static byte[] ZoomDirect(int address, int position)
        {
            var n = Nibbles(Clamp(position, PositionMin, PositionMax));
            return Command(address, 0x04, 0x47, n[0], n[1], n[2], n[3]);
        }

        /// <summary>Focus auto 81 01 04 38 02 FF (true), manual 81 01 04 38 03 FF (false).</summary>
        public static byte[] FocusAuto(int address, bool auto)
            => Command(address, 0x04, 0x38, auto ? (byte)0x02 : (byte)0x03);

        /// <summary>Focus far 81 01 04 08 2p FF, near 81 01 04 08 3p FF.</summary>
        public static byte[] Focus(int address, FocusDirection direction, int speed)
        {
            byte baseByte = direction == FocusDirection.Far ? (byte)0x20 : (byte)0x30;
            return Command(address, 0x04, 0x08, (byte)(baseByte | Clamp(speed, ZoomFocusSpeedMin, ZoomFocusSpeedMax)));
        }

        /// <summary>Focus stop: 81 01 04 08 00 FF.</summary>
        public static byte[] StopFocus(int address)
            => Command(address, 0x04, 0x08, 0x00);

        /// <summary>Focus direct: 81 01 04 48 0p 0q 0r 0s FF (nibbles of the position).</summary>
        public static byte[] FocusDirect(int address, int position)
        {
            var n = Nibbles(Clamp(position, PositionMin, PositionMax));
            return Command(address, 0x04, 0x48, n[0], n[1], n[2], n[3]);
        }

        /// <summary>Power on 81 01 04 00 02 FF, off 81 01 04 00 03 FF.</summary>
        public static byte[] Power(int address, bool on)
            => Command(address, 0x04, 0x00, on ? (byte)0x02 : (byte)0x03);

        /// <summary>Power inquiry: 81 09 04 00 FF.</summary>
        public static byte[] InquirePower(int address)
            => Inquiry(address, 0x04, 0x00);

        /// <summary>Zoom position inquiry: 81 09 04 47 FF.</summary>
        public static byte[] InquireZoom(int address)
            => Inquiry(address, 0x04, 0x47);

        /// <summary>Focus position inquiry: 81 09 04 48 FF.</summary>
        public static byte[] InquireFocus(int address)
            => Inquiry(address, 0x04, 0x48);

        /// <summary>Focus mode inquiry: 81 09 04 38 FF.</summary>
        public static byte[] InquireFocusMode(int address)
            => Inquiry(address, 0x04, 0x38);

        // Command packet: [0x80+address] [0x01] body... [0xFF]
        private static byte[] Command(int address, params byte[] body)
            => Packet(address, 0x01, body);

        // Inquiry packet: [0x80+address] [0x09] body... [0xFF]
        private static byte[] Inquiry(int address, params byte[] body)
            => Packet(address, 0x09, body);

        private static byte[] Packet(int address, byte type, byte[] body)
        {
            if (address < 1 || address > 7)
                throw new ArgumentOutOfRangeException(nameof(address), address, "VISCA address must be 1-7.");

            var packet = new byte[body.Length + 3];
            packet[0] = (byte)(0x80 + address);
            packet[1] = type;
            Array.Copy(body, 0, packet, 2, body.Length);
            packet[^1] = Terminator;
            return packet;
        }

        private static byte[] Nibbles(int value)
            => new[]
            {
                (byte)((value >> 12) & 0x0F),
                (byte)((value >> 8) & 0x0F),
                (byte)((value >> 4) & 0x0F),
                (byte)(value & 0x0F)
            };

        private static int Clamp(int value, int min, int max)
            => value < min ? min : value > max ? max : value;
    }
}
