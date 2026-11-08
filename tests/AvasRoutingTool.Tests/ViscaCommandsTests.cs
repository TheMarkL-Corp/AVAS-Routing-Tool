using System;
using System.Globalization;
using AvasRoutingTool.Services.Visca;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public class ViscaCommandsTests
    {
        // Golden bytes are taken from the VISCA command table in
        // docs/superpowers/plans/2026-11-02-room-cameras.md ("VISCA command set (v1)").
        // Address 1 (camera 1) is used unless a test says otherwise.

        private static byte[] Hex(string hex)
        {
            var parts = hex.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var bytes = new byte[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                bytes[i] = byte.Parse(parts[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return bytes;
        }

        // ---- Pan/tilt drive: 81 01 06 01 VV WW pp qq FF ----

        [Fact]
        public void PanTilt_Up_Speed12_10()
        {
            Assert.Equal(Hex("81 01 06 01 0C 0A 03 01 FF"),
                ViscaCommands.PanTilt(1, PanTiltDirection.Up, 0x0C, 0x0A));
        }

        [Fact]
        public void PanTilt_Down_Speed12_10()
        {
            Assert.Equal(Hex("81 01 06 01 0C 0A 03 02 FF"),
                ViscaCommands.PanTilt(1, PanTiltDirection.Down, 0x0C, 0x0A));
        }

        [Fact]
        public void PanTilt_Left_Speed12_10()
        {
            Assert.Equal(Hex("81 01 06 01 0C 0A 01 03 FF"),
                ViscaCommands.PanTilt(1, PanTiltDirection.Left, 0x0C, 0x0A));
        }

        [Fact]
        public void PanTilt_Right_Speed12_10()
        {
            Assert.Equal(Hex("81 01 06 01 0C 0A 02 03 FF"),
                ViscaCommands.PanTilt(1, PanTiltDirection.Right, 0x0C, 0x0A));
        }

        [Fact]
        public void PanTilt_UpLeft_Speed12_10()
        {
            Assert.Equal(Hex("81 01 06 01 0C 0A 01 01 FF"),
                ViscaCommands.PanTilt(1, PanTiltDirection.UpLeft, 0x0C, 0x0A));
        }

        [Fact]
        public void PanTilt_UpRight_Speed12_10()
        {
            Assert.Equal(Hex("81 01 06 01 0C 0A 02 01 FF"),
                ViscaCommands.PanTilt(1, PanTiltDirection.UpRight, 0x0C, 0x0A));
        }

        [Fact]
        public void PanTilt_DownLeft_Speed12_10()
        {
            Assert.Equal(Hex("81 01 06 01 0C 0A 01 02 FF"),
                ViscaCommands.PanTilt(1, PanTiltDirection.DownLeft, 0x0C, 0x0A));
        }

        [Fact]
        public void PanTilt_DownRight_Speed12_10()
        {
            Assert.Equal(Hex("81 01 06 01 0C 0A 02 02 FF"),
                ViscaCommands.PanTilt(1, PanTiltDirection.DownRight, 0x0C, 0x0A));
        }

        [Fact]
        public void PanTilt_ClampsPanSpeedToRange()
        {
            Assert.Equal(Hex("81 01 06 01 01 0A 01 01 FF"),
                ViscaCommands.PanTilt(1, PanTiltDirection.UpLeft, 0, 0x0A));
            Assert.Equal(Hex("81 01 06 01 18 0A 01 01 FF"),
                ViscaCommands.PanTilt(1, PanTiltDirection.UpLeft, 0x99, 0x0A));
        }

        [Fact]
        public void PanTilt_ClampsTiltSpeedToRange()
        {
            Assert.Equal(Hex("81 01 06 01 0C 01 01 01 FF"),
                ViscaCommands.PanTilt(1, PanTiltDirection.UpLeft, 0x0C, -4));
            Assert.Equal(Hex("81 01 06 01 0C 14 01 01 FF"),
                ViscaCommands.PanTilt(1, PanTiltDirection.UpLeft, 0x0C, 0xFF));
        }

        // ---- Stop pan/tilt: 81 01 06 01 01 01 03 03 FF ----

        [Fact]
        public void StopPanTilt_()
        {
            Assert.Equal(Hex("81 01 06 01 01 01 03 03 FF"), ViscaCommands.StopPanTilt(1));
        }

        // ---- Home: 81 01 06 04 FF ----

        [Fact]
        public void Home_()
        {
            Assert.Equal(Hex("81 01 06 04 FF"), ViscaCommands.Home(1));
        }

        // ---- Zoom tele / wide / stop: 81 01 04 07 2p FF / 3p / 00 ----

        [Fact]
        public void Zoom_Tele_Speed4()
        {
            Assert.Equal(Hex("81 01 04 07 24 FF"), ViscaCommands.Zoom(1, ZoomDirection.Tele, 4));
        }

        [Fact]
        public void Zoom_Wide_Speed4()
        {
            Assert.Equal(Hex("81 01 04 07 34 FF"), ViscaCommands.Zoom(1, ZoomDirection.Wide, 4));
        }

        [Fact]
        public void Zoom_ClampsSpeedToZeroToSeven()
        {
            Assert.Equal(Hex("81 01 04 07 20 FF"), ViscaCommands.Zoom(1, ZoomDirection.Tele, -3));
            Assert.Equal(Hex("81 01 04 07 27 FF"), ViscaCommands.Zoom(1, ZoomDirection.Tele, 9));
        }

        [Fact]
        public void StopZoom_()
        {
            Assert.Equal(Hex("81 01 04 07 00 FF"), ViscaCommands.StopZoom(1));
        }

        // ---- Zoom direct: 81 01 04 47 0p 0q 0r 0s FF ----

        [Fact]
        public void ZoomDirect_0x2A5C()
        {
            Assert.Equal(Hex("81 01 04 47 02 0A 05 0C FF"), ViscaCommands.ZoomDirect(1, 0x2A5C));
        }

        [Fact]
        public void ZoomDirect_ClampsPositionToZeroToFFFF()
        {
            Assert.Equal(Hex("81 01 04 47 00 00 00 00 FF"), ViscaCommands.ZoomDirect(1, -5));
            Assert.Equal(Hex("81 01 04 47 0F 0F 0F 0F FF"), ViscaCommands.ZoomDirect(1, 0x1FFFF));
        }

        // ---- Focus auto / manual: 81 01 04 38 02 FF / 03 ----

        [Fact]
        public void FocusAuto_True_Auto()
        {
            Assert.Equal(Hex("81 01 04 38 02 FF"), ViscaCommands.FocusAuto(1, true));
        }

        [Fact]
        public void FocusAuto_False_Manual()
        {
            Assert.Equal(Hex("81 01 04 38 03 FF"), ViscaCommands.FocusAuto(1, false));
        }

        // ---- Focus far / near / stop: 81 01 04 08 2p FF / 3p / 00 ----

        [Fact]
        public void Focus_Far_Speed4()
        {
            Assert.Equal(Hex("81 01 04 08 24 FF"), ViscaCommands.Focus(1, FocusDirection.Far, 4));
        }

        [Fact]
        public void Focus_Near_Speed4()
        {
            Assert.Equal(Hex("81 01 04 08 34 FF"), ViscaCommands.Focus(1, FocusDirection.Near, 4));
        }

        [Fact]
        public void Focus_ClampsSpeedToZeroToSeven()
        {
            Assert.Equal(Hex("81 01 04 08 20 FF"), ViscaCommands.Focus(1, FocusDirection.Far, -1));
            Assert.Equal(Hex("81 01 04 08 37 FF"), ViscaCommands.Focus(1, FocusDirection.Near, 70));
        }

        [Fact]
        public void StopFocus_()
        {
            Assert.Equal(Hex("81 01 04 08 00 FF"), ViscaCommands.StopFocus(1));
        }

        // ---- Focus direct: 81 01 04 48 0p 0q 0r 0s FF ----

        [Fact]
        public void FocusDirect_0x1234()
        {
            Assert.Equal(Hex("81 01 04 48 01 02 03 04 FF"), ViscaCommands.FocusDirect(1, 0x1234));
        }

        [Fact]
        public void FocusDirect_ClampsPositionToZeroToFFFF()
        {
            Assert.Equal(Hex("81 01 04 48 00 00 00 00 FF"), ViscaCommands.FocusDirect(1, -1));
            Assert.Equal(Hex("81 01 04 48 0F 0F 0F 0F FF"), ViscaCommands.FocusDirect(1, 0x10000));
        }

        // ---- Power on / off: 81 01 04 00 02 FF / 03 FF ----

        [Fact]
        public void Power_On()
        {
            Assert.Equal(Hex("81 01 04 00 02 FF"), ViscaCommands.Power(1, true));
        }

        [Fact]
        public void Power_Off()
        {
            Assert.Equal(Hex("81 01 04 00 03 FF"), ViscaCommands.Power(1, false));
        }

        // ---- Inquiries ----

        [Fact]
        public void InquirePower_()
        {
            Assert.Equal(Hex("81 09 04 00 FF"), ViscaCommands.InquirePower(1));
        }

        [Fact]
        public void InquireZoom_()
        {
            Assert.Equal(Hex("81 09 04 47 FF"), ViscaCommands.InquireZoom(1));
        }

        [Fact]
        public void InquireFocus_()
        {
            Assert.Equal(Hex("81 09 04 48 FF"), ViscaCommands.InquireFocus(1));
        }

        [Fact]
        public void InquireFocusMode_()
        {
            Assert.Equal(Hex("81 09 04 38 FF"), ViscaCommands.InquireFocusMode(1));
        }

        // ---- Address mapping ----

        [Fact]
        public void Address3_YieldsFirstByte0x83()
        {
            var bytes = ViscaCommands.Home(3);
            Assert.Equal(0x83, bytes[0]);
            Assert.Equal(Hex("83 01 06 04 FF"), bytes);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(8)]
        public void Address_OutsideOneToSeven_Throws(int address)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ViscaCommands.Home(address));
        }
    }
}
