using System.IO;
using AvasRoutingTool.Configuration;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public class RoomCameraConfigTests
    {
        private static string TempFile() =>
            Path.Combine(Path.GetTempPath(), $"cfg_{System.Guid.NewGuid()}.json");

        [Fact]
        public void RoundTrip_PreservesAllCameraFields()
        {
            string file = TempFile();
            try
            {
                var service = new ConfigService(file);
                var config = new AppConfig
                {
                    RoomCameras = new System.Collections.Generic.List<RoomCameraConfig>
                    {
                        new RoomCameraConfig
                        {
                            Id = "abc123",
                            Name = "ROOM-CAM",
                            Host = "192.168.1.163",
                            Port = 1259,
                            Transport = ViscaTransportKind.Tcp,
                            ViscaAddress = 3,
                            BoundTxMac = "00:11:22:33:44:55",
                            ZoomMax = 0x2A5C,
                            FocusMax = 0x1234
                        },
                        new RoomCameraConfig
                        {
                            Id = "def456",
                            Name = "OR-2 ceiling",
                            Host = "10.0.0.7",
                            Port = 3001,
                            Transport = ViscaTransportKind.Udp,
                            ViscaAddress = 7,
                            BoundTxMac = "",
                            ZoomMax = 0x3000,
                            FocusMax = 0x8000
                        }
                    }
                };

                service.Save(config);
                var loaded = new ConfigService(file).Load();

                Assert.Equal(2, loaded.RoomCameras.Count);

                var a = loaded.RoomCameras[0];
                Assert.Equal("abc123", a.Id);
                Assert.Equal("ROOM-CAM", a.Name);
                Assert.Equal("192.168.1.163", a.Host);
                Assert.Equal(1259, a.Port);
                Assert.Equal(ViscaTransportKind.Tcp, a.Transport);
                Assert.Equal(3, a.ViscaAddress);
                Assert.Equal("00:11:22:33:44:55", a.BoundTxMac);
                Assert.Equal(0x2A5C, a.ZoomMax);
                Assert.Equal(0x1234, a.FocusMax);

                var b = loaded.RoomCameras[1];
                Assert.Equal("def456", b.Id);
                Assert.Equal("OR-2 ceiling", b.Name);
                Assert.Equal("10.0.0.7", b.Host);
                Assert.Equal(3001, b.Port);
                Assert.Equal(ViscaTransportKind.Udp, b.Transport);
                Assert.Equal(7, b.ViscaAddress);
                Assert.Equal("", b.BoundTxMac);
                Assert.Equal(0x3000, b.ZoomMax);
                Assert.Equal(0x8000, b.FocusMax);
            }
            finally
            {
                if (File.Exists(file)) File.Delete(file);
            }
        }

        [Fact]
        public void LegacyFileWithoutRoomCameras_LoadsEmptyList()
        {
            string file = TempFile();
            try
            {
                File.WriteAllText(file, "{\n  \"BavmUrl\": \"http://localhost:80\",\n  \"Theme\": \"Light\"\n}");

                var loaded = new ConfigService(file).Load();

                Assert.NotNull(loaded.RoomCameras);
                Assert.Empty(loaded.RoomCameras);
                Assert.Equal("Light", loaded.Theme);
            }
            finally
            {
                if (File.Exists(file)) File.Delete(file);
            }
        }

        [Fact]
        public void SavingCameras_KeepsTheme()
        {
            string file = TempFile();
            try
            {
                var service = new ConfigService(file);
                var config = new AppConfig
                {
                    Theme = "Light",
                    RoomCameras = new System.Collections.Generic.List<RoomCameraConfig>
                    {
                        new RoomCameraConfig { Name = "ROOM-CAM", Host = "192.168.1.163" }
                    }
                };

                service.Save(config);
                var loaded = new ConfigService(file).Load();

                Assert.Equal("Light", loaded.Theme);
                Assert.Single(loaded.RoomCameras);
            }
            finally
            {
                if (File.Exists(file)) File.Delete(file);
            }
        }

        [Fact]
        public void SavingTheme_KeepsCameras()
        {
            string file = TempFile();
            try
            {
                var service = new ConfigService(file);
                service.Save(new AppConfig
                {
                    RoomCameras = new System.Collections.Generic.List<RoomCameraConfig>
                    {
                        new RoomCameraConfig { Id = "keep1", Name = "ROOM-CAM", Host = "192.168.1.163", Port = 1259 }
                    }
                });

                var config = service.Load();
                config.Theme = "Light";
                service.Save(config);

                var loaded = new ConfigService(file).Load();
                Assert.Equal("Light", loaded.Theme);
                Assert.Single(loaded.RoomCameras);
                Assert.Equal("keep1", loaded.RoomCameras[0].Id);
                Assert.Equal("ROOM-CAM", loaded.RoomCameras[0].Name);
                Assert.Equal(1259, loaded.RoomCameras[0].Port);
            }
            finally
            {
                if (File.Exists(file)) File.Delete(file);
            }
        }

        [Fact]
        public void Transport_SerialisesAsName()
        {
            string file = TempFile();
            try
            {
                var service = new ConfigService(file);
                service.Save(new AppConfig
                {
                    RoomCameras = new System.Collections.Generic.List<RoomCameraConfig>
                    {
                        new RoomCameraConfig { Transport = ViscaTransportKind.Udp }
                    }
                });

                string json = File.ReadAllText(file);

                Assert.Contains("\"Udp\"", json);
                Assert.DoesNotContain("\"Transport\": 0", json);
            }
            finally
            {
                if (File.Exists(file)) File.Delete(file);
            }
        }

        [Fact]
        public void CorruptCameraEntry_DoesNotDiscardOtherSettings()
        {
            string file = TempFile();
            try
            {
                File.WriteAllText(file,
                    "{\n  \"Theme\": \"Light\",\n  \"RoomCameras\": [\n" +
                    "    { \"Id\": \"first\", \"Name\": \"OR-1\", \"Host\": \"10.0.0.1\", \"Port\": 52381 },\n" +
                    "    { \"Id\": \"bad\", \"Name\": \"ROOM-CAM\", \"Port\": \"abc\" },\n" +
                    "    { \"Id\": \"last\", \"Name\": \"OR-2\", \"Host\": \"10.0.0.2\", \"Port\": 3001 }\n" +
                    "  ]\n}");

                // Only the corrupt camera entry is skipped; every other setting and valid camera is kept, in order.
                var loaded = new ConfigService(file).Load();

                Assert.NotNull(loaded);
                Assert.Equal("Light", loaded.Theme);
                Assert.Equal(2, loaded.RoomCameras.Count);
                Assert.Equal("first", loaded.RoomCameras[0].Id);
                Assert.Equal("10.0.0.1", loaded.RoomCameras[0].Host);
                Assert.Equal("last", loaded.RoomCameras[1].Id);
                Assert.Equal("10.0.0.2", loaded.RoomCameras[1].Host);
            }
            finally
            {
                if (File.Exists(file)) File.Delete(file);
            }
        }
    }
}
