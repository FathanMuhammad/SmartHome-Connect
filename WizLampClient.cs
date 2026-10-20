using System;
using System.Collections.Generic;
using System.Net;
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
        /// Discovers WiZ lamps on the local network via UDP broadcast (Port 38899)
        /// </summary>
        public async Task<List<WizLampDevice>> DiscoverLampsAsync(int timeoutMs = 2000)
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

            Log("Memulai pencarian lampu WiZ (UDP Broadcast)...");

            try
            {
                using var udpClient = new UdpClient();
                udpClient.EnableBroadcast = true;
                udpClient.Client.ReceiveTimeout = timeoutMs;

                // Send broadcast getSystemConfig
                string discoverMsg = "{\"method\":\"getSystemConfig\",\"params\":{}}";
                byte[] bytes = Encoding.UTF8.GetBytes(discoverMsg);
                var broadcastEndpoint = new IPEndPoint(IPAddress.Broadcast, WizPort);

                await udpClient.SendAsync(bytes, bytes.Length, broadcastEndpoint);

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
                                    Log($"Lampu ditemukan: {device.Name} ({ip}) [MAC: {mac}]");
                                }
                                else
                                {
                                    // Other WiZ lamp found
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
                        Log($"Peringatan saat scan: {ex.Message}", true);
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Gagal broadcast discovery: {ex.Message}", true);
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
                Log($"Gagal kirim ke {ip}: {ex.Message}", true);
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
            string cmd = $"{{\"method\":\"setPilot\",\"params\":{{\"dimming\":{dimming}}}}}";
            await SendCommandAsync(lamp.IpAddress, cmd);
        }

        public async Task SetKelvinAsync(WizLampDevice lamp, int kelvin)
        {
            kelvin = Math.Clamp(kelvin, 2000, 6500);
            lamp.Kelvin = kelvin;
            string cmd = $"{{\"method\":\"setPilot\",\"params\":{{\"temp\":{kelvin}}}}}";
            await SendCommandAsync(lamp.IpAddress, cmd);
        }

        public async Task SetRgbAsync(WizLampDevice lamp, byte r, byte g, byte b)
        {
            lamp.R = r;
            lamp.G = g;
            lamp.B = b;
            string cmd = $"{{\"method\":\"setPilot\",\"params\":{{\"r\":{r},\"g\":{g},\"b\":{b}}}}}";
            await SendCommandAsync(lamp.IpAddress, cmd);
        }

        /// <summary>
        /// Queries current status of a WiZ lamp (getPilot)
        /// </summary>
        public async Task<bool> RefreshLampStatusAsync(WizLampDevice lamp)
        {
            if (string.IsNullOrWhiteSpace(lamp.IpAddress)) return false;

            try
            {
                using var udpClient = new UdpClient();
                udpClient.Client.ReceiveTimeout = 1500;

                string query = "{\"method\":\"getPilot\"}";
                byte[] queryBytes = Encoding.UTF8.GetBytes(query);
                await udpClient.SendAsync(queryBytes, queryBytes.Length, lamp.IpAddress, WizPort);

                var receiveTask = udpClient.ReceiveAsync();
                var completedTask = await Task.WhenAny(receiveTask, Task.Delay(1500));

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
                            lamp.IsOn = stateProp.GetBoolean();
                        }
                        if (res.TryGetProperty("dimming", out var dimProp))
                        {
                            lamp.Brightness = dimProp.GetInt32();
                        }
                        if (res.TryGetProperty("temp", out var tempProp))
                        {
                            lamp.Kelvin = tempProp.GetInt32();
                        }
                        if (res.TryGetProperty("r", out var rProp) &&
                            res.TryGetProperty("g", out var gProp) &&
                            res.TryGetProperty("b", out var bProp))
                        {
                            lamp.R = (byte)rProp.GetInt32();
                            lamp.G = (byte)gProp.GetInt32();
                            lamp.B = (byte)bProp.GetInt32();
                        }

                        return true;
                    }
                }
            }
            catch
            {
                // Lamp might be offline
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
