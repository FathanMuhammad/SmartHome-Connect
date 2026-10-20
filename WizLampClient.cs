using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SmartHomeAcController
{
    public class WizLampDevice
    {
        public string Mac { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public bool IsOnline { get; set; } = false;
        public bool IsOn { get; set; } = false;
        public int Brightness { get; set; } = 100; // 10 - 100
        public int Kelvin { get; set; } = 4000;     // 2000 - 6500
        public byte R { get; set; } = 255;
        public byte G { get; set; } = 255;
        public byte B { get; set; } = 255;
    }

    public class WizLampClient
    {
        public const int WizPort = 38899;

        // Known default targets from user config
        public static readonly Dictionary<string, string> DefaultMacTargets = new(StringComparer.OrdinalIgnoreCase)
        {
            { "A8BB50B5A2E9", "Lampu Meja" },
            { "CC40858E7E82", "Lampu LED Strip" }
        };

        public event Action<string, bool>? OnLog;

        private void Log(string message, bool isError = false)
        {
            OnLog?.Invoke($"[WiZ {DateTime.Now:HH:mm:ss}] {message}", isError);
        }

        /// <summary>
        /// Collects all local subnet broadcast addresses (e.g. 192.168.1.255)
        /// </summary>
        private static List<IPAddress> GetSubnetBroadcastAddresses()
        {
            var list = new List<IPAddress> { IPAddress.Broadcast }; // 255.255.255.255

            try
            {
                foreach (var iface in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (iface.OperationalStatus != OperationalStatus.Up ||
                        iface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                        continue;

                    var ipProps = iface.GetIPProperties();
                    foreach (var u in ipProps.UnicastAddresses)
                    {
                        if (u.Address.AddressFamily == AddressFamily.InterNetwork && u.IPv4Mask != null)
                        {
                            byte[] ipBytes = u.Address.GetAddressBytes();
                            byte[] maskBytes = u.IPv4Mask.GetAddressBytes();
                            byte[] broadcastBytes = new byte[4];

                            for (int i = 0; i < 4; i++)
                            {
                                broadcastBytes[i] = (byte)(ipBytes[i] | ~maskBytes[i]);
                            }

                            var bcastIp = new IPAddress(broadcastBytes);
                            if (!list.Contains(bcastIp))
                            {
                                list.Add(bcastIp);
                            }
                        }
                    }
                }
            }
            catch
            {
                // Fallback to 255.255.255.255
            }

            return list;
        }

        /// <summary>
        /// Discovers WiZ lamps on the local network via Subnet Broadcast and direct unicast queries
        /// </summary>
        public async Task<List<WizLampDevice>> DiscoverLampsAsync(IEnumerable<string>? knownIps = null, int timeoutMs = 2500)
        {
            var discovered = new Dictionary<string, WizLampDevice>(StringComparer.OrdinalIgnoreCase);

            // Pre-seed known targets
            foreach (var kvp in DefaultMacTargets)
            {
                discovered[kvp.Key] = new WizLampDevice
                {
                    Mac = kvp.Key,
                    Name = kvp.Value,
                    IsOnline = false
                };
            }

            Log("Memulai pencarian lampu WiZ (Broadcast & Direct Query)...");

            try
            {
                using var udpClient = new UdpClient();
                udpClient.EnableBroadcast = true;
                udpClient.Client.ReceiveTimeout = timeoutMs;

                string discoverMsg = "{\"method\":\"getSystemConfig\",\"params\":{}}";
                byte[] bytes = Encoding.UTF8.GetBytes(discoverMsg);

                // 1. Send broadcast to 255.255.255.255 and all subnet broadcasts (e.g. 192.168.1.255)
                var broadcastAddresses = GetSubnetBroadcastAddresses();
                foreach (var bcast in broadcastAddresses)
                {
                    try
                    {
                        await udpClient.SendAsync(bytes, bytes.Length, new IPEndPoint(bcast, WizPort));
                    }
                    catch
                    {
                        // Ignore broadcast errors on secondary adapters
                    }
                }

                // 2. Also send direct unicast to any known IPs (guarantees discovery even if router blocks broadcast)
                if (knownIps != null)
                {
                    foreach (var ip in knownIps.Where(ip => !string.IsNullOrWhiteSpace(ip)))
                    {
                        try
                        {
                            await udpClient.SendAsync(bytes, bytes.Length, ip, WizPort);
                        }
                        catch
                        {
                            // Ignore unicast send errors
                        }
                    }
                }

                using var cts = new CancellationTokenSource(timeoutMs);

                while (!cts.IsCancellationRequested)
                {
                    try
                    {
                        var receiveTask = udpClient.ReceiveAsync();
                        var completedTask = await Task.WhenAny(receiveTask, Task.Delay(timeoutMs, cts.Token));

                        if (completedTask != receiveTask)
                        {
                            break; // Timeout reached
                        }

                        var result = await receiveTask;
                        string json = Encoding.UTF8.GetString(result.Buffer);
                        string ip = result.RemoteEndPoint.Address.ToString();

                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;

                        if (root.TryGetProperty("result", out var resProp) &&
                            resProp.TryGetProperty("mac", out var macProp))
                        {
                            string mac = macProp.GetString()?.ToUpperInvariant() ?? "";

                            if (!string.IsNullOrEmpty(mac))
                            {
                                if (discovered.TryGetValue(mac, out var device))
                                {
                                    device.IpAddress = ip;
                                    device.IsOnline = true;
                                    Log($"Lampu terdeteksi: {device.Name} di IP {ip}");
                                }
                                else
                                {
                                    var newDevice = new WizLampDevice
                                    {
                                        Mac = mac,
                                        Name = $"WiZ Lamp ({mac.Substring(Math.Max(0, mac.Length - 4))})",
                                        IpAddress = ip,
                                        IsOnline = true
                                    };
                                    discovered[mac] = newDevice;
                                    Log($"Lampu baru ditemukan: {newDevice.Name} ({ip})");
                                }
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Log($"Peringatan scan: {ex.Message}", true);
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Gagal proses discovery: {ex.Message}", true);
            }

            var list = new List<WizLampDevice>(discovered.Values);

            // Fetch initial status for online lamps
            foreach (var dev in list)
            {
                if (dev.IsOnline && !string.IsNullOrEmpty(dev.IpAddress))
                {
                    await RefreshLampStatusAsync(dev);
                }
            }

            return list;
        }

        /// <summary>
        /// Sends UDP command to a specific WiZ lamp
        /// </summary>
        public async Task<bool> SendCommandAsync(string ip, string jsonCommand)
        {
            if (string.IsNullOrWhiteSpace(ip)) return false;

            try
            {
                using var udpClient = new UdpClient();
                byte[] bytes = Encoding.UTF8.GetBytes(jsonCommand);
                await udpClient.SendAsync(bytes, bytes.Length, ip, WizPort);
                return true;
            }
            catch (Exception ex)
            {
                Log($"Gagal kirim UDP ke {ip}: {ex.Message}", true);
                return false;
            }
        }

        public async Task SetPowerAsync(WizLampDevice lamp, bool on)
        {
            lamp.IsOn = on;
            string cmd = $"{{\"method\":\"setState\",\"params\":{{\"state\":{(on ? "true" : "false")}}}}}";
            await SendCommandAsync(lamp.IpAddress, cmd);
            Log($"{lamp.Name}: Daya diubah ke {(on ? "ON" : "OFF")}");
        }

        public async Task SetBrightnessAsync(WizLampDevice lamp, int dimming)
        {
            dimming = Math.Clamp(dimming, 10, 100);
            lamp.Brightness = dimming;
            lamp.IsOn = true;
            // Include state: true so the light turns on/applies even if asleep
            string cmd = $"{{\"method\":\"setPilot\",\"params\":{{\"state\":true,\"dimming\":{dimming}}}}}";
            await SendCommandAsync(lamp.IpAddress, cmd);
        }

        public async Task SetKelvinAsync(WizLampDevice lamp, int kelvin)
        {
            kelvin = Math.Clamp(kelvin, 2000, 6500);
            lamp.Kelvin = kelvin;
            lamp.IsOn = true;
            string cmd = $"{{\"method\":\"setPilot\",\"params\":{{\"state\":true,\"temp\":{kelvin}}}}}";
            await SendCommandAsync(lamp.IpAddress, cmd);
        }

        public async Task SetRgbAsync(WizLampDevice lamp, byte r, byte g, byte b)
        {
            lamp.R = r;
            lamp.G = g;
            lamp.B = b;
            lamp.IsOn = true;
            string cmd = $"{{\"method\":\"setPilot\",\"params\":{{\"state\":true,\"r\":{r},\"g\":{g},\"b\":{b}}}}}";
            await SendCommandAsync(lamp.IpAddress, cmd);
            Log($"{lamp.Name}: Warna RGB diubah ke ({r},{g},{b})");
        }

        /// <summary>
        /// Queries current status of a WiZ lamp (getPilot) with retry
        /// </summary>
        public async Task<bool> RefreshLampStatusAsync(WizLampDevice lamp, int timeoutMs = 2500)
        {
            if (string.IsNullOrWhiteSpace(lamp.IpAddress)) return false;

            // Try up to 2 attempts
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    using var udpClient = new UdpClient();
                    udpClient.Client.ReceiveTimeout = timeoutMs;

                    string query = "{\"method\":\"getPilot\"}";
                    byte[] queryBytes = Encoding.UTF8.GetBytes(query);
                    await udpClient.SendAsync(queryBytes, queryBytes.Length, lamp.IpAddress, WizPort);

                    var receiveTask = udpClient.ReceiveAsync();
                    var completedTask = await Task.WhenAny(receiveTask, Task.Delay(timeoutMs));

                    if (completedTask == receiveTask)
                    {
                        var result = await receiveTask;
                        string response = Encoding.UTF8.GetString(result.Buffer);

                        using var doc = JsonDocument.Parse(response);
                        var root = doc.RootElement;

                        if (root.TryGetProperty("result", out var res))
                        {
                            lamp.IsOnline = true;

                            if (res.TryGetProperty("state", out var stateProp))
                            {
                                if (stateProp.ValueKind == JsonValueKind.True || stateProp.ValueKind == JsonValueKind.False)
                                {
                                    lamp.IsOn = stateProp.GetBoolean();
                                }
                                else if (stateProp.ValueKind == JsonValueKind.Number)
                                {
                                    lamp.IsOn = stateProp.GetInt32() == 1;
                                }
                            }

                            if (res.TryGetProperty("dimming", out var dimProp) && dimProp.ValueKind == JsonValueKind.Number)
                            {
                                lamp.Brightness = Math.Clamp(dimProp.GetInt32(), 10, 100);
                            }

                            if (res.TryGetProperty("temp", out var tempProp) && tempProp.ValueKind == JsonValueKind.Number)
                            {
                                int temp = tempProp.GetInt32();
                                if (temp >= 2000 && temp <= 6500)
                                {
                                    lamp.Kelvin = temp;
                                }
                            }

                            if (res.TryGetProperty("r", out var rProp) &&
                                res.TryGetProperty("g", out var gProp) &&
                                res.TryGetProperty("b", out var bProp))
                            {
                                lamp.R = (byte)Math.Clamp(rProp.GetInt32(), 0, 255);
                                lamp.G = (byte)Math.Clamp(gProp.GetInt32(), 0, 255);
                                lamp.B = (byte)Math.Clamp(bProp.GetInt32(), 0, 255);
                            }

                            return true;
                        }
                    }
                }
                catch
                {
                    // Retry once
                }
            }

            return false;
        }

        public static string KelvinToHex(int kelvin)
        {
            float ratio = (float)(kelvin - 2000) / (6500 - 2000);
            ratio = Math.Clamp(ratio, 0f, 1f);

            int r = 255;
            int g = (int)(180 + (255 - 180) * ratio);
            int b = (int)(100 + (255 - 100) * ratio);

            return $"#{r:X2}{g:X2}{b:X2}";
        }
    }
}
